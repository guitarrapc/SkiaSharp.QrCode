using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// rMQR module placement (ISO/IEC 23941 6.3, 7.7-7.9): function patterns, both
/// format-information copies, and the two-column zigzag data placement with the
/// single fixed data mask. Writes a byte-per-module core matrix (0 light, 1 dark,
/// row-major over the symbol width, quiet zone excluded).
/// </summary>
/// <remarks>
/// This is the readable reference implementation, deliberately per-module: it
/// writes every module (function and data) so the caller need not zero the buffer,
/// and it allocates nothing. The function-module predicate
/// (<see cref="IsFunctionModule"/>) and the mask (<see cref="GetMaskBit"/>) are the
/// single source of truth the matrix decoder reuses, so both sides always agree. A
/// fused fast path (as Micro QR's <c>PlaceSymbol</c> tiers) is a benchmark-driven
/// follow-up and must stay parity-tested against this implementation.
///
/// Geometry (0-based, h = height, w = width):
/// finder 7×7 at (0,0) with light separators col 7 (rows 0-7) and row 7 (cols 0-7);
/// sub-finder 5×5 at (h-5, w-5); timing patterns on rows 0 / h-1 (dark at even
/// columns) and cols 0 / w-1 (dark at even rows); corner patterns (0,w-2), (1,w-1)
/// dark with (1,w-2) light and (h-2,0), (h-1,1) dark with (h-2,1) light (on height 9
/// the separator row 7 overrides (h-2,0) to light); vertical
/// timing columns (RmQRConstants.GetAlignmentColumns) dark at even rows with a 3×3
/// alignment pattern (dark ring, light center) at rows 0-2 and h-3..h-1; format
/// copy 1 in rows 1-5 × cols 8-10 (bit = col-major index) plus col 11 rows 1-3
/// (bits 15-17); format copy 2 in rows h-6..h-2 × cols w-8..w-6 plus row h-6 cols
/// w-5..w-3. Data walks column pairs from (w-2, w-3) leftward, upward first, right
/// column first, skipping function modules; bits beyond the final message are
/// remainder bits and are light before masking.
/// </remarks>
internal static class RmQRModulePlacer
{
    /// <summary>The single rMQR data mask (ISO/IEC 23941 7.8): dark when ((row / 2) + (col / 3)) is even.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetMaskBit(int row, int col) => (((row >> 1) + col / 3) & 1) == 0;

    /// <summary>
    /// Whether (row, col) is a function module (finder, separators, sub-finder,
    /// timing, corners, alignment / vertical timing, or format information) for the
    /// version; everything else is a data or remainder module.
    /// </summary>
    public static bool IsFunctionModule(RmQRVersion version, int row, int col)
    {
        var height = RmQRConstants.GetHeight(version);
        var width = RmQRConstants.GetWidth(version);

        // Edge timing patterns (row 0 / h-1, col 0 / w-1) include the corner cells.
        if (row == 0 || row == height - 1 || col == 0 || col == width - 1)
            return true;
        // Finder (7×7) with its separators: col 7 rows 0-7 and row 7 cols 0-7.
        if (row <= 7 && col <= 7)
            return true;
        // Sub-finder 5×5, bottom-right.
        if (row >= height - 5 && col >= width - 5)
            return true;
        // Corner patterns' inner light modules.
        if ((row == 1 && col == width - 2) || (row == height - 2 && col == 1))
            return true;
        // Format information, finder side.
        if (row >= 1 && row <= 5 && col >= 8 && col <= 10)
            return true;
        if (row >= 1 && row <= 3 && col == 11)
            return true;
        // Format information, sub-finder side.
        if (row >= height - 6 && row <= height - 2 && col >= width - 8 && col <= width - 6)
            return true;
        if (row == height - 6 && col >= width - 5 && col <= width - 3)
            return true;
        // Vertical timing columns and their 3×3 alignment patterns at both ends.
        var alignment = RmQRConstants.GetAlignmentColumns(version);
        for (var i = 0; i < alignment.Length; i++)
        {
            var c = alignment[i];
            if (col == c)
                return true;
            if ((row <= 2 || row >= height - 3) && (col == c - 1 || col == c + 1))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the complete symbol: function patterns, both format copies, and the
    /// masked final message (data + ECC + remainder) into <paramref name="core"/>
    /// (byte per module, row-major over the version's width; every module is written).
    /// </summary>
    /// <param name="core">At least width × height bytes.</param>
    /// <param name="version">Symbol version.</param>
    /// <param name="eccLevel">ECC level (format information only; the message is already ECC-encoded).</param>
    /// <param name="finalMessage">Interleaved final message from <see cref="RmQRCodewordEncoder"/> (at least total codewords bytes).</param>
    public static void PlaceSymbol(Span<byte> core, RmQRVersion version, RmQREccLevel eccLevel, ReadOnlySpan<byte> finalMessage)
    {
        var height = RmQRConstants.GetHeight(version);
        var width = RmQRConstants.GetWidth(version);
        var totalCodewords = RmQRConstants.GetTotalCodewordCount(version);
        if (core.Length < width * height)
            throw new ArgumentException($"Core buffer too small: required {width * height} bytes ({width}x{height}), got {core.Length}.", nameof(core));
        if (finalMessage.Length < totalCodewords)
            throw new ArgumentException($"Final message too short: required {totalCodewords} codewords, got {finalMessage.Length}.", nameof(finalMessage));

        core = core.Slice(0, width * height);
        PlaceFunctionModules(core, version, height, width);
        PlaceFormat(core, version, eccLevel, height, width);
        PlaceData(core, version, height, width, finalMessage.Slice(0, totalCodewords));
    }

    /// <summary>Paints every function module (dark or light) for the version.</summary>
    internal static void PlaceFunctionModules(Span<byte> core, RmQRVersion version, int height, int width)
    {
        // Edge timing patterns first; finder, sub-finder, corners and alignment
        // patterns overwrite their cells afterwards.
        for (var col = 0; col < width; col++)
        {
            var dark = (byte)((col & 1) == 0 ? 1 : 0);
            core[col] = dark;
            core[(height - 1) * width + col] = dark;
        }
        for (var row = 0; row < height; row++)
        {
            var dark = (byte)((row & 1) == 0 ? 1 : 0);
            core[row * width] = dark;
            core[row * width + width - 1] = dark;
        }

        // Vertical timing columns with a 3×3 alignment pattern (dark ring, light center) at both ends.
        var alignment = RmQRConstants.GetAlignmentColumns(version);
        for (var i = 0; i < alignment.Length; i++)
        {
            int c = alignment[i];
            for (var row = 0; row < height; row++)
                core[row * width + c] = (byte)((row & 1) == 0 ? 1 : 0);
            for (var dr = 0; dr < 3; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    var dark = (byte)(dr == 1 && dc == 0 ? 0 : 1);
                    core[dr * width + c + dc] = dark;
                    core[(height - 3 + dr) * width + c + dc] = dark;
                }
            }
        }

        // Corner patterns: top-right and bottom-left. Painted BEFORE the finder
        // separators: on height 9 the bottom-left corner cell (h-2, 0) = (7, 0) lies on
        // separator row 7, and the separator (light) wins there (both external lineages agree).
        core[width - 2] = 1;
        core[width - 1] = 1;
        core[width + width - 1] = 1;
        core[width + width - 2] = 0;
        core[(height - 1) * width] = 1;
        core[(height - 1) * width + 1] = 1;
        core[(height - 2) * width] = 1;
        core[(height - 2) * width + 1] = 0;

        // Finder 7×7: dark border, light ring, dark 3×3 center; light separators.
        for (var row = 0; row < 7; row++)
        {
            for (var col = 0; col < 7; col++)
            {
                var ring = Math.Max(Math.Abs(row - 3), Math.Abs(col - 3));
                core[row * width + col] = (byte)(ring == 2 ? 0 : 1);
            }
        }
        for (var row = 0; row < Math.Min(8, height); row++)
            core[row * width + 7] = 0;
        if (height > 7)
        {
            for (var col = 0; col <= 7; col++)
                core[7 * width + col] = 0;
        }

        // Sub-finder 5×5: dark border, light ring, dark center.
        for (var row = 0; row < 5; row++)
        {
            for (var col = 0; col < 5; col++)
            {
                var ring = Math.Max(Math.Abs(row - 2), Math.Abs(col - 2));
                core[(height - 5 + row) * width + width - 5 + col] = (byte)(ring == 1 ? 0 : 1);
            }
        }

    }

    /// <summary>Writes both 18-bit format-information copies.</summary>
    internal static void PlaceFormat(Span<byte> core, RmQRVersion version, RmQREccLevel eccLevel, int height, int width)
    {
        var finderSide = RmQRConstants.GetFormatBits(version, eccLevel, subFinderSide: false);
        var subFinderSide = RmQRConstants.GetFormatBits(version, eccLevel, subFinderSide: true);

        // Bits 0-14: five rows × three columns, column-major (bit = col * 5 + row).
        for (var c = 0; c < 3; c++)
        {
            for (var r = 0; r < 5; r++)
            {
                var bit = c * 5 + r;
                core[(r + 1) * width + (c + 8)] = (byte)((finderSide >> bit) & 1);
                core[(height - 6 + r) * width + (width - 8 + c)] = (byte)((subFinderSide >> bit) & 1);
            }
        }
        // Bits 15-17.
        for (var k = 0; k < 3; k++)
        {
            core[(k + 1) * width + 11] = (byte)((finderSide >> (15 + k)) & 1);
            core[(height - 6) * width + (width - 5 + k)] = (byte)((subFinderSide >> (15 + k)) & 1);
        }
    }

    /// <summary>
    /// Two-column zigzag data placement with the fixed mask: column pairs from
    /// (w-2, w-3) leftward, upward first, right column first, function modules
    /// skipped; bits beyond the message are remainder bits (light before masking).
    /// </summary>
    internal static void PlaceData(Span<byte> core, RmQRVersion version, int height, int width, ReadOnlySpan<byte> finalMessage)
    {
        var bitIndex = 0;
        var bitCount = finalMessage.Length * 8;
        var upward = true;
        for (var col = width - 2; col >= 1; col -= 2)
        {
            for (var step = 0; step < height; step++)
            {
                var row = upward ? height - 1 - step : step;
                for (var c = col; c >= col - 1; c--)
                {
                    if (IsFunctionModule(version, row, c))
                        continue;

                    var bit = 0;
                    if (bitIndex < bitCount)
                    {
                        bit = (finalMessage[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1;
                    }
                    bitIndex++;

                    core[row * width + c] = (byte)(bit ^ (GetMaskBit(row, c) ? 1 : 0));
                }
            }
            upward = !upward;
        }
    }
}
