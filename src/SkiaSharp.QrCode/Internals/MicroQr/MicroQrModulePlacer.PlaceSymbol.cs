using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SkiaSharp.QrCode.Internals.MicroQr;

/// <summary>
/// Fused fast-path placement pipeline for Micro QR
/// (<see cref="PlaceSymbol"/>): function patterns, data placement, mask
/// selection/application and format information in one pass.
///
/// Produces matrices byte-identical to the per-module reference methods in
/// MicroQrModulePlacer.cs (guarded by MicroQrModulePlacerParityTest).
/// Measured 3.1-4.0x over the reference across M1-M4, zero allocations
/// (see the micro-optimization findings log):
/// <list type="bullet">
/// <item>The transmission stream is at most 192 bits (M4), so it is packed once
/// into three ulongs and consumed MSB-first from a register accumulator instead
/// of per-bit indexed loads with a data/ecc branch.</item>
/// <item>Column pairs are (even, odd) and never straddle the finder boundary:
/// <c>right &gt;= 10</c> frees rows 1..size-1, <c>right &lt;= 8</c> frees rows
/// 9..size-1. Placement runs 2 bits per row with no per-module function-region
/// predicate; the stream fills the free modules exactly (ISO capacity tables,
/// validated up front), so no remaining-bits guard is needed either.</item>
/// <item>Mask scoring reads only the two symbol edges (ISO 7.8.3): both edges
/// are packed into one ulong each and every candidate's SUM1/SUM2 is one XOR +
/// popcount against a static per-(size, mask) edge table — the dominant cost of
/// the reference pipeline (4 patterns x 2 edges x a mask switch per module).</item>
/// <item>Sizes 13/15/17 run the whole pipeline on bit-packed rows (one ulong
/// per row, the ModulePlacer.Masking representation): function pattern = static
/// packed constants, placement = shift+or, mask apply = one XOR per row via
/// 12-row templates, format = bit pokes, then one SWAR unpack pass to the byte
/// matrix. Size 11 stays byte-domain — the unpack pass never amortizes on a
/// 121-byte matrix, and the per-module mask apply wins there (measured).</item>
/// <item>Pure scalar ulong arithmetic: identical on every TFM and
/// NativeAOT/trimming-safe; all tables are small static arrays built by
/// ordinary code from the reference implementations.</item>
/// </list>
/// </summary>
internal static partial class MicroQrModulePlacer
{
    /// <summary>
    /// Runs the full placement pipeline into a zeroed core matrix: function
    /// patterns, data/ECC codeword placement, mask selection and application,
    /// and format information. Returns the selected mask pattern (0-3).
    /// </summary>
    /// <param name="matrix">Zeroed core matrix, at least size*size bytes.</param>
    /// <param name="size">Core side length in modules (11/13/15/17).</param>
    /// <param name="dataCodewords">Data codewords including padding.</param>
    /// <param name="eccCodewords">Error correction codewords.</param>
    /// <param name="dataBitCount">
    /// Number of DATA bits to emit — for M1/M3 this stops after the high nibble
    /// of the final (4-bit) data codeword; its forced-zero low nibble is never
    /// placed.
    /// </param>
    /// <param name="version">Micro QR version (drives the format information).</param>
    /// <param name="eccLevel">ECC level (drives the format information).</param>
    public static int PlaceSymbol(Span<byte> matrix, int size, ReadOnlySpan<byte> dataCodewords, ReadOnlySpan<byte> eccCodewords, int dataBitCount, MicroQrVersion version, MicroQrEccLevel eccLevel)
    {
        // The fast paths below index by arithmetic the JIT cannot bounds-prove
        // (flat zigzag indices, 8-byte unaligned unpack writes), so the whole
        // traversal domain is validated here instead of per access.
        if (MicroQrConstants.VersionFromSize(size) == 0)
            throw new ArgumentOutOfRangeException(nameof(size), $"size must be a Micro QR size (11/13/15/17), got {size}");
        if (matrix.Length < size * size)
            throw new ArgumentException($"matrix too small: required {size * size}, got {matrix.Length}", nameof(matrix));
        if (dataBitCount < 0 || dataCodewords.Length * 8 < dataBitCount)
            throw new ArgumentException($"dataCodewords too small: required {dataBitCount} bits, got {dataCodewords.Length * 8}", nameof(dataCodewords));

        // Free data modules: size^2 minus row 0 (size), the rest of column 0
        // (size-1) and rows 1..8 x cols 1..8 (64). The ISO capacity tables make
        // the stream fill this exactly; the placement loops rely on it.
        var totalBits = dataBitCount + eccCodewords.Length * 8;
        var freeModules = size * size - 2 * size - 63;
        if (totalBits != freeModules)
            throw new ArgumentException($"stream length mismatch: {totalBits} bits for {freeModules} free modules (size {size})", nameof(eccCodewords));

        Span<ulong> stream = stackalloc ulong[3];
        PackStream(stream, dataCodewords, eccCodewords, dataBitCount);

        return size == 11
            ? PlaceSymbolBytes(matrix, size, stream, version, eccLevel)
            : PlaceSymbolPacked(matrix, size, stream, version, eccLevel);
    }

    /// <summary>
    /// Packs the transmission stream (data bits MSB-first, cut at
    /// <paramref name="dataBitCount"/>, then ECC bits) into three MSB-aligned
    /// ulongs. The stream is byte-aligned except after an M1/M3 half codeword,
    /// where the remaining bytes are merged with a 4-bit carry.
    /// </summary>
    private static void PackStream(Span<ulong> stream, ReadOnlySpan<byte> dataCodewords, ReadOnlySpan<byte> eccCodewords, int dataBitCount)
    {
        Span<byte> tmp = stackalloc byte[24];
        tmp.Clear();
        var fullDataBytes = dataBitCount >> 3;
        dataCodewords.Slice(0, fullDataBytes).CopyTo(tmp);
        if ((dataBitCount & 7) != 0)
        {
            var t = fullDataBytes;
            var carry = (byte)(dataCodewords[fullDataBytes] & 0xF0);
            for (var i = 0; i < eccCodewords.Length; i++)
            {
                tmp[t++] = (byte)(carry | (eccCodewords[i] >> 4));
                carry = (byte)(eccCodewords[i] << 4);
            }
            tmp[t] = carry;
        }
        else
        {
            eccCodewords.CopyTo(tmp.Slice(fullDataBytes));
        }
        stream[0] = BinaryPrimitives.ReadUInt64BigEndian(tmp);
        stream[1] = BinaryPrimitives.ReadUInt64BigEndian(tmp.Slice(8));
        stream[2] = BinaryPrimitives.ReadUInt64BigEndian(tmp.Slice(16));
    }

    // ---------------------------------------------------------------
    // Byte-domain path (size 11)
    // ---------------------------------------------------------------

    private static int PlaceSymbolBytes(Span<byte> matrix, int size, ReadOnlySpan<ulong> stream, MicroQrVersion version, MicroQrEccLevel eccLevel)
    {
        // Function patterns: rows 0..8 are a per-size constant block; below it
        // only the column-0 timing modules (dark at even rows) remain.
        FuncTopRows(size).CopyTo(matrix);
        for (var i = 10; i < size; i += 2)
        {
            matrix[i * size] = 1;
        }

        // Data placement: 2 stream bits per free row of each column pair.
        var acc = stream[0];
        var w1 = stream[1];
        var w2 = stream[2];
        var accBits = 64;
        var upward = true;

        for (var right = size - 1; right >= 2; right -= 2)
        {
            var rowStart = right >= 10 ? 1 : 9;
            var rows = size - rowStart;
            var b = upward ? (size - 1) * size + right : rowStart * size + right;
            var bStep = upward ? -size : size;

            for (var i = 0; i < rows; i++, b += bStep)
            {
                if (accBits == 0)
                {
                    acc = w1;
                    w1 = w2;
                    accBits = 64;
                }
                matrix[b] = (byte)(acc >> 63);
                matrix[b - 1] = (byte)((acc >> 62) & 1);
                acc <<= 2;
                accBits -= 2;
            }
            upward = !upward;
        }

        // Score from the packed edges, then apply per module: at size 11 the
        // ~36 data modules cost less than the template/unpack machinery of the
        // packed path (measured).
        var last = size - 1;
        var lastRowOffset = last * size;
        ulong colDark = 0;
        ulong rowDark = 0;
        for (var i = 1; i < size; i++)
        {
            colDark |= (ulong)matrix[i * size + last] << i;
            rowDark |= (ulong)matrix[lastRowOffset + i] << i;
        }
        var mask = SelectMaskFromEdges(colDark, rowDark, size);

        for (var row = 1; row < size; row++)
        {
            var rowOffset = row * size;
            var colStart = row <= 8 ? 9 : 1;
            for (var col = colStart; col < size; col++)
            {
                if (GetMaskBit(mask, row, col))
                {
                    matrix[rowOffset + col] ^= 1;
                }
            }
        }

        PlaceFormat(matrix, size, MicroQrConstants.GetFormatBits(version, eccLevel, mask));
        return mask;
    }

    /// <summary>
    /// Scores the four mask patterns from the packed symbol edges
    /// (ISO/IEC 18004 Section 7.8.3: SUM1 = right edge dark count, SUM2 = lower
    /// edge dark count, score = min*16 + max, HIGHEST wins, ties keep the lowest
    /// pattern number). Each candidate is one XOR + popcount per edge against
    /// the static edge tables; bit 0 of both inputs must be clear (row/column 0
    /// are excluded from the edge sums).
    /// </summary>
    private static int SelectMaskFromEdges(ulong colDark, ulong rowDark, int size)
    {
        var edgeBase = ((size - 11) >> 1) * 4;
        var bestMask = 0;
        var bestScore = -1;
        for (var mask = 0; mask < 4; mask++)
        {
            var sum1 = PopCount(colDark ^ _edgeColBits[edgeBase + mask]);
            var sum2 = PopCount(rowDark ^ _edgeRowBits[edgeBase + mask]);

            var score = sum1 <= sum2 ? sum1 * 16 + sum2 : sum2 * 16 + sum1;
            if (score > bestScore)
            {
                bestScore = score;
                bestMask = mask;
            }
        }

        return bestMask;
    }

    // ---------------------------------------------------------------
    // Packed-row path (sizes 13/15/17)
    // ---------------------------------------------------------------

    private static int PlaceSymbolPacked(Span<byte> matrix, int size, ReadOnlySpan<ulong> stream, MicroQrVersion version, MicroQrEccLevel eccLevel)
    {
        var acc = stream[0];
        var w1 = stream[1];
        var w2 = stream[2];
        var accBits = 64;

        // One ulong per row, bit c = column c; seeded with the function pattern.
        Span<ulong> rows = stackalloc ulong[17];
        rows = rows.Slice(0, size);
        FuncPackedRows(size).CopyTo(rows);

        // Data placement: the pair's 2 bits land at columns (right, right-1).
        var upward = true;
        for (var right = size - 1; right >= 2; right -= 2)
        {
            var rowStart = right >= 10 ? 1 : 9;
            var row = upward ? size - 1 : rowStart;
            var rowEnd = upward ? rowStart - 1 : size;
            var step = upward ? -1 : 1;

            for (; row != rowEnd; row += step)
            {
                if (accBits == 0)
                {
                    acc = w1;
                    w1 = w2;
                    accBits = 64;
                }
                rows[row] |= ((acc >> 62) & 3) << (right - 1);
                acc <<= 2;
                accBits -= 2;
            }
            upward = !upward;
        }

        // Scoring: edges straight out of the packed rows (bit 0 = the column-0
        // function module, excluded from both edge sums).
        var last = size - 1;
        ulong colDark = 0;
        for (var i = 1; i < size; i++)
        {
            colDark |= ((rows[i] >> last) & 1) << i;
        }
        var rowDark = rows[last] & ~1ul;

        var mask = SelectMaskFromEdges(colDark, rowDark, size);

        // Apply: one template XOR per row, masked to the data region.
        var rowMask = (1ul << size) - 1;
        var tplBase = mask * 12;
        for (var row = 1; row < size; row++)
        {
            var colStart = row <= 8 ? 9 : 1;
            var allowed = rowMask & ~((1ul << colStart) - 1);
            var tplRow = row < 12 ? row : row - 12;
            rows[row] ^= _maskTemplates12[tplBase + tplRow] & allowed;
        }

        // Format information: row 8 cols 1..8 carry bits 14..7, col 8 rows 7..1
        // carry bits 6..0 (same coordinates as PlaceFormat).
        var formatBits = MicroQrConstants.GetFormatBits(version, eccLevel, mask);
        var r8 = rows[8];
        for (var col = 1; col <= 8; col++)
        {
            var bit = (ulong)((formatBits >> (15 - col)) & 1);
            r8 = (r8 & ~(1ul << col)) | (bit << col);
        }
        rows[8] = r8;
        for (var row = 7; row >= 1; row--)
        {
            var bit = (ulong)((formatBits >> (row - 1)) & 1);
            rows[row] = (rows[row] & ~(1ul << 8)) | (bit << 8);
        }

        // Unpack every row to bytes: full 8-byte write steps; a tail of >= 4
        // bytes becomes one overlapped spread ending at the row edge (write-mode
        // overlap is idempotent, unlike XOR), a tail of <= 3 bytes is cheaper as
        // scalar stores. PlaceSymbol validated matrix.Length >= size*size, so
        // every 8-byte write here stays inside the matrix.
        ref var buf = ref MemoryMarshal.GetReference(matrix);
        var rowOffset = 0;
        for (var y = 0; y < size; y++, rowOffset += size)
        {
            var bits = rows[y];
            var c = 0;
            for (; c + 8 <= size; c += 8)
            {
                WriteSpread8(ref Unsafe.Add(ref buf, rowOffset + c), bits >> c);
            }
            if (size - c >= 4)
            {
                var c2 = size - 8;
                WriteSpread8(ref Unsafe.Add(ref buf, rowOffset + c2), bits >> c2);
            }
            else
            {
                for (; c < size; c++)
                {
                    matrix[rowOffset + c] = (byte)((bits >> c) & 1);
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Writes 8 modules at once: byte k = bit k of the low byte of
    /// <paramref name="bits"/> (multiply-replicate to the per-byte diagonal,
    /// OR-cascade down to bit 0 — the ModulePlacer.Masking SWAR unpack).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSpread8(ref byte p, ulong bits)
    {
        var spread = ((bits & 0xFF) * 0x0101010101010101UL) & 0x8040201008040201UL;
        spread |= spread >> 4;
        spread |= spread >> 2;
        spread |= spread >> 1;
        spread &= 0x0101010101010101UL;
        Unsafe.WriteUnaligned(ref p, NormalizeEndianness(spread));
    }

    // ---------------------------------------------------------------
    // Static tables (all built by ordinary code from the reference
    // implementations in MicroQrModulePlacer.cs — NativeAOT/trimming-safe)
    // ---------------------------------------------------------------

    /// <summary>
    /// Function pattern rows 0..8 as byte blocks, [sizeIndex] with
    /// sizeIndex = (size - 11) / 2. Rows below 8 carry only column-0 timing.
    /// </summary>
    private static readonly byte[][] _funcTopRows = BuildFuncTopRows();

    /// <summary>Full function pattern as packed rows, [sizeIndex][row].</summary>
    private static readonly ulong[][] _funcPackedRows = BuildFuncPackedRows();

    private static ReadOnlySpan<byte> FuncTopRows(int size) => _funcTopRows[(size - 11) >> 1];

    private static ReadOnlySpan<ulong> FuncPackedRows(int size) => _funcPackedRows[(size - 11) >> 1];

    private static byte[][] BuildFuncTopRows()
    {
        var tables = new byte[4][];
        for (var sizeIndex = 0; sizeIndex < 4; sizeIndex++)
        {
            var size = 11 + 2 * sizeIndex;
            var matrix = new byte[size * size];
            PlaceFunctionModules(matrix, size);
            tables[sizeIndex] = matrix.AsSpan(0, 9 * size).ToArray();
        }
        return tables;
    }

    private static ulong[][] BuildFuncPackedRows()
    {
        var tables = new ulong[4][];
        for (var sizeIndex = 0; sizeIndex < 4; sizeIndex++)
        {
            var size = 11 + 2 * sizeIndex;
            var matrix = new byte[size * size];
            PlaceFunctionModules(matrix, size);
            var rows = new ulong[size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (matrix[y * size + x] != 0)
                    {
                        rows[y] |= 1ul << x;
                    }
                }
            }
            tables[sizeIndex] = rows;
        }
        return tables;
    }

    /// <summary>
    /// Mask darkness along the scored edges, [sizeIndex * 4 + mask]: bit i of
    /// the column table = GetMaskBit(mask, i, size-1), bit i of the row table =
    /// GetMaskBit(mask, size-1, i). Bit 0 stays clear (column 0 / row 0 are
    /// function modules and excluded from the edge sums).
    /// </summary>
    private static readonly ulong[] _edgeColBits = BuildEdgeBits(column: true);
    private static readonly ulong[] _edgeRowBits = BuildEdgeBits(column: false);

    private static ulong[] BuildEdgeBits(bool column)
    {
        var table = new ulong[4 * 4];
        for (var sizeIndex = 0; sizeIndex < 4; sizeIndex++)
        {
            var size = 11 + 2 * sizeIndex;
            for (var mask = 0; mask < 4; mask++)
            {
                ulong bits = 0;
                for (var i = 1; i < size; i++)
                {
                    var hit = column ? GetMaskBit(mask, i, size - 1) : GetMaskBit(mask, size - 1, i);
                    if (hit)
                    {
                        bits |= 1ul << i;
                    }
                }
                table[sizeIndex * 4 + mask] = bits;
            }
        }
        return table;
    }

    /// <summary>
    /// Packed mask template rows, [mask * 12 + (row % 12)]: every Micro QR mask
    /// formula is periodic in the row with period dividing 12 (row%2, row/2%2,
    /// row%3) and evaluated exactly per column bit (columns never exceed 16).
    /// Bits beyond the data region are removed by the caller's allowed mask.
    /// </summary>
    private static readonly ulong[] _maskTemplates12 = BuildMaskTemplates12();

    private static ulong[] BuildMaskTemplates12()
    {
        var table = new ulong[4 * 12];
        for (var mask = 0; mask < 4; mask++)
        {
            for (var r = 0; r < 12; r++)
            {
                ulong bits = 0;
                for (var c = 0; c < 64; c++)
                {
                    if (GetMaskBit(mask, r, c))
                    {
                        bits |= 1ul << c;
                    }
                }
                table[mask * 12 + r] = bits;
            }
        }
        return table;
    }

    // ---------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Maps between machine byte order and the logical little-endian order the
    /// SWAR unpack constant assumes (memory offset k = ulong byte k).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong NormalizeEndianness(ulong value)
        => BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PopCount(ulong value)
    {
#if NET8_0_OR_GREATER
        return System.Numerics.BitOperations.PopCount(value);
#else
        // SWAR popcount for targets without System.Numerics.BitOperations.
        value -= (value >> 1) & 0x5555555555555555UL;
        value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
        value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
        return (int)((value * 0x0101010101010101UL) >> 56);
#endif
    }
}
