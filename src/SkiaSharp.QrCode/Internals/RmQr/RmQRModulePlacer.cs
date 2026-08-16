using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// rMQR module placement (ISO/IEC 23941 6.3, 7.7-7.9): function patterns, both
/// format-information copies, and the two-column zigzag data placement with the
/// single fixed data mask. Writes a byte-per-module core matrix (0 light, 1 dark,
/// row-major over the symbol width, quiet zone excluded).
/// </summary>
/// <remarks>
/// Two implementations live here and are held to byte parity by
/// <c>RmQRModulePlacerParityTest</c>:
/// <list type="bullet">
/// <item><see cref="PlaceSymbolReference"/> — the readable per-module reference
/// (<see cref="PlaceFunctionModules"/>, <see cref="PlaceFormat"/>,
/// <see cref="PlaceData"/>). The function-module predicate
/// (<see cref="IsFunctionModule"/>) and the mask (<see cref="GetMaskBit"/>) are the
/// single source of truth the matrix decoder reuses, so both sides always agree.</item>
/// <item><see cref="PlaceSymbol"/> — the fast path (benchmark-driven, 16-27x over the
/// reference). Everything derived from the version alone is built once per version
/// by the reference code and cached (<see cref="Layout"/>): a painted template per
/// (version, ECC) with every function and format module, the zigzag order as core
/// indices, the mask value per data position, and the column-pair segmentation.
/// Placement is then a template copy, one vector pass that expands the message bits
/// to bytes and XORs the masks (AVX2 / SSSE3, scalar otherwise), and a store pass:
/// "clean" column pairs (both columns pure data on rows 1..h-2) are written as one
/// byte-swapped 16-bit store per row straight from the masked bit array, the
/// remaining pairs (finder / format / alignment / sub-finder neighbours) as a table
/// scatter. Bit array scratch is a fixed 512-byte stack budget with a pool fallback
/// (versions above 63 codewords). Zero allocations after the one-time tables.</item>
/// </list>
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
    /// Fast path; see the class remarks.
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

        var layout = GetLayout(version);
        var count = layout.DataModuleCount;

        // Bit array scratch: one byte per data module (message bits, then the light
        // remainder bits), mask already applied. Fixed stack budget, pool fallback.
        if (count <= StackBitBudget)
        {
            Span<byte> bits = stackalloc byte[StackBitBudget];
            PlaceCore(core.Slice(0, width * height), layout, layout.GetTemplate(version, eccLevel), height, width, finalMessage.Slice(0, totalCodewords), bits);
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(count + VectorSlack);
            try
            {
                PlaceCore(core.Slice(0, width * height), layout, layout.GetTemplate(version, eccLevel), height, width, finalMessage.Slice(0, totalCodewords), rented);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    // 512 B covers every version with <= 63 total codewords (504 data modules + slack);
    // larger symbols rent. The AVX2 expand step writes 32 bytes per 4 message bytes, so
    // the scratch carries 32 bytes of slack past the module count.
    private const int StackBitBudget = 512;
    private const int VectorSlack = 32;

    /// <summary>
    /// Reference composition (per-module painters), the oracle the fast path is
    /// parity-tested against; also builds the cached tables.
    /// </summary>
    internal static void PlaceSymbolReference(Span<byte> core, RmQRVersion version, RmQREccLevel eccLevel, ReadOnlySpan<byte> finalMessage)
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

    // ---------------------------------------------------------------
    // Fast path
    // ---------------------------------------------------------------

    private static void PlaceCore(Span<byte> core, Layout layout, byte[] template, int height, int width, ReadOnlySpan<byte> finalMessage, Span<byte> bits)
    {
        template.AsSpan().CopyTo(core);
        ExpandBitsMasked(finalMessage, layout.Masks, layout.DataModuleCount, bits);
        ScatterPairs(ref MemoryMarshal.GetReference(core), ref MemoryMarshal.GetReference(bits), layout, height, width);
    }

    /// <summary>
    /// bits[i] = message bit i (MSB first) XOR mask[i] for i &lt; 8 × message length;
    /// remainder positions up to <paramref name="count"/> get the mask only (light).
    /// One vector pass: pshufb replicates each message byte over 8 lanes, an AND with
    /// the per-lane bit mask + compare-equal yields 0/1 bytes, XOR with the mask table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExpandBitsMasked(ReadOnlySpan<byte> message, byte[] masks, int count, Span<byte> bits)
    {
        var byteCount = message.Length;
        ref var src = ref MemoryMarshal.GetReference(message);
        ref var msk = ref MemoryMarshal.GetReference(masks.AsSpan());
        ref var dst = ref MemoryMarshal.GetReference(bits);
        var k = 0;
#if NET8_0_OR_GREATER
        if (Avx2.IsSupported)
        {
            // 4 message bytes -> 32 module bytes per step; the uint broadcast puts the
            // same 4 bytes in both 128-bit lanes, and the in-lane shuffle picks byte 0..3
            var sel = Vector256.Create((byte)0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3);
            var bitm = Vector256.Create((byte)128, 64, 32, 16, 8, 4, 2, 1, 128, 64, 32, 16, 8, 4, 2, 1, 128, 64, 32, 16, 8, 4, 2, 1, 128, 64, 32, 16, 8, 4, 2, 1);
            var one = Vector256.Create((byte)1);
            for (; k + 4 <= byteCount; k += 4)
            {
                var v = Vector256.Create(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref src, k))).AsByte();
                var m = Avx2.Shuffle(v, sel) & bitm;
                ((Vector256.Equals(m, bitm) & one) ^ Vector256.LoadUnsafe(ref msk, (nuint)(k * 8))).StoreUnsafe(ref dst, (nuint)(k * 8));
            }
        }
        if (Ssse3.IsSupported)
        {
            var sel = Vector128.Create((byte)0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1);
            var bitm = Vector128.Create((byte)128, 64, 32, 16, 8, 4, 2, 1, 128, 64, 32, 16, 8, 4, 2, 1);
            var one = Vector128.Create((byte)1);
            for (; k + 2 <= byteCount; k += 2)
            {
                var v = Vector128.Create(Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref src, k))).AsByte();
                var m = Ssse3.Shuffle(v, sel) & bitm;
                ((Vector128.Equals(m, bitm) & one) ^ Vector128.LoadUnsafe(ref msk, (nuint)(k * 8))).StoreUnsafe(ref dst, (nuint)(k * 8));
            }
        }
#endif
        for (; k < byteCount; k++)
        {
            int b = Unsafe.Add(ref src, k);
            ref var d = ref Unsafe.Add(ref dst, k * 8);
            ref var m = ref Unsafe.Add(ref msk, k * 8);
            d = (byte)(((b >> 7) & 1) ^ m);
            Unsafe.Add(ref d, 1) = (byte)(((b >> 6) & 1) ^ Unsafe.Add(ref m, 1));
            Unsafe.Add(ref d, 2) = (byte)(((b >> 5) & 1) ^ Unsafe.Add(ref m, 2));
            Unsafe.Add(ref d, 3) = (byte)(((b >> 4) & 1) ^ Unsafe.Add(ref m, 3));
            Unsafe.Add(ref d, 4) = (byte)(((b >> 3) & 1) ^ Unsafe.Add(ref m, 4));
            Unsafe.Add(ref d, 5) = (byte)(((b >> 2) & 1) ^ Unsafe.Add(ref m, 5));
            Unsafe.Add(ref d, 6) = (byte)(((b >> 1) & 1) ^ Unsafe.Add(ref m, 6));
            Unsafe.Add(ref d, 7) = (byte)((b & 1) ^ Unsafe.Add(ref m, 7));
        }
        // remainder bits (0..7 per version): light before masking -> mask only
        for (var i = byteCount * 8; i < count; i++)
            Unsafe.Add(ref dst, i) = Unsafe.Add(ref msk, i);
    }

    /// <summary>
    /// Store pass. Clean pairs: the walk visits (row, col) then (row, col-1), so the two
    /// consecutive bit-array bytes land at core[row, col-1..col] byte-swapped, one
    /// 16-bit store per row walking the rows in the pair's direction. Other pairs:
    /// scatter through the index table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScatterPairs(ref byte dest, ref byte src, Layout layout, int height, int width)
    {
        ref var idx = ref MemoryMarshal.GetReference(layout.Index.AsSpan());
        var pairs = layout.Pairs;
        var rows = (nuint)(height - 2);
        var stride = (nuint)width;
        for (var p = 0; p < pairs.Length; p++)
        {
            var seg = pairs[p];
            if (seg.Clean)
            {
                var k = (nuint)seg.Start;
                if (seg.Upward)
                {
                    ref var d = ref Unsafe.Add(ref dest, (nuint)((height - 2) * width + seg.Col - 1));
                    for (nuint r = 0; r < rows; r++)
                    {
                        Unsafe.WriteUnaligned(ref d, SwapPair(Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref src, k))));
                        k += 2;
                        d = ref Unsafe.Subtract(ref d, stride);
                    }
                }
                else
                {
                    ref var d = ref Unsafe.Add(ref dest, (nuint)(width + seg.Col - 1));
                    for (nuint r = 0; r < rows; r++)
                    {
                        Unsafe.WriteUnaligned(ref d, SwapPair(Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref src, k))));
                        k += 2;
                        d = ref Unsafe.Add(ref d, stride);
                    }
                }
            }
            else
            {
                var end = (nuint)(seg.Start + seg.Count);
                for (var i = (nuint)seg.Start; i < end; i++)
                {
                    Unsafe.Add(ref dest, Unsafe.Add(ref idx, i)) = Unsafe.Add(ref src, i);
                }
            }
        }
    }

    // The bit array holds (col module, col-1 module) in walk order; memory wants
    // col-1 first. Read and write go through the same host endianness, so a
    // ReverseEndianness in between always swaps the two BYTES in memory order,
    // whatever the host is — the swap is unconditional by design.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort SwapPair(ushort v) => BinaryPrimitives.ReverseEndianness(v);

    // ---------------------------------------------------------------
    // Per-version tables (built once from the reference painters, so they are
    // correct by construction; published with a volatile write — a benign race
    // builds identical tables twice). Memory per version: w×h bytes per ECC template
    // used + 3 bytes per data module (index + mask) + a few pair descriptors:
    // <= 8.5 KB for R17x139, ~120 KB if every version and ECC were ever used.
    // ---------------------------------------------------------------

    /// <summary>A column pair (col, col-1) of the zigzag walk.</summary>
    private readonly struct PairSegment
    {
        public readonly int Col;
        public readonly int Start;   // first walk position of the pair
        public readonly int Count;   // data modules in the pair
        public readonly bool Upward;
        public readonly bool Clean;  // rows 1..h-2 are data in both columns (Count == 2 * (h - 2))

        public PairSegment(int col, int start, int count, bool upward, bool clean)
        {
            Col = col;
            Start = start;
            Count = count;
            Upward = upward;
            Clean = clean;
        }
    }

    private sealed class Layout
    {
        public readonly ushort[] Index;      // core index per walk position
        public readonly byte[] Masks;        // mask bit per walk position
        public readonly int DataModuleCount; // 8 * total codewords + remainder bits
        public readonly PairSegment[] Pairs;
        private readonly byte[] _functionTemplate; // function modules painted, format modules 0
        private byte[]? _templateM;
        private byte[]? _templateH;

        public Layout(byte[] functionTemplate, ushort[] index, byte[] masks, PairSegment[] pairs)
        {
            _functionTemplate = functionTemplate;
            Index = index;
            Masks = masks;
            DataModuleCount = index.Length;
            Pairs = pairs;
        }

        /// <summary>Function template with the ECC level's two format copies painted.</summary>
        public byte[] GetTemplate(RmQRVersion version, RmQREccLevel eccLevel)
        {
            ref var slot = ref (eccLevel == RmQREccLevel.M ? ref _templateM : ref _templateH);
            var t = Volatile.Read(ref slot);
            if (t is not null) return t;
            t = (byte[])_functionTemplate.Clone();
            PlaceFormat(t, version, eccLevel, RmQRConstants.GetHeight(version), RmQRConstants.GetWidth(version));
            Volatile.Write(ref slot, t);
            return t;
        }
    }

    private static readonly Layout?[] layouts = new Layout?[RmQRConstants.VersionCount + 1];

    private static Layout GetLayout(RmQRVersion version)
    {
        ref var slot = ref layouts[(int)version];
        var layout = Volatile.Read(ref slot);
        if (layout is not null) return layout;
        layout = BuildLayout(version);
        Volatile.Write(ref slot, layout);
        return layout;
    }

    private static Layout BuildLayout(RmQRVersion version)
    {
        var height = RmQRConstants.GetHeight(version);
        var width = RmQRConstants.GetWidth(version);
        var functionTemplate = new byte[width * height];
        PlaceFunctionModules(functionTemplate, version, height, width);

        // The same walk as PlaceData, recording core index + mask per data position and
        // the per-pair shape.
        var index = new List<ushort>();
        var masks = new List<byte>();
        var pairs = new List<PairSegment>();
        var upward = true;
        for (var col = width - 2; col >= 1; col -= 2)
        {
            var start = index.Count;
            var clean = true;
            for (var step = 0; step < height; step++)
            {
                var row = upward ? height - 1 - step : step;
                for (var c = col; c >= col - 1; c--)
                {
                    var isFunction = IsFunctionModule(version, row, c);
                    if (isFunction)
                    {
                        if (row >= 1 && row <= height - 2) clean = false;
                        continue;
                    }
                    index.Add((ushort)(row * width + c));
                    masks.Add((byte)(GetMaskBit(row, c) ? 1 : 0));
                }
            }
            pairs.Add(new PairSegment(col, start, index.Count - start, upward, clean));
            upward = !upward;
        }

        return new Layout(functionTemplate, index.ToArray(), masks.ToArray(), pairs.ToArray());
    }

    // ---------------------------------------------------------------
    // Reference painters (per module)
    // ---------------------------------------------------------------

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
