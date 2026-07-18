#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SkiaSharp.QrCode.Internals.StandardQr;

/// <summary>
/// Vectorized mask pattern selection for x86/x64 with AVX2. Selected at runtime
/// by <see cref="ModulePlacer.MaskCode"/>; produces byte-identical matrices and
/// identical pattern selections to the scalar bit-packed implementation in
/// ModulePlacer.Masking.cs (verified by ModulePlacerMaskSimdParityTest).
///
/// Architecture (see the micro-optimization findings log, round 5):
/// - The scalar scorer pays ~60 ALU ops + 7 popcounts per row; rows are
///   independent, so the scorer runs lane-per-row (Vector256&lt;ulong&gt; = 4 rows
///   per iteration) with per-lane shifts (vpsrlq). Column-direction rules become
///   offset-load vector passes over materialized eq/v5 arrays.
/// - .NET has no VPOPCNTDQ intrinsic, so vector popcount is the Mula sequence
///   (2x vpshufb nibble LUT + vpsadbw), accumulated in weight-grouped vector
///   accumulators and reduced once per score.
/// - Three width tiers: 1 ulong per row (size &lt;= 64, versions 1-11), 2-word
///   SoA (size &lt;= 128, versions 12-29 — the third word would be permanently
///   zero), 3-word SoA (versions 30-40). SoA storage (w0[]/w1[]/w2[] arrays)
///   turns cross-word shifts into vector ops on adjacent word arrays.
/// - Byte&lt;-&gt;bit edges are native SIMD: packing 0/1 bytes is pcmpeqb+pmovmskb
///   (32 modules per step), unpacking the winner's XOR delta is
///   broadcast+vpshufb+vpcmpeqb (32 modules per step).
/// - No abort threshold: measured, raw 4-lane throughput beats the scalar
///   scorer's early-exit at every size (v1 1.33x, v10 2.03x, v40 1.47x).
///
/// This file only executes under Avx2.IsSupported (x86/x64), so memory order is
/// always little-endian and the SWAR tail reads skip endianness normalization.
/// </summary>
internal static partial class ModulePlacer
{
    /// <summary>Entry point for the vectorized tiers. Caller guarantees Avx2.IsSupported.</summary>
    internal static int MaskCodeSimd(Span<byte> buffer, int size, int version, ReadOnlySpan<byte> blockedMask, ECCLevel eccLevel)
    {
        if (size <= 64)
        {
            return MaskCode64Simd(buffer, size, version, blockedMask, eccLevel);
        }
        return size <= 128
            ? MaskCode128Simd(buffer, size, version, blockedMask, eccLevel)
            : MaskCode192Simd(buffer, size, version, blockedMask, eccLevel);
    }

    // ---------------------------------
    // Shared SIMD pieces
    // ---------------------------------
    // Operand-order note: the cross-platform helper Vector256.AndNot(left, right)
    // computes left & ~right ("bitwise-and of a given vector and the ones
    // complement of another vector"). This is the OPPOSITE operand convention of
    // the hardware intrinsic Avx2.AndNot(left, right) = ~left & right (vpandn) —
    // the JIT swaps the operands when it emits vpandn for the helper. Every
    // AndNot in this file is the cross-platform helper, so e.g.
    // Vector256.AndNot(rowMask, x) == ~x & rowMask and
    // Vector256.AndNot(y5, y5 << 1) == y5 & ~(y5 << 1), matching the scalar code
    // (verified byte-for-byte by ModulePlacerMaskSimdParityTest).

    /// <summary>Nibble LUT for the Mula vector popcount (per-4-bit set-bit counts).</summary>
    private static readonly Vector256<byte> PopLut256 = Vector256.Create(
        (byte)0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4,
        0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4);

    /// <summary>Per-qword popcount of 4 lanes (Mula: two vpshufb nibble lookups + vpsadbw).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ulong> Pop256(Vector256<ulong> v)
    {
        var lowMask = Vector256.Create((byte)0x0F);
        var lo = v.AsByte() & lowMask;
        var hi = Vector256.ShiftRightLogical(v, 4).AsByte() & lowMask;
        var cnt = Avx2.Shuffle(PopLut256, lo) + Avx2.Shuffle(PopLut256, hi);
        return Avx2.SumAbsoluteDifferences(cnt, Vector256<byte>.Zero).AsUInt64();
    }

    /// <summary>Byte-replicate shuffle control: delta byte k -&gt; output bytes 8k..8k+7 (lane-local indices for vpshufb).</summary>
    private static readonly Vector256<byte> UnpackShuffle = Vector256.Create(
        (byte)0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
        2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3);

    /// <summary>Per-byte bit selectors [1,2,4,...,128] repeated: bit i of the delta byte selects output byte i.</summary>
    private static readonly Vector256<byte> UnpackBitSel = Vector256.Create(
        (byte)1, 2, 4, 8, 16, 32, 64, 128, 1, 2, 4, 8, 16, 32, 64, 128,
        1, 2, 4, 8, 16, 32, 64, 128, 1, 2, 4, 8, 16, 32, 64, 128);

    /// <summary>
    /// Packs a row of 0/1 module bytes into bits: pcmpeqb+pmovmskb handles 32
    /// modules per step (vs 8 per SWAR multiply), then 16-byte, 8-byte-SWAR and
    /// scalar tails. Bit c = row[c], same contract as PackRowBits64.
    /// </summary>
    internal static ulong PackRowBits64Simd(ReadOnlySpan<byte> row)
    {
        ulong w = 0;
        var c = 0;
        ref var r = ref MemoryMarshal.GetReference(row);
        for (; c + 32 <= row.Length; c += 32)
        {
            var v = Vector256.LoadUnsafe(ref r, (nuint)c);
            var zeroMask = (uint)Avx2.MoveMask(Vector256.Equals(v, Vector256<byte>.Zero));
            w |= (ulong)~zeroMask << c;
        }
        if (c + 16 <= row.Length)
        {
            var v = Vector128.LoadUnsafe(ref r, (nuint)c);
            var zeroMask = (uint)Sse2.MoveMask(Vector128.Equals(v, Vector128<byte>.Zero));
            w |= (ulong)(ushort)~zeroMask << c;
            c += 16;
        }
        for (; c + 8 <= row.Length; c += 8)
        {
            // x86 only (Avx2-guarded), so the load is little-endian by definition.
            var u = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref r, c));
            w |= ((u * 0x0102040810204080UL) >> 56) << c;
        }
        for (; c < row.Length; c++)
        {
            if (row[c] != 0)
            {
                w |= 1ul << c;
            }
        }
        return w;
    }

    /// <summary>
    /// Triple-word SIMD row packer. The 32-module chunks are 32-aligned so they
    /// never straddle a word boundary; the 16/8-module tails stay within a word
    /// for the same reason.
    /// </summary>
    private static Row192 PackRowBits192Simd(ReadOnlySpan<byte> row)
    {
        ulong w0 = 0, w1 = 0, w2 = 0;
        var c = 0;
        ref var r = ref MemoryMarshal.GetReference(row);
        for (; c + 32 <= row.Length; c += 32)
        {
            var v = Vector256.LoadUnsafe(ref r, (nuint)c);
            var bits = (ulong)(uint)~Avx2.MoveMask(Vector256.Equals(v, Vector256<byte>.Zero));
            if (c < 64) w0 |= bits << c;
            else if (c < 128) w1 |= bits << (c - 64);
            else w2 |= bits << (c - 128);
        }
        if (c + 16 <= row.Length)
        {
            var v = Vector128.LoadUnsafe(ref r, (nuint)c);
            var bits = (ulong)(ushort)~Sse2.MoveMask(Vector128.Equals(v, Vector128<byte>.Zero));
            if (c < 64) w0 |= bits << c;
            else if (c < 128) w1 |= bits << (c - 64);
            else w2 |= bits << (c - 128);
            c += 16;
        }
        for (; c + 8 <= row.Length; c += 8)
        {
            var u = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref r, c));
            var bits = (u * 0x0102040810204080UL) >> 56;
            if (c < 64) w0 |= bits << c;
            else if (c < 128) w1 |= bits << (c - 64);
            else w2 |= bits << (c - 128);
        }
        for (; c < row.Length; c++)
        {
            if (row[c] != 0)
            {
                if (c < 64) w0 |= 1ul << c;
                else if (c < 128) w1 |= 1ul << (c - 64);
                else w2 |= 1ul << (c - 128);
            }
        }
        return new Row192(w0, w1, w2);
    }

    /// <summary>XORs a packed 0/1 delta into a byte row, 32 modules per SIMD step (broadcast+vpshufb+vpcmpeqb), SWAR + scalar tails.</summary>
    internal static void XorUnpackRow64Simd(Span<byte> row, ulong delta)
    {
        var c = 0;
        ref var r = ref MemoryMarshal.GetReference(row);
        var ones = Vector256.Create((byte)1);
        for (; c + 32 <= row.Length; c += 32)
        {
            var chunk = (uint)(delta >> c);
            var repl = Avx2.Shuffle(Vector256.Create(chunk).AsByte(), UnpackShuffle);
            var sel = repl & UnpackBitSel;
            var bits01 = Vector256.Equals(sel, UnpackBitSel) & ones;
            var cur = Vector256.LoadUnsafe(ref r, (nuint)c);
            (cur ^ bits01).StoreUnsafe(ref r, (nuint)c);
        }
        for (; c + 8 <= row.Length; c += 8)
        {
            var b = (delta >> c) & 0xFF;
            var spread = (b * 0x0101010101010101UL) & 0x8040201008040201UL;
            spread |= spread >> 4;
            spread |= spread >> 2;
            spread |= spread >> 1;
            spread &= 0x0101010101010101UL;
            ref var p = ref Unsafe.Add(ref r, c);
            var cur = Unsafe.ReadUnaligned<ulong>(ref p);
            Unsafe.WriteUnaligned(ref p, cur ^ spread);
        }
        for (; c < row.Length; c++)
        {
            if (((delta >> c) & 1) != 0)
            {
                row[c] ^= 1;
            }
        }
    }

    /// <summary>Triple-word SIMD delta unpack (32-aligned chunks never straddle words), SWAR + scalar tails.</summary>
    private static void XorUnpackRow192Simd(ref byte rowRef, int len, in Row192 delta)
    {
        var c = 0;
        var ones = Vector256.Create((byte)1);
        for (; c + 32 <= len; c += 32)
        {
            var chunk = (uint)(delta.WordAt(c >> 6) >> (c & 63));
            var repl = Avx2.Shuffle(Vector256.Create(chunk).AsByte(), UnpackShuffle);
            var sel = repl & UnpackBitSel;
            var bits01 = Vector256.Equals(sel, UnpackBitSel) & ones;
            ref var p = ref Unsafe.Add(ref rowRef, c);
            var cur = Vector256.LoadUnsafe(ref p);
            (cur ^ bits01).StoreUnsafe(ref p);
        }
        for (; c + 8 <= len; c += 8)
        {
            var b = (delta.WordAt(c >> 6) >> (c & 63)) & 0xFF;
            var spread = (b * 0x0101010101010101UL) & 0x8040201008040201UL;
            spread |= spread >> 4;
            spread |= spread >> 2;
            spread |= spread >> 1;
            spread &= 0x0101010101010101UL;
            ref var p = ref Unsafe.Add(ref rowRef, c);
            var cur = Unsafe.ReadUnaligned<ulong>(ref p);
            Unsafe.WriteUnaligned(ref p, cur ^ spread);
        }
        for (; c < len; c++)
        {
            if (((delta.WordAt(c >> 6) >> (c & 63)) & 1) != 0)
            {
                Unsafe.Add(ref rowRef, c) ^= 1;
            }
        }
    }

    // ---------------------------------
    // Single-word tier (versions 1-11)
    // ---------------------------------

    internal static int MaskCode64Simd(Span<byte> buffer, int size, int version, ReadOnlySpan<byte> blockedMask, ECCLevel eccLevel)
    {
        Span<ulong> packed = stackalloc ulong[64];
        Span<ulong> allowed = stackalloc ulong[64];
        Span<ulong> masked = stackalloc ulong[64];
        Span<ulong> nmasked = stackalloc ulong[64];
        Span<ulong> eqScratch = stackalloc ulong[64];
        Span<ulong> v5Scratch = stackalloc ulong[64];
        packed = packed[..size];
        allowed = allowed[..size];
        masked = masked[..size];
        nmasked = nmasked[..size];

        for (var y = 0; y < size; y++)
        {
            packed[y] = PackRowBits64Simd(buffer.Slice(y * size, size));
        }

        // Version bits sit in blocked areas, hence identical for every pattern.
        if (version >= 7)
        {
            var versionBits = QRCodeConstants.GetVersionBits(version);
            for (var x = 0; x < 6; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    var bit = (versionBits & (1u << (x * 3 + y))) != 0;
                    packed[y + size - 11] = WithBit64(packed[y + size - 11], x, bit);
                    packed[x] = WithBit64(packed[x], y + size - 11, bit);
                }
            }
        }

        // Each allowed row (~blocked) is a contiguous bit slice of the blocked
        // bitmask; a padded copy makes the two 8-byte slice reads always legal.
        Span<byte> padded = stackalloc byte[blockedMask.Length + 16];
        padded.Clear();
        blockedMask.CopyTo(padded);
        var rowMask = size == 64 ? ulong.MaxValue : (1ul << size) - 1;
        for (var y = 0; y < size; y++)
        {
            var bitOffset = y * size;
            var byteOff = bitOffset >> 3;
            var sh = bitOffset & 7;
            var u0 = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(padded.Slice(byteOff)));
            var u1 = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(padded.Slice(byteOff + 8)));
            var blocked = sh == 0 ? u0 : (u0 >> sh) | (u1 << (64 - sh));
            allowed[y] = ~blocked & rowMask;
        }

        var templates = _maskTemplates64;
        var bestPatternIndex = 0;
        var bestScore = int.MaxValue;
        for (var patternIndex = 0; patternIndex < 8; patternIndex++)
        {
            var tplBase = patternIndex * 12;
            for (int y = 0, tplRow = 0; y < size; y++)
            {
                masked[y] = packed[y] ^ (templates[tplBase + tplRow] & allowed[y]);
                if (++tplRow == 12) tplRow = 0;
            }
            PokeFormatBits64(masked, size, QRCodeConstants.GetFormatBits(eccLevel, patternIndex));

            var score = CalculateScore64Vec(masked, nmasked, eqScratch, v5Scratch, size);
            if (score < bestScore)
            {
                bestPatternIndex = patternIndex;
                bestScore = score;
            }
        }

        // Apply the winner to the byte buffer: unpack the XOR delta 32 modules at a time.
        {
            var tplBase = bestPatternIndex * 12;
            for (int y = 0, tplRow = 0; y < size; y++)
            {
                XorUnpackRow64Simd(buffer.Slice(y * size, size), _maskTemplates64[tplBase + tplRow] & allowed[y]);
                if (++tplRow == 12) tplRow = 0;
            }
        }

        return bestPatternIndex;
    }

    /// <summary>
    /// Lane-per-row Vector256 penalty scorer for single-word rows.
    /// Row-direction rules run 4 rows per iteration with per-lane shifts and
    /// Mula vector popcount; column-direction rules materialize eq (vertical run
    /// continuation) and v5 (4-deep AND window) arrays with vector passes, then
    /// score them with offset loads. Scalar tails reuse the scalar expressions.
    /// Popcounts accumulate in weight-grouped vector accumulators (rule-1 ones /
    /// twos, rule-2 x3, rule-3 x40, balance) and reduce once at the end.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static int CalculateScore64Vec(ReadOnlySpan<ulong> rows, Span<ulong> nrows, Span<ulong> eqArr, Span<ulong> v5Arr, int size)
    {
        var rowMask = size == 64 ? ulong.MaxValue : (1ul << size) - 1;
        var startMaskP3 = (1ul << (size - 10)) - 1;
        var maskN1 = (1ul << (size - 1)) - 1;

        ref var rowsRef = ref MemoryMarshal.GetReference(rows);
        ref var nrowsRef = ref MemoryMarshal.GetReference(nrows);
        ref var eqRef = ref MemoryMarshal.GetReference(eqArr);
        ref var v5Ref = ref MemoryMarshal.GetReference(v5Arr);

        var score1 = 0;
        var score2 = 0;
        var score3 = 0;
        var blackModules = 0;

        var rowMaskV = Vector256.Create(rowMask);
        var startMaskV = Vector256.Create(startMaskP3);
        var maskN1V = Vector256.Create(maskN1);

        var accOnes = Vector256<ulong>.Zero;  // rule-1 weight-1 counts (y5 positions)
        var accTwos = Vector256<ulong>.Zero;  // rule-1 weight-2 counts (run starts)
        var accP2 = Vector256<ulong>.Zero;    // rule-2 blocks (x3 at reduce)
        var accP3 = Vector256<ulong>.Zero;    // rule-3 windows (x40 at reduce)
        var accBlack = Vector256<ulong>.Zero;

        // nrows = ~rows & rowMask
        var y0 = 0;
        for (; y0 + 4 <= size; y0 += 4)
        {
            var x = Vector256.LoadUnsafe(ref rowsRef, (nuint)y0);
            Vector256.AndNot(rowMaskV, x).StoreUnsafe(ref nrowsRef, (nuint)y0);
        }
        for (; y0 < size; y0++)
        {
            nrows[y0] = ~rows[y0] & rowMask;
        }

        // Row-direction rules 1 and 3 + balance popcount, 4 rows per iteration.
        var y = 0;
        for (; y + 4 <= size; y += 4)
        {
            var x = Vector256.LoadUnsafe(ref rowsRef, (nuint)y);
            var nx = Vector256.LoadUnsafe(ref nrowsRef, (nuint)y);

            accBlack += Pop256(x);

            var y2 = x & Vector256.ShiftRightLogical(x, 1);
            var y4 = y2 & Vector256.ShiftRightLogical(y2, 2);
            var y5 = y4 & Vector256.ShiftRightLogical(x, 4);
            var st = Vector256.AndNot(y5, Vector256.ShiftLeft(y5, 1));
            var n2 = nx & Vector256.ShiftRightLogical(nx, 1);
            var n4 = n2 & Vector256.ShiftRightLogical(n2, 2);
            var n5 = n4 & Vector256.ShiftRightLogical(nx, 4);
            var nst = Vector256.AndNot(n5, Vector256.ShiftLeft(n5, 1));
            accOnes += Pop256(y5) + Pop256(n5);
            accTwos += Pop256(st) + Pop256(nst);

            var mf = nx & Vector256.ShiftRightLogical(nx, 1) & Vector256.ShiftRightLogical(nx, 2) & Vector256.ShiftRightLogical(nx, 3)
                   & Vector256.ShiftRightLogical(x, 4) & Vector256.ShiftRightLogical(nx, 5) & Vector256.ShiftRightLogical(x, 6) & Vector256.ShiftRightLogical(x, 7)
                   & Vector256.ShiftRightLogical(x, 8) & Vector256.ShiftRightLogical(nx, 9) & Vector256.ShiftRightLogical(x, 10) & startMaskV;
            var mb = x & Vector256.ShiftRightLogical(nx, 1) & Vector256.ShiftRightLogical(x, 2) & Vector256.ShiftRightLogical(x, 3)
                   & Vector256.ShiftRightLogical(x, 4) & Vector256.ShiftRightLogical(nx, 5) & Vector256.ShiftRightLogical(x, 6) & Vector256.ShiftRightLogical(nx, 7)
                   & Vector256.ShiftRightLogical(nx, 8) & Vector256.ShiftRightLogical(nx, 9) & Vector256.ShiftRightLogical(nx, 10) & startMaskV;
            accP3 += Pop256(mf) + Pop256(mb);
        }
        for (; y < size; y++)
        {
            var x = rows[y];
            var nx = nrows[y];

            blackModules += PopCount(x);
            score1 += ScoreRuns64(x) + ScoreRuns64(nx);

            var mf = nx & (nx >> 1) & (nx >> 2) & (nx >> 3)
                   & (x >> 4) & (nx >> 5) & (x >> 6) & (x >> 7)
                   & (x >> 8) & (nx >> 9) & (x >> 10) & startMaskP3;
            var mb = x & (nx >> 1) & (x >> 2) & (x >> 3)
                   & (x >> 4) & (nx >> 5) & (x >> 6) & (nx >> 7)
                   & (nx >> 8) & (nx >> 9) & (nx >> 10) & startMaskP3;
            score3 += 40 * (PopCount(mf) + PopCount(mb));
        }

        // eqArr[i] = ~(rows[i] ^ rows[i+1]) & rowMask, i in 0..size-2
        // (vertical run continuation between adjacent rows; reused by rules 1 and 2).
        var i = 0;
        for (; i + 4 <= size - 1; i += 4)
        {
            var a = Vector256.LoadUnsafe(ref rowsRef, (nuint)i);
            var b = Vector256.LoadUnsafe(ref rowsRef, (nuint)(i + 1));
            (~(a ^ b) & rowMaskV).StoreUnsafe(ref eqRef, (nuint)i);
        }
        for (; i < size - 1; i++)
        {
            eqArr[i] = ~(rows[i] ^ rows[i + 1]) & rowMask;
        }

        // Rule 2 (2x2 blocks): m = eqh & eq & (eq >> 1) & maskN1 per row pair.
        // eqArr is masked with rowMask ⊇ maskN1, so reusing it is exact.
        y = 0;
        for (; y + 4 <= size - 1; y += 4)
        {
            var x = Vector256.LoadUnsafe(ref rowsRef, (nuint)y);
            var eqv = Vector256.LoadUnsafe(ref eqRef, (nuint)y);
            var eqh = ~(x ^ Vector256.ShiftRightLogical(x, 1));
            var m = eqh & eqv & Vector256.ShiftRightLogical(eqv, 1) & maskN1V;
            accP2 += Pop256(m);
        }
        for (; y < size - 1; y++)
        {
            var x = rows[y];
            var eqv = eqArr[y];
            var eqh = ~(x ^ (x >> 1));
            var m = eqh & eqv & (eqv >> 1) & maskN1;
            score2 += 3 * PopCount(m);
        }

        // Column rule 1: v5Arr[y] = AND of eqArr[y-4..y-1] for y in 4..size-1
        // (vertical 5-run marker); v5Arr[3] = 0 backs the prev-load at y = 4.
        v5Arr[3] = 0;
        y = 4;
        for (; y + 4 <= size; y += 4)
        {
            var e1 = Vector256.LoadUnsafe(ref eqRef, (nuint)(y - 4));
            var e2 = Vector256.LoadUnsafe(ref eqRef, (nuint)(y - 3));
            var e3 = Vector256.LoadUnsafe(ref eqRef, (nuint)(y - 2));
            var e4 = Vector256.LoadUnsafe(ref eqRef, (nuint)(y - 1));
            (e1 & e2 & e3 & e4).StoreUnsafe(ref v5Ref, (nuint)y);
        }
        for (; y < size; y++)
        {
            v5Arr[y] = eqArr[y - 4] & eqArr[y - 3] & eqArr[y - 2] & eqArr[y - 1];
        }

        y = 4;
        for (; y + 4 <= size; y += 4)
        {
            var v5 = Vector256.LoadUnsafe(ref v5Ref, (nuint)y);
            var prev = Vector256.LoadUnsafe(ref v5Ref, (nuint)(y - 1));
            accOnes += Pop256(v5);
            accTwos += Pop256(Vector256.AndNot(v5, prev));
        }
        for (; y < size; y++)
        {
            var v5 = v5Arr[y];
            score1 += PopCount(v5) + 2 * PopCount(v5 & ~v5Arr[y - 1]);
        }

        // Column rule 3: 11-row windows, start rows b in 0..size-11.
        var b0 = 0;
        for (; b0 + 4 <= size - 10; b0 += 4)
        {
            var r0 = Vector256.LoadUnsafe(ref rowsRef, (nuint)b0);
            var r2 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 2));
            var r3 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 3));
            var r4 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 4));
            var r6 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 6));
            var r7 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 7));
            var r8 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 8));
            var r10 = Vector256.LoadUnsafe(ref rowsRef, (nuint)(b0 + 10));
            var n0 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)b0);
            var n1 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 1));
            var n2c = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 2));
            var n3 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 3));
            var n5c = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 5));
            var n7 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 7));
            var n8 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 8));
            var n9 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 9));
            var n10 = Vector256.LoadUnsafe(ref nrowsRef, (nuint)(b0 + 10));

            var mf = n0 & n1 & n2c & n3 & r4 & n5c & r6 & r7 & r8 & n9 & r10;
            var mb = r0 & n1 & r2 & r3 & r4 & n5c & r6 & n7 & n8 & n9 & n10;
            accP3 += Pop256(mf) + Pop256(mb);
        }
        for (; b0 <= size - 11; b0++)
        {
            var mf = nrows[b0] & nrows[b0 + 1] & nrows[b0 + 2] & nrows[b0 + 3]
                   & rows[b0 + 4] & nrows[b0 + 5] & rows[b0 + 6] & rows[b0 + 7]
                   & rows[b0 + 8] & nrows[b0 + 9] & rows[b0 + 10];
            var mb = rows[b0] & nrows[b0 + 1] & rows[b0 + 2] & rows[b0 + 3]
                   & rows[b0 + 4] & nrows[b0 + 5] & rows[b0 + 6] & nrows[b0 + 7]
                   & nrows[b0 + 8] & nrows[b0 + 9] & nrows[b0 + 10];
            score3 += 40 * (PopCount(mf) + PopCount(mb));
        }

        score1 += (int)Vector256.Sum(accOnes) + 2 * (int)Vector256.Sum(accTwos);
        score2 += 3 * (int)Vector256.Sum(accP2);
        score3 += 40 * (int)Vector256.Sum(accP3);
        blackModules += (int)Vector256.Sum(accBlack);

        return score1 + score2 + score3 + CalculateBalanceScore(blackModules, size);
    }

    // ---------------------------------
    // Two-word SoA tier (versions 12-29, size 65..128)
    // ---------------------------------

    internal static int MaskCode128Simd(Span<byte> buffer, int size, int version, ReadOnlySpan<byte> blockedMask, ECCLevel eccLevel)
    {
        var packedRent = System.Buffers.ArrayPool<Row192>.Shared.Rent(2 * size);
        var wordsRent = System.Buffers.ArrayPool<ulong>.Shared.Rent(8 * size);
        try
        {
            var packed = packedRent.AsSpan(0, size);
            var allowed = packedRent.AsSpan(size, size);

            PackAllSimd(buffer, blockedMask, size, version, packed, allowed);

            // SoA partitions: masked words, ~masked words, eq words, v5 words.
            var mw0 = wordsRent.AsSpan(0, size);
            var mw1 = wordsRent.AsSpan(size, size);
            var nw0 = wordsRent.AsSpan(2 * size, size);
            var nw1 = wordsRent.AsSpan(3 * size, size);
            var eq0 = wordsRent.AsSpan(4 * size, size);
            var eq1 = wordsRent.AsSpan(5 * size, size);
            var v50 = wordsRent.AsSpan(6 * size, size);
            var v51 = wordsRent.AsSpan(7 * size, size);

            var templates = _maskTemplates;
            var bestPatternIndex = 0;
            var bestScore = int.MaxValue;
            for (var patternIndex = 0; patternIndex < 8; patternIndex++)
            {
                var tplBase = patternIndex * 12;
                for (int y = 0, tplRow = 0; y < size; y++)
                {
                    var t = templates[tplBase + tplRow];
                    var a = allowed[y];
                    var p = packed[y];
                    mw0[y] = p.W0 ^ (t.W0 & a.W0);
                    mw1[y] = p.W1 ^ (t.W1 & a.W1);
                    if (++tplRow == 12) tplRow = 0;
                }
                PokeFormatBitsSoA2(mw0, mw1, size, QRCodeConstants.GetFormatBits(eccLevel, patternIndex));

                var score = CalculateScore128Vec(mw0, mw1, nw0, nw1, eq0, eq1, v50, v51, size);
                if (score < bestScore)
                {
                    bestPatternIndex = patternIndex;
                    bestScore = score;
                }
            }

            ApplyWinnerSimd(buffer, size, bestPatternIndex, allowed);
            return bestPatternIndex;
        }
        finally
        {
            System.Buffers.ArrayPool<Row192>.Shared.Return(packedRent);
            System.Buffers.ArrayPool<ulong>.Shared.Return(wordsRent);
        }
    }

    private static void PokeFormatBitsSoA2(Span<ulong> w0, Span<ulong> w1, int size, ushort formatBits)
    {
        // Same coordinate scheme as PokeFormatBits64 (see FormatXs1/FormatYs1).
        for (var i = 0; i < 15; i++)
        {
            var bit = (formatBits & (1 << i)) != 0;
            SetBitSoA2(w0, w1, FormatYs1[i], FormatXs1[i], bit);
            var x2 = i < 8 ? size - 1 - i : 8;
            var y2 = i < 8 ? 8 : size - 15 + i;
            SetBitSoA2(w0, w1, y2, x2, bit);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetBitSoA2(Span<ulong> w0, Span<ulong> w1, int y, int x, bool value)
    {
        ref var w = ref (x < 64 ? ref w0[y] : ref w1[y]);
        var bit = 1ul << (x & 63);
        w = value ? w | bit : w & ~bit;
    }

    /// <summary>4 rows x 128 bits in SoA form: word k of rows y..y+3 in one vector.</summary>
    private readonly struct RV128
    {
        public readonly Vector256<ulong> A, B;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV128(Vector256<ulong> a, Vector256<ulong> b) { A = a; B = b; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RV128 Load(ref ulong w0, ref ulong w1, int y)
            => new(Vector256.LoadUnsafe(ref w0, (nuint)y), Vector256.LoadUnsafe(ref w1, (nuint)y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(ref ulong w0, ref ulong w1, int y)
        {
            A.StoreUnsafe(ref w0, (nuint)y);
            B.StoreUnsafe(ref w1, (nuint)y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RV128 operator &(in RV128 x, in RV128 y) => new(x.A & y.A, x.B & y.B);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RV128 operator ^(in RV128 x, in RV128 y) => new(x.A ^ y.A, x.B ^ y.B);

        /// <summary>~(this ^ other).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV128 Xnor(in RV128 o) => new(~(A ^ o.A), ~(B ^ o.B));

        /// <summary>this &amp; ~other (Vector256.AndNot(left, right) == left &amp; ~right; see the operand-order note above).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV128 AndNotWith(in RV128 o) => new(Vector256.AndNot(A, o.A), Vector256.AndNot(B, o.B));

        /// <summary>Logical shift right by k bits (1..63), pulling neighbor-word bits in at the top of each lane.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV128 ShiftRight(int k)
            => new(Vector256.ShiftRightLogical(A, k) | Vector256.ShiftLeft(B, 64 - k),
                   Vector256.ShiftRightLogical(B, k));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV128 ShiftLeft1()
            => new(Vector256.ShiftLeft(A, 1),
                   Vector256.ShiftLeft(B, 1) | Vector256.ShiftRightLogical(A, 63));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<ulong> Pop() => Pop256(A) + Pop256(B);
    }

    /// <summary>Two-word SoA Vector256 penalty scorer (structure mirrors <see cref="CalculateScore64Vec"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static int CalculateScore128Vec(
        Span<ulong> rw0, Span<ulong> rw1,
        Span<ulong> nw0, Span<ulong> nw1,
        Span<ulong> eq0, Span<ulong> eq1,
        Span<ulong> v50, Span<ulong> v51, int size)
    {
        var rowMaskR = Row192.MaskLow(size);
        var startMaskR = Row192.MaskLow(size - 10);
        var maskN1R = Row192.MaskLow(size - 1);

        ref var rw0R = ref MemoryMarshal.GetReference(rw0);
        ref var rw1R = ref MemoryMarshal.GetReference(rw1);
        ref var nw0R = ref MemoryMarshal.GetReference(nw0);
        ref var nw1R = ref MemoryMarshal.GetReference(nw1);
        ref var eq0R = ref MemoryMarshal.GetReference(eq0);
        ref var eq1R = ref MemoryMarshal.GetReference(eq1);
        ref var v50R = ref MemoryMarshal.GetReference(v50);
        ref var v51R = ref MemoryMarshal.GetReference(v51);

        var score1 = 0;
        var score2 = 0;
        var score3 = 0;
        var blackModules = 0;

        var rowMaskV = new RV128(Vector256.Create(rowMaskR.W0), Vector256.Create(rowMaskR.W1));
        var startMaskV = new RV128(Vector256.Create(startMaskR.W0), Vector256.Create(startMaskR.W1));
        var maskN1V = new RV128(Vector256.Create(maskN1R.W0), Vector256.Create(maskN1R.W1));

        var accOnes = Vector256<ulong>.Zero;
        var accTwos = Vector256<ulong>.Zero;
        var accP2 = Vector256<ulong>.Zero;
        var accP3 = Vector256<ulong>.Zero;
        var accBlack = Vector256<ulong>.Zero;

        var y0 = 0;
        for (; y0 + 4 <= size; y0 += 4)
        {
            var x = RV128.Load(ref rw0R, ref rw1R, y0);
            new RV128(Vector256.AndNot(rowMaskV.A, x.A), Vector256.AndNot(rowMaskV.B, x.B)).Store(ref nw0R, ref nw1R, y0);
        }
        for (; y0 < size; y0++)
        {
            nw0[y0] = ~rw0[y0] & rowMaskR.W0;
            nw1[y0] = ~rw1[y0] & rowMaskR.W1;
        }

        var y = 0;
        for (; y + 4 <= size; y += 4)
        {
            var x = RV128.Load(ref rw0R, ref rw1R, y);
            var nx = RV128.Load(ref nw0R, ref nw1R, y);

            accBlack += x.Pop();

            var y2 = x & x.ShiftRight(1);
            var y4 = y2 & y2.ShiftRight(2);
            var y5 = y4 & x.ShiftRight(4);
            var st = y5.AndNotWith(y5.ShiftLeft1());
            var n2 = nx & nx.ShiftRight(1);
            var n4 = n2 & n2.ShiftRight(2);
            var n5 = n4 & nx.ShiftRight(4);
            var nst = n5.AndNotWith(n5.ShiftLeft1());
            accOnes += y5.Pop() + n5.Pop();
            accTwos += st.Pop() + nst.Pop();

            var mf = nx & nx.ShiftRight(1) & nx.ShiftRight(2) & nx.ShiftRight(3)
                   & x.ShiftRight(4) & nx.ShiftRight(5) & x.ShiftRight(6) & x.ShiftRight(7)
                   & x.ShiftRight(8) & nx.ShiftRight(9) & x.ShiftRight(10) & startMaskV;
            var mb = x & nx.ShiftRight(1) & x.ShiftRight(2) & x.ShiftRight(3)
                   & x.ShiftRight(4) & nx.ShiftRight(5) & x.ShiftRight(6) & nx.ShiftRight(7)
                   & nx.ShiftRight(8) & nx.ShiftRight(9) & nx.ShiftRight(10) & startMaskV;
            accP3 += mf.Pop() + mb.Pop();
        }
        for (; y < size; y++)
        {
            var x = new Row192(rw0[y], rw1[y], 0);
            var nx = new Row192(nw0[y], nw1[y], 0);
            blackModules += x.PopCount();
            score1 += ScoreRuns(x) + ScoreRuns(nx);
            score3 += MatchFinderRowSimd(x, nx, startMaskR);
        }

        var i = 0;
        for (; i + 4 <= size - 1; i += 4)
        {
            var a = RV128.Load(ref rw0R, ref rw1R, i);
            var b = RV128.Load(ref rw0R, ref rw1R, i + 1);
            (a.Xnor(b) & rowMaskV).Store(ref eq0R, ref eq1R, i);
        }
        for (; i < size - 1; i++)
        {
            eq0[i] = ~(rw0[i] ^ rw0[i + 1]) & rowMaskR.W0;
            eq1[i] = ~(rw1[i] ^ rw1[i + 1]) & rowMaskR.W1;
        }

        y = 0;
        for (; y + 4 <= size - 1; y += 4)
        {
            var x = RV128.Load(ref rw0R, ref rw1R, y);
            var eqv = RV128.Load(ref eq0R, ref eq1R, y);
            var eqh = x.Xnor(x.ShiftRight(1));
            var m = eqh & eqv & eqv.ShiftRight(1) & maskN1V;
            accP2 += m.Pop();
        }
        for (; y < size - 1; y++)
        {
            var x = new Row192(rw0[y], rw1[y], 0);
            var eqv = new Row192(eq0[y], eq1[y], 0);
            var eqh = x.Xnor(x.ShiftRight(1));
            var m = eqh & eqv & eqv.ShiftRight(1) & maskN1R;
            score2 += 3 * m.PopCount();
        }

        v50[3] = 0;
        v51[3] = 0;
        y = 4;
        for (; y + 4 <= size; y += 4)
        {
            var e1 = RV128.Load(ref eq0R, ref eq1R, y - 4);
            var e2 = RV128.Load(ref eq0R, ref eq1R, y - 3);
            var e3 = RV128.Load(ref eq0R, ref eq1R, y - 2);
            var e4 = RV128.Load(ref eq0R, ref eq1R, y - 1);
            (e1 & e2 & e3 & e4).Store(ref v50R, ref v51R, y);
        }
        for (; y < size; y++)
        {
            v50[y] = eq0[y - 4] & eq0[y - 3] & eq0[y - 2] & eq0[y - 1];
            v51[y] = eq1[y - 4] & eq1[y - 3] & eq1[y - 2] & eq1[y - 1];
        }

        y = 4;
        for (; y + 4 <= size; y += 4)
        {
            var v5 = RV128.Load(ref v50R, ref v51R, y);
            var prev = RV128.Load(ref v50R, ref v51R, y - 1);
            accOnes += v5.Pop();
            accTwos += v5.AndNotWith(prev).Pop();
        }
        for (; y < size; y++)
        {
            var v5 = new Row192(v50[y], v51[y], 0);
            var prev = new Row192(v50[y - 1], v51[y - 1], 0);
            score1 += v5.PopCount() + 2 * v5.AndNotWith(prev).PopCount();
        }

        var b0 = 0;
        for (; b0 + 4 <= size - 10; b0 += 4)
        {
            var r0 = RV128.Load(ref rw0R, ref rw1R, b0);
            var r2 = RV128.Load(ref rw0R, ref rw1R, b0 + 2);
            var r3 = RV128.Load(ref rw0R, ref rw1R, b0 + 3);
            var r4 = RV128.Load(ref rw0R, ref rw1R, b0 + 4);
            var r6 = RV128.Load(ref rw0R, ref rw1R, b0 + 6);
            var r7 = RV128.Load(ref rw0R, ref rw1R, b0 + 7);
            var r8 = RV128.Load(ref rw0R, ref rw1R, b0 + 8);
            var r10 = RV128.Load(ref rw0R, ref rw1R, b0 + 10);
            var n0 = RV128.Load(ref nw0R, ref nw1R, b0);
            var n1 = RV128.Load(ref nw0R, ref nw1R, b0 + 1);
            var n2c = RV128.Load(ref nw0R, ref nw1R, b0 + 2);
            var n3 = RV128.Load(ref nw0R, ref nw1R, b0 + 3);
            var n5c = RV128.Load(ref nw0R, ref nw1R, b0 + 5);
            var n7 = RV128.Load(ref nw0R, ref nw1R, b0 + 7);
            var n8 = RV128.Load(ref nw0R, ref nw1R, b0 + 8);
            var n9 = RV128.Load(ref nw0R, ref nw1R, b0 + 9);
            var n10 = RV128.Load(ref nw0R, ref nw1R, b0 + 10);

            var mf = n0 & n1 & n2c & n3 & r4 & n5c & r6 & r7 & r8 & n9 & r10;
            var mb = r0 & n1 & r2 & r3 & r4 & n5c & r6 & n7 & n8 & n9 & n10;
            accP3 += mf.Pop() + mb.Pop();
        }
        for (; b0 <= size - 11; b0++)
        {
            var mfw0 = nw0[b0] & nw0[b0 + 1] & nw0[b0 + 2] & nw0[b0 + 3]
                     & rw0[b0 + 4] & nw0[b0 + 5] & rw0[b0 + 6] & rw0[b0 + 7]
                     & rw0[b0 + 8] & nw0[b0 + 9] & rw0[b0 + 10];
            var mfw1 = nw1[b0] & nw1[b0 + 1] & nw1[b0 + 2] & nw1[b0 + 3]
                     & rw1[b0 + 4] & nw1[b0 + 5] & rw1[b0 + 6] & rw1[b0 + 7]
                     & rw1[b0 + 8] & nw1[b0 + 9] & rw1[b0 + 10];
            var mbw0 = rw0[b0] & nw0[b0 + 1] & rw0[b0 + 2] & rw0[b0 + 3]
                     & rw0[b0 + 4] & nw0[b0 + 5] & rw0[b0 + 6] & nw0[b0 + 7]
                     & nw0[b0 + 8] & nw0[b0 + 9] & nw0[b0 + 10];
            var mbw1 = rw1[b0] & nw1[b0 + 1] & rw1[b0 + 2] & rw1[b0 + 3]
                     & rw1[b0 + 4] & nw1[b0 + 5] & rw1[b0 + 6] & nw1[b0 + 7]
                     & nw1[b0 + 8] & nw1[b0 + 9] & nw1[b0 + 10];
            score3 += 40 * (PopCount(mfw0) + PopCount(mfw1) + PopCount(mbw0) + PopCount(mbw1));
        }

        score1 += (int)Vector256.Sum(accOnes) + 2 * (int)Vector256.Sum(accTwos);
        score2 += 3 * (int)Vector256.Sum(accP2);
        score3 += 40 * (int)Vector256.Sum(accP3);
        blackModules += (int)Vector256.Sum(accBlack);

        return score1 + score2 + score3 + CalculateBalanceScore(blackModules, size);
    }

    // ---------------------------------
    // Three-word SoA tier (versions 30-40, size > 128)
    // ---------------------------------

    internal static int MaskCode192Simd(Span<byte> buffer, int size, int version, ReadOnlySpan<byte> blockedMask, ECCLevel eccLevel)
    {
        var packedRent = System.Buffers.ArrayPool<Row192>.Shared.Rent(2 * size);
        var wordsRent = System.Buffers.ArrayPool<ulong>.Shared.Rent(12 * size);
        try
        {
            var packed = packedRent.AsSpan(0, size);
            var allowed = packedRent.AsSpan(size, size);

            PackAllSimd(buffer, blockedMask, size, version, packed, allowed);

            var mw0 = wordsRent.AsSpan(0, size);
            var mw1 = wordsRent.AsSpan(size, size);
            var mw2 = wordsRent.AsSpan(2 * size, size);
            var nw0 = wordsRent.AsSpan(3 * size, size);
            var nw1 = wordsRent.AsSpan(4 * size, size);
            var nw2 = wordsRent.AsSpan(5 * size, size);
            var eq0 = wordsRent.AsSpan(6 * size, size);
            var eq1 = wordsRent.AsSpan(7 * size, size);
            var eq2 = wordsRent.AsSpan(8 * size, size);
            var v50 = wordsRent.AsSpan(9 * size, size);
            var v51 = wordsRent.AsSpan(10 * size, size);
            var v52 = wordsRent.AsSpan(11 * size, size);

            var templates = _maskTemplates;
            var bestPatternIndex = 0;
            var bestScore = int.MaxValue;
            for (var patternIndex = 0; patternIndex < 8; patternIndex++)
            {
                var tplBase = patternIndex * 12;
                for (int y = 0, tplRow = 0; y < size; y++)
                {
                    var t = templates[tplBase + tplRow];
                    var a = allowed[y];
                    var p = packed[y];
                    mw0[y] = p.W0 ^ (t.W0 & a.W0);
                    mw1[y] = p.W1 ^ (t.W1 & a.W1);
                    mw2[y] = p.W2 ^ (t.W2 & a.W2);
                    if (++tplRow == 12) tplRow = 0;
                }
                PokeFormatBitsSoA3(mw0, mw1, mw2, size, QRCodeConstants.GetFormatBits(eccLevel, patternIndex));

                var score = CalculateScore192Vec(mw0, mw1, mw2, nw0, nw1, nw2, eq0, eq1, eq2, v50, v51, v52, size);
                if (score < bestScore)
                {
                    bestPatternIndex = patternIndex;
                    bestScore = score;
                }
            }

            ApplyWinnerSimd(buffer, size, bestPatternIndex, allowed);
            return bestPatternIndex;
        }
        finally
        {
            System.Buffers.ArrayPool<Row192>.Shared.Return(packedRent);
            System.Buffers.ArrayPool<ulong>.Shared.Return(wordsRent);
        }
    }

    /// <summary>Packs buffer rows (SIMD movemask), pokes version bits once, and builds allowed rows (bit slices).</summary>
    private static void PackAllSimd(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> blockedMask, int size, int version,
        Span<Row192> packed, Span<Row192> allowed)
    {
        for (var y = 0; y < size; y++)
        {
            packed[y] = PackRowBits192Simd(buffer.Slice(y * size, size));
        }

        // This path only serves size > 64, i.e. version >= 12, so always poke.
        var versionBits = QRCodeConstants.GetVersionBits(version);
        for (var x = 0; x < 6; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                var bit = (versionBits & (1u << (x * 3 + y))) != 0;
                packed[y + size - 11] = packed[y + size - 11].WithBit(x, bit);
                packed[x] = packed[x].WithBit(y + size - 11, bit);
            }
        }

        // Padded copy so the four 8-byte slice reads per row never overrun.
        Span<byte> padded = stackalloc byte[blockedMask.Length + 32];
        padded.Clear();
        blockedMask.CopyTo(padded);
        var rowMask = Row192.MaskLow(size);
        for (var y = 0; y < size; y++)
        {
            allowed[y] = Row192.FromBitSlice(padded, y * size).AndNot(rowMask);
        }
    }

    /// <summary>Applies the winning pattern's packed XOR delta to the byte buffer, 32 modules per step.</summary>
    private static void ApplyWinnerSimd(Span<byte> buffer, int size, int bestPatternIndex, ReadOnlySpan<Row192> allowed)
    {
        var tplBase = bestPatternIndex * 12;
        ref var bufRef = ref MemoryMarshal.GetReference(buffer);
        for (int y = 0, tplRow = 0; y < size; y++)
        {
            var delta = _maskTemplates[tplBase + tplRow] & allowed[y];
            XorUnpackRow192Simd(ref Unsafe.Add(ref bufRef, y * size), size, delta);
            if (++tplRow == 12) tplRow = 0;
        }
    }

    private static void PokeFormatBitsSoA3(Span<ulong> w0, Span<ulong> w1, Span<ulong> w2, int size, ushort formatBits)
    {
        // Same coordinate scheme as PokeFormatBits64 (see FormatXs1/FormatYs1).
        for (var i = 0; i < 15; i++)
        {
            var bit = (formatBits & (1 << i)) != 0;
            SetBitSoA3(w0, w1, w2, FormatYs1[i], FormatXs1[i], bit);
            var x2 = i < 8 ? size - 1 - i : 8;
            var y2 = i < 8 ? 8 : size - 15 + i;
            SetBitSoA3(w0, w1, w2, y2, x2, bit);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetBitSoA3(Span<ulong> w0, Span<ulong> w1, Span<ulong> w2, int y, int x, bool value)
    {
        ref var w = ref (x < 64 ? ref w0[y] : ref (x < 128 ? ref w1[y] : ref w2[y]));
        var bit = 1ul << (x & 63);
        w = value ? w | bit : w & ~bit;
    }

    /// <summary>Penalty-3 row matches over a Row192 (scalar-tail helper; forward = [0,0,0,0,1,0,1,1,1,0,1], backward reversed).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MatchFinderRowSimd(in Row192 x, in Row192 nx, in Row192 startMask)
    {
        var mf = nx & nx.ShiftRight(1) & nx.ShiftRight(2) & nx.ShiftRight(3)
               & x.ShiftRight(4) & nx.ShiftRight(5) & x.ShiftRight(6) & x.ShiftRight(7)
               & x.ShiftRight(8) & nx.ShiftRight(9) & x.ShiftRight(10) & startMask;
        var mb = x & nx.ShiftRight(1) & x.ShiftRight(2) & x.ShiftRight(3)
               & x.ShiftRight(4) & nx.ShiftRight(5) & x.ShiftRight(6) & nx.ShiftRight(7)
               & nx.ShiftRight(8) & nx.ShiftRight(9) & nx.ShiftRight(10) & startMask;
        return 40 * (mf.PopCount() + mb.PopCount());
    }

    /// <summary>4 rows x 192 bits in SoA form: word k of rows y..y+3 in one vector.</summary>
    private readonly struct RV192
    {
        public readonly Vector256<ulong> A, B, C;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV192(Vector256<ulong> a, Vector256<ulong> b, Vector256<ulong> c) { A = a; B = b; C = c; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RV192 Load(ref ulong w0, ref ulong w1, ref ulong w2, int y)
            => new(Vector256.LoadUnsafe(ref w0, (nuint)y), Vector256.LoadUnsafe(ref w1, (nuint)y), Vector256.LoadUnsafe(ref w2, (nuint)y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(ref ulong w0, ref ulong w1, ref ulong w2, int y)
        {
            A.StoreUnsafe(ref w0, (nuint)y);
            B.StoreUnsafe(ref w1, (nuint)y);
            C.StoreUnsafe(ref w2, (nuint)y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RV192 operator &(in RV192 x, in RV192 y) => new(x.A & y.A, x.B & y.B, x.C & y.C);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RV192 operator ^(in RV192 x, in RV192 y) => new(x.A ^ y.A, x.B ^ y.B, x.C ^ y.C);

        /// <summary>~(this ^ other).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV192 Xnor(in RV192 o) => new(~(A ^ o.A), ~(B ^ o.B), ~(C ^ o.C));

        /// <summary>this &amp; ~other (Vector256.AndNot(left, right) == left &amp; ~right; see the operand-order note above).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV192 AndNotWith(in RV192 o) => new(Vector256.AndNot(A, o.A), Vector256.AndNot(B, o.B), Vector256.AndNot(C, o.C));

        /// <summary>Logical shift right by k bits (1..63), pulling neighbor-word bits in at the top of each lane.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV192 ShiftRight(int k)
            => new(Vector256.ShiftRightLogical(A, k) | Vector256.ShiftLeft(B, 64 - k),
                   Vector256.ShiftRightLogical(B, k) | Vector256.ShiftLeft(C, 64 - k),
                   Vector256.ShiftRightLogical(C, k));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RV192 ShiftLeft1()
            => new(Vector256.ShiftLeft(A, 1),
                   Vector256.ShiftLeft(B, 1) | Vector256.ShiftRightLogical(A, 63),
                   Vector256.ShiftLeft(C, 1) | Vector256.ShiftRightLogical(B, 63));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<ulong> Pop() => Pop256(A) + Pop256(B) + Pop256(C);
    }

    /// <summary>Three-word SoA Vector256 penalty scorer (structure mirrors <see cref="CalculateScore64Vec"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static int CalculateScore192Vec(
        Span<ulong> rw0, Span<ulong> rw1, Span<ulong> rw2,
        Span<ulong> nw0, Span<ulong> nw1, Span<ulong> nw2,
        Span<ulong> eq0, Span<ulong> eq1, Span<ulong> eq2,
        Span<ulong> v50, Span<ulong> v51, Span<ulong> v52, int size)
    {
        var rowMaskR = Row192.MaskLow(size);
        var startMaskR = Row192.MaskLow(size - 10);
        var maskN1R = Row192.MaskLow(size - 1);

        ref var rw0R = ref MemoryMarshal.GetReference(rw0);
        ref var rw1R = ref MemoryMarshal.GetReference(rw1);
        ref var rw2R = ref MemoryMarshal.GetReference(rw2);
        ref var nw0R = ref MemoryMarshal.GetReference(nw0);
        ref var nw1R = ref MemoryMarshal.GetReference(nw1);
        ref var nw2R = ref MemoryMarshal.GetReference(nw2);
        ref var eq0R = ref MemoryMarshal.GetReference(eq0);
        ref var eq1R = ref MemoryMarshal.GetReference(eq1);
        ref var eq2R = ref MemoryMarshal.GetReference(eq2);
        ref var v50R = ref MemoryMarshal.GetReference(v50);
        ref var v51R = ref MemoryMarshal.GetReference(v51);
        ref var v52R = ref MemoryMarshal.GetReference(v52);

        var score1 = 0;
        var score2 = 0;
        var score3 = 0;
        var blackModules = 0;

        var rowMaskV = new RV192(Vector256.Create(rowMaskR.W0), Vector256.Create(rowMaskR.W1), Vector256.Create(rowMaskR.W2));
        var startMaskV = new RV192(Vector256.Create(startMaskR.W0), Vector256.Create(startMaskR.W1), Vector256.Create(startMaskR.W2));
        var maskN1V = new RV192(Vector256.Create(maskN1R.W0), Vector256.Create(maskN1R.W1), Vector256.Create(maskN1R.W2));

        var accOnes = Vector256<ulong>.Zero;
        var accTwos = Vector256<ulong>.Zero;
        var accP2 = Vector256<ulong>.Zero;
        var accP3 = Vector256<ulong>.Zero;
        var accBlack = Vector256<ulong>.Zero;

        var y0 = 0;
        for (; y0 + 4 <= size; y0 += 4)
        {
            var x = RV192.Load(ref rw0R, ref rw1R, ref rw2R, y0);
            var nx = new RV192(Vector256.AndNot(rowMaskV.A, x.A), Vector256.AndNot(rowMaskV.B, x.B), Vector256.AndNot(rowMaskV.C, x.C));
            nx.Store(ref nw0R, ref nw1R, ref nw2R, y0);
        }
        for (; y0 < size; y0++)
        {
            nw0[y0] = ~rw0[y0] & rowMaskR.W0;
            nw1[y0] = ~rw1[y0] & rowMaskR.W1;
            nw2[y0] = ~rw2[y0] & rowMaskR.W2;
        }

        var y = 0;
        for (; y + 4 <= size; y += 4)
        {
            var x = RV192.Load(ref rw0R, ref rw1R, ref rw2R, y);
            var nx = RV192.Load(ref nw0R, ref nw1R, ref nw2R, y);

            accBlack += x.Pop();

            var y2 = x & x.ShiftRight(1);
            var y4 = y2 & y2.ShiftRight(2);
            var y5 = y4 & x.ShiftRight(4);
            var st = y5.AndNotWith(y5.ShiftLeft1());
            var n2 = nx & nx.ShiftRight(1);
            var n4 = n2 & n2.ShiftRight(2);
            var n5 = n4 & nx.ShiftRight(4);
            var nst = n5.AndNotWith(n5.ShiftLeft1());
            accOnes += y5.Pop() + n5.Pop();
            accTwos += st.Pop() + nst.Pop();

            var mf = nx & nx.ShiftRight(1) & nx.ShiftRight(2) & nx.ShiftRight(3)
                   & x.ShiftRight(4) & nx.ShiftRight(5) & x.ShiftRight(6) & x.ShiftRight(7)
                   & x.ShiftRight(8) & nx.ShiftRight(9) & x.ShiftRight(10) & startMaskV;
            var mb = x & nx.ShiftRight(1) & x.ShiftRight(2) & x.ShiftRight(3)
                   & x.ShiftRight(4) & nx.ShiftRight(5) & x.ShiftRight(6) & nx.ShiftRight(7)
                   & nx.ShiftRight(8) & nx.ShiftRight(9) & nx.ShiftRight(10) & startMaskV;
            accP3 += mf.Pop() + mb.Pop();
        }
        for (; y < size; y++)
        {
            var x = new Row192(rw0[y], rw1[y], rw2[y]);
            var nx = new Row192(nw0[y], nw1[y], nw2[y]);
            blackModules += x.PopCount();
            score1 += ScoreRuns(x) + ScoreRuns(nx);
            score3 += MatchFinderRowSimd(x, nx, startMaskR);
        }

        var i = 0;
        for (; i + 4 <= size - 1; i += 4)
        {
            var a = RV192.Load(ref rw0R, ref rw1R, ref rw2R, i);
            var b = RV192.Load(ref rw0R, ref rw1R, ref rw2R, i + 1);
            (a.Xnor(b) & rowMaskV).Store(ref eq0R, ref eq1R, ref eq2R, i);
        }
        for (; i < size - 1; i++)
        {
            eq0[i] = ~(rw0[i] ^ rw0[i + 1]) & rowMaskR.W0;
            eq1[i] = ~(rw1[i] ^ rw1[i + 1]) & rowMaskR.W1;
            eq2[i] = ~(rw2[i] ^ rw2[i + 1]) & rowMaskR.W2;
        }

        y = 0;
        for (; y + 4 <= size - 1; y += 4)
        {
            var x = RV192.Load(ref rw0R, ref rw1R, ref rw2R, y);
            var eqv = RV192.Load(ref eq0R, ref eq1R, ref eq2R, y);
            var eqh = x.Xnor(x.ShiftRight(1));
            var m = eqh & eqv & eqv.ShiftRight(1) & maskN1V;
            accP2 += m.Pop();
        }
        for (; y < size - 1; y++)
        {
            var x = new Row192(rw0[y], rw1[y], rw2[y]);
            var eqv = new Row192(eq0[y], eq1[y], eq2[y]);
            var eqh = x.Xnor(x.ShiftRight(1));
            var m = eqh & eqv & eqv.ShiftRight(1) & maskN1R;
            score2 += 3 * m.PopCount();
        }

        v50[3] = 0;
        v51[3] = 0;
        v52[3] = 0;
        y = 4;
        for (; y + 4 <= size; y += 4)
        {
            var e1 = RV192.Load(ref eq0R, ref eq1R, ref eq2R, y - 4);
            var e2 = RV192.Load(ref eq0R, ref eq1R, ref eq2R, y - 3);
            var e3 = RV192.Load(ref eq0R, ref eq1R, ref eq2R, y - 2);
            var e4 = RV192.Load(ref eq0R, ref eq1R, ref eq2R, y - 1);
            (e1 & e2 & e3 & e4).Store(ref v50R, ref v51R, ref v52R, y);
        }
        for (; y < size; y++)
        {
            v50[y] = eq0[y - 4] & eq0[y - 3] & eq0[y - 2] & eq0[y - 1];
            v51[y] = eq1[y - 4] & eq1[y - 3] & eq1[y - 2] & eq1[y - 1];
            v52[y] = eq2[y - 4] & eq2[y - 3] & eq2[y - 2] & eq2[y - 1];
        }

        y = 4;
        for (; y + 4 <= size; y += 4)
        {
            var v5 = RV192.Load(ref v50R, ref v51R, ref v52R, y);
            var prev = RV192.Load(ref v50R, ref v51R, ref v52R, y - 1);
            accOnes += v5.Pop();
            accTwos += v5.AndNotWith(prev).Pop();
        }
        for (; y < size; y++)
        {
            var v5 = new Row192(v50[y], v51[y], v52[y]);
            var prev = new Row192(v50[y - 1], v51[y - 1], v52[y - 1]);
            score1 += v5.PopCount() + 2 * v5.AndNotWith(prev).PopCount();
        }

        var b0 = 0;
        for (; b0 + 4 <= size - 10; b0 += 4)
        {
            var r0 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0);
            var r2 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 2);
            var r3 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 3);
            var r4 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 4);
            var r6 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 6);
            var r7 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 7);
            var r8 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 8);
            var r10 = RV192.Load(ref rw0R, ref rw1R, ref rw2R, b0 + 10);
            var n0 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0);
            var n1 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 1);
            var n2c = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 2);
            var n3 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 3);
            var n5c = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 5);
            var n7 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 7);
            var n8 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 8);
            var n9 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 9);
            var n10 = RV192.Load(ref nw0R, ref nw1R, ref nw2R, b0 + 10);

            var mf = n0 & n1 & n2c & n3 & r4 & n5c & r6 & r7 & r8 & n9 & r10;
            var mb = r0 & n1 & r2 & r3 & r4 & n5c & r6 & n7 & n8 & n9 & n10;
            accP3 += mf.Pop() + mb.Pop();
        }
        for (; b0 <= size - 11; b0++)
        {
            var mf = new Row192(nw0[b0], nw1[b0], nw2[b0])
                   & new Row192(nw0[b0 + 1], nw1[b0 + 1], nw2[b0 + 1])
                   & new Row192(nw0[b0 + 2], nw1[b0 + 2], nw2[b0 + 2])
                   & new Row192(nw0[b0 + 3], nw1[b0 + 3], nw2[b0 + 3])
                   & new Row192(rw0[b0 + 4], rw1[b0 + 4], rw2[b0 + 4])
                   & new Row192(nw0[b0 + 5], nw1[b0 + 5], nw2[b0 + 5])
                   & new Row192(rw0[b0 + 6], rw1[b0 + 6], rw2[b0 + 6])
                   & new Row192(rw0[b0 + 7], rw1[b0 + 7], rw2[b0 + 7])
                   & new Row192(rw0[b0 + 8], rw1[b0 + 8], rw2[b0 + 8])
                   & new Row192(nw0[b0 + 9], nw1[b0 + 9], nw2[b0 + 9])
                   & new Row192(rw0[b0 + 10], rw1[b0 + 10], rw2[b0 + 10]);
            var mb = new Row192(rw0[b0], rw1[b0], rw2[b0])
                   & new Row192(nw0[b0 + 1], nw1[b0 + 1], nw2[b0 + 1])
                   & new Row192(rw0[b0 + 2], rw1[b0 + 2], rw2[b0 + 2])
                   & new Row192(rw0[b0 + 3], rw1[b0 + 3], rw2[b0 + 3])
                   & new Row192(rw0[b0 + 4], rw1[b0 + 4], rw2[b0 + 4])
                   & new Row192(nw0[b0 + 5], nw1[b0 + 5], nw2[b0 + 5])
                   & new Row192(rw0[b0 + 6], rw1[b0 + 6], rw2[b0 + 6])
                   & new Row192(nw0[b0 + 7], nw1[b0 + 7], nw2[b0 + 7])
                   & new Row192(nw0[b0 + 8], nw1[b0 + 8], nw2[b0 + 8])
                   & new Row192(nw0[b0 + 9], nw1[b0 + 9], nw2[b0 + 9])
                   & new Row192(nw0[b0 + 10], nw1[b0 + 10], nw2[b0 + 10]);
            score3 += 40 * (mf.PopCount() + mb.PopCount());
        }

        score1 += (int)Vector256.Sum(accOnes) + 2 * (int)Vector256.Sum(accTwos);
        score2 += 3 * (int)Vector256.Sum(accP2);
        score3 += 40 * (int)Vector256.Sum(accP3);
        blackModules += (int)Vector256.Sum(accBlack);

        return score1 + score2 + score3 + CalculateBalanceScore(blackModules, size);
    }
}
#endif
