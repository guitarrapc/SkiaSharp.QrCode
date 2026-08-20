#if NET8_0_OR_GREATER
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// ARM64 store pass: the masked bit array is written into the core matrix through a
/// register transpose instead of one 16-bit store per symbol row.
/// </summary>
/// <remarks>
/// The zigzag walk fills the bit array column-pair by column-pair while the matrix is
/// row-major, so the store pass is a transpose. The portable tier does it two modules
/// at a time (one byte-swapped 16-bit store per row of a "clean" column pair). This
/// tier takes four consecutive clean pairs — eight consecutive columns — and turns
/// them inside out in registers: each pair's two column vectors are separated with
/// UZP1/UZP2, the upward-walked pairs are flipped back into row order with TBL, and a
/// three-stage ZIP network leaves symbol row <c>i</c> in 64-bit lane <c>i &amp; 1</c> of
/// vector <c>i / 2</c>. One symbol row is then one 8-byte store: one store per eight
/// modules instead of one per two.
/// <para>
/// What the eight columns cannot cover is not thrown back to per-module scatter. The
/// portable tier's "is this whole column pair clean?" test disqualifies a pair when a
/// single function module appears anywhere in it, which on R11x27 sends 56 % of the
/// modules through the byte scatter even though 92 % of them sit in stretches of rows
/// where both columns are ordinary data. Those stretches are precomputed per version
/// as row RUNS and keep the 16-bit pair store; only the genuinely isolated modules
/// (4-12 % of a symbol) are scattered one byte at a time.
/// </para>
/// <para>
/// Measured against the shipped portable path on Apple M2 (kernel benchmark, six
/// rounds): 0.29x at R17x139, 0.34x at R13x99, 0.50x at R11x59, 0.56x at R7x43 and
/// 0.68x at R11x27. It wins every size, so there is no size switch. Two further
/// designs were measured and rejected: generalizing a block to "four adjacent pairs ×
/// their common data row range" raised coverage to 89 % but lost, because the ZIP
/// network costs a fixed ~40 instructions per block whatever its height, so short
/// blocks pay a tall block's price and take modules away from the cheaper run path.
/// </para>
/// <para>
/// The kernel is written against ref-based loads and stores rather than LD2/ST1-lane,
/// which would need <c>AllowUnsafeBlocks</c> across the library; UZP1/UZP2 replaces
/// the LD2 deinterleave and a 64-bit half store replaces the lane store.
/// </para>
/// </remarks>
internal static partial class RmQRModulePlacer
{
    /// <summary>
    /// Writes every data module: transpose blocks first, then the row runs, then the
    /// isolated modules. <paramref name="pitch"/> is the destination row stride, so the
    /// tight and quiet-zoned destinations share one implementation; only the isolated
    /// modules need the separate row/col table when the pitch is not the symbol width.
    /// </summary>
    private static void ScatterNeon(Span<byte> destination, Span<byte> bits, Layout layout, int height, int pitch, bool strided)
    {
        Debug.Assert(AdvSimd.Arm64.IsSupported, "The transpose tier is only dispatched on ARM64.");

        var rows = height - 2;
        ref var dest = ref MemoryMarshal.GetReference(destination);
        ref var src = ref MemoryMarshal.GetReference(bits);
        var step = (nuint)pitch;

        var blocks = layout.Blocks;
        if (blocks.Length != 0)
        {
            var revIdx = Vector128.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(layout.ReverseIndex));
            var pairBytes = (nuint)(2 * rows);
            for (var b = 0; b < blocks.Length; b++)
            {
                var blk = blocks[b];
                var s = (nuint)blk.StartBit;

                // Pair j of the block (j = 0 is the rightmost) -> its two column
                // vectors: even byte lanes are the pair's right column, odd its left.
                var (a0, c0) = SplitColumns(ref src, s);
                var (a1, c1) = SplitColumns(ref src, s + pairBytes);
                var (a2, c2) = SplitColumns(ref src, s + 2 * pairBytes);
                var (a3, c3) = SplitColumns(ref src, s + 3 * pairBytes);

                // An upward pair lists rows h-2 .. 1, so its lanes are row-reversed.
                // Directions alternate, so exactly two of the four need flipping.
                if (blk.Upward)
                {
                    a0 = AdvSimd.Arm64.VectorTableLookup(a0, revIdx);
                    c0 = AdvSimd.Arm64.VectorTableLookup(c0, revIdx);
                    a2 = AdvSimd.Arm64.VectorTableLookup(a2, revIdx);
                    c2 = AdvSimd.Arm64.VectorTableLookup(c2, revIdx);
                }
                else
                {
                    a1 = AdvSimd.Arm64.VectorTableLookup(a1, revIdx);
                    c1 = AdvSimd.Arm64.VectorTableLookup(c1, revIdx);
                    a3 = AdvSimd.Arm64.VectorTableLookup(a3, revIdx);
                    c3 = AdvSimd.Arm64.VectorTableLookup(c3, revIdx);
                }

                // Columns left to right are C3 A3 C2 A2 C1 A1 C0 A0 (the walk moves
                // right to left and visits a pair's right column first).
                var z0 = AdvSimd.Arm64.ZipLow(c3, a3);
                var z1 = AdvSimd.Arm64.ZipHigh(c3, a3);
                var z2 = AdvSimd.Arm64.ZipLow(c2, a2);
                var z3 = AdvSimd.Arm64.ZipHigh(c2, a2);
                var z4 = AdvSimd.Arm64.ZipLow(c1, a1);
                var z5 = AdvSimd.Arm64.ZipHigh(c1, a1);
                var z6 = AdvSimd.Arm64.ZipLow(c0, a0);
                var z7 = AdvSimd.Arm64.ZipHigh(c0, a0);

                var y0 = AdvSimd.Arm64.ZipLow(z0.AsUInt16(), z2.AsUInt16());
                var y1 = AdvSimd.Arm64.ZipHigh(z0.AsUInt16(), z2.AsUInt16());
                var y2 = AdvSimd.Arm64.ZipLow(z1.AsUInt16(), z3.AsUInt16());
                var y3 = AdvSimd.Arm64.ZipHigh(z1.AsUInt16(), z3.AsUInt16());
                var y4 = AdvSimd.Arm64.ZipLow(z4.AsUInt16(), z6.AsUInt16());
                var y5 = AdvSimd.Arm64.ZipHigh(z4.AsUInt16(), z6.AsUInt16());
                var y6 = AdvSimd.Arm64.ZipLow(z5.AsUInt16(), z7.AsUInt16());
                var y7 = AdvSimd.Arm64.ZipHigh(z5.AsUInt16(), z7.AsUInt16());

                // 64-bit lane (i & 1) of r(i / 2) is symbol row i + 1, all eight columns.
                var r0 = AdvSimd.Arm64.ZipLow(y0.AsUInt32(), y4.AsUInt32()).AsByte();
                var r1 = AdvSimd.Arm64.ZipHigh(y0.AsUInt32(), y4.AsUInt32()).AsByte();
                var r2 = AdvSimd.Arm64.ZipLow(y1.AsUInt32(), y5.AsUInt32()).AsByte();
                var r3 = AdvSimd.Arm64.ZipHigh(y1.AsUInt32(), y5.AsUInt32()).AsByte();
                var r4 = AdvSimd.Arm64.ZipLow(y2.AsUInt32(), y6.AsUInt32()).AsByte();
                var r5 = AdvSimd.Arm64.ZipHigh(y2.AsUInt32(), y6.AsUInt32()).AsByte();
                var r6 = AdvSimd.Arm64.ZipLow(y3.AsUInt32(), y7.AsUInt32()).AsByte();
                var r7 = AdvSimd.Arm64.ZipHigh(y3.AsUInt32(), y7.AsUInt32()).AsByte();

                // Heights are odd, so rows is odd and at least 5. The guards below are
                // per-version constants, not data, so they predict perfectly.
                ref var d = ref Unsafe.Add(ref dest, step + (nuint)blk.LeftCol);
                r0.GetLower().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                r0.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                r1.GetLower().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                r1.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                r2.GetLower().StoreUnsafe(ref d);
                if (rows > 5)
                {
                    d = ref Unsafe.Add(ref d, step);
                    r2.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                    r3.GetLower().StoreUnsafe(ref d);
                }
                if (rows > 7)
                {
                    d = ref Unsafe.Add(ref d, step);
                    r3.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                    r4.GetLower().StoreUnsafe(ref d);
                }
                if (rows > 9)
                {
                    d = ref Unsafe.Add(ref d, step);
                    r4.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                    r5.GetLower().StoreUnsafe(ref d);
                }
                if (rows > 11)
                {
                    d = ref Unsafe.Add(ref d, step);
                    r5.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                    r6.GetLower().StoreUnsafe(ref d);
                }
                if (rows > 13)
                {
                    d = ref Unsafe.Add(ref d, step);
                    r6.GetUpper().StoreUnsafe(ref d); d = ref Unsafe.Add(ref d, step);
                    r7.GetLower().StoreUnsafe(ref d);
                }
            }
        }

        // Stretches of rows where both columns of a pair are data: the same byte-swapped
        // 16-bit store per row as the portable tier, but no longer conditional on the
        // whole pair being clean.
        var runs = layout.Runs;
        for (var i = 0; i < runs.Length; i++)
        {
            var run = runs[i];
            var k = (nuint)run.StartBit;
            ref var d = ref Unsafe.Add(ref dest, (nuint)run.FirstRow * step + (nuint)run.LeftCol);
            if (run.Upward)
            {
                for (var r = 0; r < run.RowCount; r++)
                {
                    Unsafe.WriteUnaligned(ref d, SwapPair(Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref src, k))));
                    k += 2;
                    d = ref Unsafe.Subtract(ref d, step);
                }
            }
            else
            {
                for (var r = 0; r < run.RowCount; r++)
                {
                    Unsafe.WriteUnaligned(ref d, SwapPair(Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref src, k))));
                    k += 2;
                    d = ref Unsafe.Add(ref d, step);
                }
            }
        }

        // Whatever is left is a genuinely isolated module.
        var singles = layout.Singles;
        if (strided)
        {
            ref var rowCol = ref MemoryMarshal.GetArrayDataReference(layout.RowCol);
            for (var i = 0; i < singles.Length; i++)
            {
                var pos = (nuint)singles[i];
                uint code = Unsafe.Add(ref rowCol, pos);
                Unsafe.Add(ref dest, (nuint)(code >> 8) * step + (code & 0xFF)) = Unsafe.Add(ref src, pos);
            }
        }
        else
        {
            ref var idx = ref MemoryMarshal.GetArrayDataReference(layout.Index);
            for (var i = 0; i < singles.Length; i++)
            {
                var pos = (nuint)singles[i];
                Unsafe.Add(ref dest, Unsafe.Add(ref idx, pos)) = Unsafe.Add(ref src, pos);
            }
        }
    }

    /// <summary>
    /// Splits one column pair's 32 bit-array bytes into its two column vectors: the
    /// walk alternates right column, left column on every row, so the even byte lanes
    /// are the right column and the odd ones the left. Reads a fixed 32 bytes (a pair
    /// spans at most 30); the bit scratch always carries the slack.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> Right, Vector128<byte> Left) SplitColumns(ref byte src, nuint offset)
    {
        var v0 = Vector128.LoadUnsafe(ref src, offset);
        var v1 = Vector128.LoadUnsafe(ref src, offset + 16);
        return (AdvSimd.Arm64.UnzipEven(v0, v1), AdvSimd.Arm64.UnzipOdd(v0, v1));
    }
}
#endif
