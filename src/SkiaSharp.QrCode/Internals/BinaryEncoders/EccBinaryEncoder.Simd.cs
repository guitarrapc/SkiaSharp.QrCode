#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Vectorized Reed-Solomon kernels for x86/x64. Selected at runtime by
/// <see cref="EccBinaryEncoder.CalculateECC"/>; every kernel produces byte-identical
/// output to <see cref="EccBinaryEncoder.CalculateEccScalar"/>.
/// </summary>
/// <remarks>
/// Shared architecture of both kernels (result of a measured 13-round optimization
/// loop; the discarded alternatives and reasoning live in the private MicroBenchmarks
/// repository):
///
/// - The ≤32-byte remainder register lives in one (eccCount ≤ 16) or two vector
///   registers for the whole block — no message buffer, no store/load round-trips.
/// - Four data bytes are consumed per iteration. Their division factors are a
///   GF(2)-linear function of the four input bytes, so they come from four parallel
///   256-entry uint table lookups (T) instead of a serial per-byte recurrence.
/// - Iterations are software-pipelined: the next quad's factors are derived from the
///   PRE-update register using composed T∘U tables, which keeps the whole vector
///   update off the critical dependency chain (same idea as multi-bit CRC tables).
/// - The register update multiplies the (shifted) generator vector by each factor:
///   GFNI does this in one gf2p8affineqb per factor; the SSSE3 kernel uses the
///   classic PSHUFB nibble-split multiply (2 shuffles per factor) instead.
///
/// Table cost: ~8 KB static (SSSE3 nibble-split table) + 2 KB static (GFNI matrices),
/// plus ~8-18 KB per distinct eccCount actually used (QR uses at most 13 values;
/// a typical app touches one or two). All tables build lazily on first use.
/// </remarks>
internal static partial class EccBinaryEncoder
{
    /// <summary>
    /// Entry point for vectorized kernels. Caller guarantees Ssse3.IsSupported and
    /// eccCount ≤ 32 (QR maximum is 30).
    /// </summary>
    internal static void CalculateEccSimd(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
#if NET10_0_OR_GREATER
        if (Gfni.IsSupported)
        {
            CalculateEccGfni(data, ecc, eccCount);
            return;
        }
#endif
        // Covers all pre-GFNI x86 (AVX2-only CPUs included; wider vectors measured
        // slower than dual 128-bit registers for this kernel).
        CalculateEccSsse3(data, ecc, eccCount);
    }

    // ---------------------------------------------------------------------------
    // SSSE3 kernel (PSHUFB nibble-split GF multiply)
    // ---------------------------------------------------------------------------

    internal static void CalculateEccSsse3(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        if (eccCount <= 16)
        {
            Ssse3Core128(data, ecc, eccCount);
        }
        else
        {
            Ssse3CoreDual128(data, ecc, eccCount);
        }
    }

    private static void Ssse3Core128(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        var nib = GetNibbleTables(eccCount);
        ref var nibRef = ref MemoryMarshal.GetArrayDataReference(nib);
        ref var mulBase = ref MemoryMarshal.GetArrayDataReference(s_nibbleMulTable);
        ref var qf = ref MemoryMarshal.GetArrayDataReference(GetQuadFactorTables(eccCount));
        ref var tu = ref MemoryMarshal.GetArrayDataReference(GetComposedTUTables(eccCount));
        ref var dataRef = ref MemoryMarshal.GetReference(data);

        var genLo = Vector128.LoadUnsafe(ref nibRef, NibGenLoA);
        var genHi = Vector128.LoadUnsafe(ref nibRef, NibGenHiA);
        var genS1Lo = Vector128.LoadUnsafe(ref nibRef, NibGenS1LoA);
        var genS1Hi = Vector128.LoadUnsafe(ref nibRef, NibGenS1HiA);
        var genS2Lo = Vector128.LoadUnsafe(ref nibRef, NibGenS2LoA);
        var genS2Hi = Vector128.LoadUnsafe(ref nibRef, NibGenS2HiA);
        var genS3Lo = Vector128.LoadUnsafe(ref nibRef, NibGenS3LoA);
        var genS3Hi = Vector128.LoadUnsafe(ref nibRef, NibGenS3HiA);

        var reg = Vector128<byte>.Zero; // remainder register, ecc[0] = byte 0

        var i = 0;
        if (data.Length >= 4)
        {
            // First quad's factors come straight from the data (register is zero).
            var d0 = Unsafe.ReadUnaligned<uint>(ref dataRef);
            var f = QuadLookup(ref qf, d0);

            for (i = 4; i + 3 < data.Length; i += 4)
            {
                // Next quad's input from the PRE-update register (software pipelining):
                // x_next = d_next ^ reg[4..7] ^ U(f), and by GF(2)-linearity
                // T(x_next) = T(d_next ^ reg[4..7]) ^ (T∘U)(f).
                var z = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, i)) ^ reg.AsUInt32().GetElement(1);
                var y = QuadLookup(ref qf, z);

                ref var t0 = ref Unsafe.Add(ref mulBase, (nint)((f & 0xFF) * 32));
                ref var t1 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 8) & 0xFF) * 32));
                ref var t2 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 16) & 0xFF) * 32));
                ref var t3 = ref Unsafe.Add(ref mulBase, (nint)((f >> 24) * 32));

                // reg = (reg >> 4 bytes) ^ (gen>>3)·f0 ^ (gen>>2)·f1 ^ (gen>>1)·f2 ^ gen·f3
                reg = Sse2.ShiftRightLogical128BitLane(reg, 4)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t0), genS3Lo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t0, 16), genS3Hi)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t1), genS2Lo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t1, 16), genS2Hi)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t2), genS1Lo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t2, 16), genS1Hi)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t3), genLo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t3, 16), genHi);

                f = QuadLookup(ref tu, f) ^ y;
            }

            // Final full quad (its factors were prepared by the last loop iteration).
            {
                ref var t0 = ref Unsafe.Add(ref mulBase, (nint)((f & 0xFF) * 32));
                ref var t1 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 8) & 0xFF) * 32));
                ref var t2 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 16) & 0xFF) * 32));
                ref var t3 = ref Unsafe.Add(ref mulBase, (nint)((f >> 24) * 32));

                reg = Sse2.ShiftRightLogical128BitLane(reg, 4)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t0), genS3Lo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t0, 16), genS3Hi)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t1), genS2Lo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t1, 16), genS2Hi)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t2), genS1Lo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t2, 16), genS1Hi)
                    ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t3), genLo) ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t3, 16), genHi);
            }
        }

        // 0..3 trailing bytes, single-step
        for (; i < data.Length; i++)
        {
            var f = (uint)(Unsafe.Add(ref dataRef, i) ^ reg.ToScalar());
            ref var t = ref Unsafe.Add(ref mulBase, (nint)(f * 32));
            reg = Sse2.ShiftRightLogical128BitLane(reg, 1)
                ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t), genLo)
                ^ Ssse3.Shuffle(Vector128.LoadUnsafe(ref t, 16), genHi);
        }

        Span<byte> tmp = stackalloc byte[16];
        reg.StoreUnsafe(ref MemoryMarshal.GetReference(tmp));
        tmp.Slice(0, eccCount).CopyTo(ecc);
    }

    private static void Ssse3CoreDual128(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        var nib = GetNibbleTables(eccCount);
        ref var nibRef = ref MemoryMarshal.GetArrayDataReference(nib);
        ref var mulBase = ref MemoryMarshal.GetArrayDataReference(s_nibbleMulTable);
        ref var qf = ref MemoryMarshal.GetArrayDataReference(GetQuadFactorTables(eccCount));
        ref var tu = ref MemoryMarshal.GetArrayDataReference(GetComposedTUTables(eccCount));
        ref var dataRef = ref MemoryMarshal.GetReference(data);

        var genLoA = Vector128.LoadUnsafe(ref nibRef, NibGenLoA);
        var genHiA = Vector128.LoadUnsafe(ref nibRef, NibGenHiA);
        var genLoB = Vector128.LoadUnsafe(ref nibRef, NibGenLoB);
        var genHiB = Vector128.LoadUnsafe(ref nibRef, NibGenHiB);
        var genS1LoA = Vector128.LoadUnsafe(ref nibRef, NibGenS1LoA);
        var genS1HiA = Vector128.LoadUnsafe(ref nibRef, NibGenS1HiA);
        var genS1LoB = Vector128.LoadUnsafe(ref nibRef, NibGenS1LoB);
        var genS1HiB = Vector128.LoadUnsafe(ref nibRef, NibGenS1HiB);
        var genS2LoA = Vector128.LoadUnsafe(ref nibRef, NibGenS2LoA);
        var genS2HiA = Vector128.LoadUnsafe(ref nibRef, NibGenS2HiA);
        var genS2LoB = Vector128.LoadUnsafe(ref nibRef, NibGenS2LoB);
        var genS2HiB = Vector128.LoadUnsafe(ref nibRef, NibGenS2HiB);
        var genS3LoA = Vector128.LoadUnsafe(ref nibRef, NibGenS3LoA);
        var genS3HiA = Vector128.LoadUnsafe(ref nibRef, NibGenS3HiA);
        var genS3LoB = Vector128.LoadUnsafe(ref nibRef, NibGenS3LoB);
        var genS3HiB = Vector128.LoadUnsafe(ref nibRef, NibGenS3HiB);

        // Remainder register as two 128-bit halves: lo = ecc[0..15], hi = ecc[16..31].
        // Measured faster than one 256-bit register (no cross-lane shift, no table
        // broadcast on the dependency chain).
        var lo = Vector128<byte>.Zero;
        var hi = Vector128<byte>.Zero;

        var i = 0;
        if (data.Length >= 4)
        {
            var d0 = Unsafe.ReadUnaligned<uint>(ref dataRef);
            var f = QuadLookup(ref qf, d0);

            for (i = 4; i + 3 < data.Length; i += 4)
            {
                var z = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, i)) ^ lo.AsUInt32().GetElement(1);
                var y = QuadLookup(ref qf, z);

                ref var t0 = ref Unsafe.Add(ref mulBase, (nint)((f & 0xFF) * 32));
                ref var t1 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 8) & 0xFF) * 32));
                ref var t2 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 16) & 0xFF) * 32));
                ref var t3 = ref Unsafe.Add(ref mulBase, (nint)((f >> 24) * 32));

                var tblLo0 = Vector128.LoadUnsafe(ref t0);
                var tblHi0 = Vector128.LoadUnsafe(ref t0, 16);
                var tblLo1 = Vector128.LoadUnsafe(ref t1);
                var tblHi1 = Vector128.LoadUnsafe(ref t1, 16);
                var tblLo2 = Vector128.LoadUnsafe(ref t2);
                var tblHi2 = Vector128.LoadUnsafe(ref t2, 16);
                var tblLo3 = Vector128.LoadUnsafe(ref t3);
                var tblHi3 = Vector128.LoadUnsafe(ref t3, 16);

                // Shift the 32-byte register left by 4 array positions:
                // newLo = lo[4..15] ++ hi[0..3], newHi = hi[4..15] ++ 0000.
                var newLo = Ssse3.AlignRight(hi, lo, 4);
                var newHi = Sse2.ShiftRightLogical128BitLane(hi, 4);

                lo = newLo
                    ^ Ssse3.Shuffle(tblLo0, genS3LoA) ^ Ssse3.Shuffle(tblHi0, genS3HiA)
                    ^ Ssse3.Shuffle(tblLo1, genS2LoA) ^ Ssse3.Shuffle(tblHi1, genS2HiA)
                    ^ Ssse3.Shuffle(tblLo2, genS1LoA) ^ Ssse3.Shuffle(tblHi2, genS1HiA)
                    ^ Ssse3.Shuffle(tblLo3, genLoA) ^ Ssse3.Shuffle(tblHi3, genHiA);
                hi = newHi
                    ^ Ssse3.Shuffle(tblLo0, genS3LoB) ^ Ssse3.Shuffle(tblHi0, genS3HiB)
                    ^ Ssse3.Shuffle(tblLo1, genS2LoB) ^ Ssse3.Shuffle(tblHi1, genS2HiB)
                    ^ Ssse3.Shuffle(tblLo2, genS1LoB) ^ Ssse3.Shuffle(tblHi2, genS1HiB)
                    ^ Ssse3.Shuffle(tblLo3, genLoB) ^ Ssse3.Shuffle(tblHi3, genHiB);

                f = QuadLookup(ref tu, f) ^ y;
            }

            // Final full quad
            {
                ref var t0 = ref Unsafe.Add(ref mulBase, (nint)((f & 0xFF) * 32));
                ref var t1 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 8) & 0xFF) * 32));
                ref var t2 = ref Unsafe.Add(ref mulBase, (nint)(((f >> 16) & 0xFF) * 32));
                ref var t3 = ref Unsafe.Add(ref mulBase, (nint)((f >> 24) * 32));

                var tblLo0 = Vector128.LoadUnsafe(ref t0);
                var tblHi0 = Vector128.LoadUnsafe(ref t0, 16);
                var tblLo1 = Vector128.LoadUnsafe(ref t1);
                var tblHi1 = Vector128.LoadUnsafe(ref t1, 16);
                var tblLo2 = Vector128.LoadUnsafe(ref t2);
                var tblHi2 = Vector128.LoadUnsafe(ref t2, 16);
                var tblLo3 = Vector128.LoadUnsafe(ref t3);
                var tblHi3 = Vector128.LoadUnsafe(ref t3, 16);

                var newLo = Ssse3.AlignRight(hi, lo, 4);
                var newHi = Sse2.ShiftRightLogical128BitLane(hi, 4);

                lo = newLo
                    ^ Ssse3.Shuffle(tblLo0, genS3LoA) ^ Ssse3.Shuffle(tblHi0, genS3HiA)
                    ^ Ssse3.Shuffle(tblLo1, genS2LoA) ^ Ssse3.Shuffle(tblHi1, genS2HiA)
                    ^ Ssse3.Shuffle(tblLo2, genS1LoA) ^ Ssse3.Shuffle(tblHi2, genS1HiA)
                    ^ Ssse3.Shuffle(tblLo3, genLoA) ^ Ssse3.Shuffle(tblHi3, genHiA);
                hi = newHi
                    ^ Ssse3.Shuffle(tblLo0, genS3LoB) ^ Ssse3.Shuffle(tblHi0, genS3HiB)
                    ^ Ssse3.Shuffle(tblLo1, genS2LoB) ^ Ssse3.Shuffle(tblHi1, genS2HiB)
                    ^ Ssse3.Shuffle(tblLo2, genS1LoB) ^ Ssse3.Shuffle(tblHi2, genS1HiB)
                    ^ Ssse3.Shuffle(tblLo3, genLoB) ^ Ssse3.Shuffle(tblHi3, genHiB);
            }
        }

        for (; i < data.Length; i++)
        {
            var f = (uint)(Unsafe.Add(ref dataRef, i) ^ lo.ToScalar());
            ref var t = ref Unsafe.Add(ref mulBase, (nint)(f * 32));
            var tblLo = Vector128.LoadUnsafe(ref t);
            var tblHi = Vector128.LoadUnsafe(ref t, 16);
            var newLo = Ssse3.AlignRight(hi, lo, 1);
            var newHi = Sse2.ShiftRightLogical128BitLane(hi, 1);
            lo = newLo ^ Ssse3.Shuffle(tblLo, genLoA) ^ Ssse3.Shuffle(tblHi, genHiA);
            hi = newHi ^ Ssse3.Shuffle(tblLo, genLoB) ^ Ssse3.Shuffle(tblHi, genHiB);
        }

        Span<byte> tmp = stackalloc byte[32];
        lo.StoreUnsafe(ref MemoryMarshal.GetReference(tmp));
        hi.StoreUnsafe(ref MemoryMarshal.GetReference(tmp), 16);
        tmp.Slice(0, eccCount).CopyTo(ecc);
    }

#if NET10_0_OR_GREATER
    // ---------------------------------------------------------------------------
    // GFNI kernel (gf2p8affineqb: whole-vector GF multiply in one instruction)
    // ---------------------------------------------------------------------------

    internal static void CalculateEccGfni(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        if (eccCount <= 16)
        {
            GfniCore128(data, ecc, eccCount);
        }
        else if (Gfni.V256.IsSupported && Avx2.IsSupported)
        {
            GfniCore256(data, ecc, eccCount);
        }
        else
        {
            Ssse3CoreDual128(data, ecc, eccCount);
        }
    }

    private static void GfniCore128(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        ref var blob = ref MemoryMarshal.GetArrayDataReference(GetBlob(eccCount));
        ref var gm = ref MemoryMarshal.GetArrayDataReference(s_gfniMatrix);
        ref var dataRef = ref MemoryMarshal.GetReference(data);

        // Raw shifted generator vectors: (gen >> s)[j] = gen[j + s]
        var gen0v = Vector128.LoadUnsafe(ref blob, BlobGen);
        var gen1v = Vector128.LoadUnsafe(ref blob, BlobGen + 1);
        var gen2v = Vector128.LoadUnsafe(ref blob, BlobGen + 2);
        var gen3v = Vector128.LoadUnsafe(ref blob, BlobGen + 3);

        var reg = Vector128<byte>.Zero;

        var i = 0;
        if (data.Length >= 4)
        {
            var d0 = Unsafe.ReadUnaligned<uint>(ref dataRef);
            var f = BlobQuadLookup(ref blob, BlobQf, d0);

            for (i = 4; i + 3 < data.Length; i += 4)
            {
                var z = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, i)) ^ reg.AsUInt32().GetElement(1);
                var y = BlobQuadLookup(ref blob, BlobQf, z);

                var m0 = Vector128.Create(ReadMatrix(ref gm, f & 0xFF)).AsByte();
                var m1 = Vector128.Create(ReadMatrix(ref gm, (f >> 8) & 0xFF)).AsByte();
                var m2 = Vector128.Create(ReadMatrix(ref gm, (f >> 16) & 0xFF)).AsByte();
                var m3 = Vector128.Create(ReadMatrix(ref gm, f >> 24)).AsByte();

                reg = Sse2.ShiftRightLogical128BitLane(reg, 4)
                    ^ Gfni.GaloisFieldAffineTransform(gen3v, m0, 0)
                    ^ Gfni.GaloisFieldAffineTransform(gen2v, m1, 0)
                    ^ Gfni.GaloisFieldAffineTransform(gen1v, m2, 0)
                    ^ Gfni.GaloisFieldAffineTransform(gen0v, m3, 0);

                f = BlobQuadLookup(ref blob, BlobTu, f) ^ y;
            }

            // Final full quad
            {
                var m0 = Vector128.Create(ReadMatrix(ref gm, f & 0xFF)).AsByte();
                var m1 = Vector128.Create(ReadMatrix(ref gm, (f >> 8) & 0xFF)).AsByte();
                var m2 = Vector128.Create(ReadMatrix(ref gm, (f >> 16) & 0xFF)).AsByte();
                var m3 = Vector128.Create(ReadMatrix(ref gm, f >> 24)).AsByte();

                reg = Sse2.ShiftRightLogical128BitLane(reg, 4)
                    ^ Gfni.GaloisFieldAffineTransform(gen3v, m0, 0)
                    ^ Gfni.GaloisFieldAffineTransform(gen2v, m1, 0)
                    ^ Gfni.GaloisFieldAffineTransform(gen1v, m2, 0)
                    ^ Gfni.GaloisFieldAffineTransform(gen0v, m3, 0);
            }
        }

        for (; i < data.Length; i++)
        {
            var f = (uint)(Unsafe.Add(ref dataRef, i) ^ reg.ToScalar());
            var m = Vector128.Create(ReadMatrix(ref gm, f)).AsByte();
            reg = Sse2.ShiftRightLogical128BitLane(reg, 1)
                ^ Gfni.GaloisFieldAffineTransform(gen0v, m, 0);
        }

        Span<byte> tmp = stackalloc byte[16];
        reg.StoreUnsafe(ref MemoryMarshal.GetReference(tmp));
        tmp.Slice(0, eccCount).CopyTo(ecc);
    }

    private static void GfniCore256(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        ref var blob = ref MemoryMarshal.GetArrayDataReference(GetBlob(eccCount));
        ref var gm = ref MemoryMarshal.GetArrayDataReference(s_gfniMatrix);
        ref var dataRef = ref MemoryMarshal.GetReference(data);

        var gen0v = Vector256.LoadUnsafe(ref blob, BlobGen);
        var gen1v = Vector256.LoadUnsafe(ref blob, BlobGen + 1);
        var gen2v = Vector256.LoadUnsafe(ref blob, BlobGen + 2);
        var gen3v = Vector256.LoadUnsafe(ref blob, BlobGen + 3);

        var reg = Vector256<byte>.Zero;

        var i = 0;
        if (data.Length >= 4)
        {
            var d0 = Unsafe.ReadUnaligned<uint>(ref dataRef);
            var f = BlobQuadLookup(ref blob, BlobQf, d0);

            for (i = 4; i + 3 < data.Length; i += 4)
            {
                var z = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, i)) ^ reg.AsUInt32().GetElement(1);
                var y = BlobQuadLookup(ref blob, BlobQf, z);

                var m0 = Vector256.Create(ReadMatrix(ref gm, f & 0xFF)).AsByte();
                var m1 = Vector256.Create(ReadMatrix(ref gm, (f >> 8) & 0xFF)).AsByte();
                var m2 = Vector256.Create(ReadMatrix(ref gm, (f >> 16) & 0xFF)).AsByte();
                var m3 = Vector256.Create(ReadMatrix(ref gm, f >> 24)).AsByte();

                // 256-bit byte shift right by 4 across the lane boundary
                var carry = Avx2.Permute2x128(reg, reg, 0x81);
                reg = Avx2.AlignRight(carry, reg, 4)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen3v, m0, 0)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen2v, m1, 0)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen1v, m2, 0)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen0v, m3, 0);

                f = BlobQuadLookup(ref blob, BlobTu, f) ^ y;
            }

            // Final full quad
            {
                var m0 = Vector256.Create(ReadMatrix(ref gm, f & 0xFF)).AsByte();
                var m1 = Vector256.Create(ReadMatrix(ref gm, (f >> 8) & 0xFF)).AsByte();
                var m2 = Vector256.Create(ReadMatrix(ref gm, (f >> 16) & 0xFF)).AsByte();
                var m3 = Vector256.Create(ReadMatrix(ref gm, f >> 24)).AsByte();

                var carry = Avx2.Permute2x128(reg, reg, 0x81);
                reg = Avx2.AlignRight(carry, reg, 4)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen3v, m0, 0)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen2v, m1, 0)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen1v, m2, 0)
                    ^ Gfni.V256.GaloisFieldAffineTransform(gen0v, m3, 0);
            }
        }

        for (; i < data.Length; i++)
        {
            var f = (uint)(Unsafe.Add(ref dataRef, i) ^ reg.ToScalar());
            var m = Vector256.Create(ReadMatrix(ref gm, f)).AsByte();
            var carry = Avx2.Permute2x128(reg, reg, 0x81);
            reg = Avx2.AlignRight(carry, reg, 1)
                ^ Gfni.V256.GaloisFieldAffineTransform(gen0v, m, 0);
        }

        Span<byte> tmp = stackalloc byte[32];
        reg.StoreUnsafe(ref MemoryMarshal.GetReference(tmp));
        tmp.Slice(0, eccCount).CopyTo(ecc);
    }
#endif // NET10_0_OR_GREATER

    // ---------------------------------------------------------------------------
    // Lookup helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// XOR of the four per-byte-position table entries for the packed input
    /// <paramref name="x"/>: T0[x0] ^ T1[x1] ^ T2[x2] ^ T3[x3].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint QuadLookup(ref uint table, uint x)
        => Unsafe.Add(ref table, (nint)(x & 0xFF))
         ^ Unsafe.Add(ref table, 256 + (nint)((x >> 8) & 0xFF))
         ^ Unsafe.Add(ref table, 512 + (nint)((x >> 16) & 0xFF))
         ^ Unsafe.Add(ref table, 768 + (nint)(x >> 24));

#if NET10_0_OR_GREATER
    /// <summary>Same as <see cref="QuadLookup"/> against a byte-offset region of the fused blob.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BlobQuadLookup(ref byte blob, nint regionOffset, uint x)
        => Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref blob, regionOffset + (nint)((x & 0xFF) * 4)))
         ^ Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref blob, regionOffset + 1024 + (nint)(((x >> 8) & 0xFF) * 4)))
         ^ Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref blob, regionOffset + 2048 + (nint)(((x >> 16) & 0xFF) * 4)))
         ^ Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref blob, regionOffset + 3072 + (nint)((x >> 24) * 4)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadMatrix(ref ulong matrices, uint factor)
        => Unsafe.Add(ref matrices, (nint)factor);
#endif

    // ---------------------------------------------------------------------------
    // Table construction (all lazy, per eccCount; benign races — identical results,
    // atomic reference assignment)
    // ---------------------------------------------------------------------------

    // Normal-domain generator coefficients (generator[1..eccCount], leading 1
    // dropped), zero-padded to 32 bytes. GF-multiplying zero padding contributes 0,
    // so padded lanes are no-ops in the vector kernels.
    private static readonly byte[]?[] s_paddedGenCache = new byte[]?[33];

    private static byte[] GetPaddedGenerator(int eccCount)
    {
        var cached = s_paddedGenCache[eccCount];
        if (cached is not null) return cached;

        Span<byte> generator = stackalloc byte[eccCount + 1];
        GenerateGeneratorPolynomial(generator, eccCount);

        var padded = new byte[32];
        generator.Slice(1, eccCount).CopyTo(padded);

        s_paddedGenCache[eccCount] = padded;
        return padded;
    }

    // PSHUFB nibble-split GF(256) multiplication tables: for each factor c,
    // 32 bytes = TLo (c·n for low nibble n) followed by THi (c·(n<<4) for high
    // nibble n), so c·x = TLo[x & 0xF] ^ THi[x >> 4]. 256 factors × 32 B = 8 KB.
    private static readonly byte[] s_nibbleMulTable = BuildNibbleMulTable();

    private static byte[] BuildNibbleMulTable()
    {
        var table = new byte[256 * 32];
        for (var c = 0; c < 256; c++)
        {
            for (var n = 0; n < 16; n++)
            {
                table[c * 32 + n] = GaloisField.Multiply((byte)c, (byte)n);
                table[c * 32 + 16 + n] = GaloisField.Multiply((byte)c, (byte)(n << 4));
            }
        }
        return table;
    }

    // Shuffle-ready nibble split of (gen >> s bytes) for s = 0..3, each as
    // [loA|hiA|loB|hiB] 16-byte blocks (A = gen bytes 0..15, B = 16..31).
    // Offsets below; 256 bytes per eccCount.
    private const nuint NibGenLoA = 0;
    private const nuint NibGenHiA = 16;
    private const nuint NibGenLoB = 32;
    private const nuint NibGenHiB = 48;
    private const nuint NibGenS1LoA = 64;
    private const nuint NibGenS1HiA = 80;
    private const nuint NibGenS1LoB = 96;
    private const nuint NibGenS1HiB = 112;
    private const nuint NibGenS2LoA = 128;
    private const nuint NibGenS2HiA = 144;
    private const nuint NibGenS2LoB = 160;
    private const nuint NibGenS2HiB = 176;
    private const nuint NibGenS3LoA = 192;
    private const nuint NibGenS3HiA = 208;
    private const nuint NibGenS3LoB = 224;
    private const nuint NibGenS3HiB = 240;

    private static readonly byte[]?[] s_nibbleCache = new byte[]?[33];

    private static byte[] GetNibbleTables(int eccCount)
    {
        var cached = s_nibbleCache[eccCount];
        if (cached is not null) return cached;

        var padded = GetPaddedGenerator(eccCount);
        var t = new byte[256];
        for (var shift = 0; shift < 4; shift++)
        {
            var baseOff = shift * 64;
            for (var j = 0; j < 32; j++)
            {
                var idx = j + shift;
                var v = idx < padded.Length ? padded[idx] : (byte)0;
                var half = j >> 4; // 0 = A (bytes 0..15), 1 = B (bytes 16..31)
                var lane = j & 15;
                t[baseOff + half * 32 + lane] = (byte)(v & 0x0F);
                t[baseOff + half * 32 + 16 + lane] = (byte)(v >> 4);
            }
        }

        s_nibbleCache[eccCount] = t;
        return t;
    }

    // Quad factor tables T: T_k[b] = packed division factors (f0..f3) produced by
    // input byte b at position k with all other bytes zero. Factors for a quad are
    // the XOR of the four entries (GF(2)-linearity). 4 KB per eccCount.
    private static readonly uint[]?[] s_quadFactorCache = new uint[]?[33];

    private static uint[] GetQuadFactorTables(int eccCount)
    {
        var cached = s_quadFactorCache[eccCount];
        if (cached is not null) return cached;

        var padded = GetPaddedGenerator(eccCount);
        var g0 = padded[0];
        var g1 = padded[1];
        var g2 = padded[2];

        var t = new uint[4 * 256];
        for (var k = 0; k < 4; k++)
        {
            for (var b = 0; b < 256; b++)
            {
                // Run the factor recurrence with w = b at position k, zeros elsewhere:
                // f0 = w0; f_j = w_j ^ Σ gen[j-1-m]·f_m
                var w0 = k == 0 ? (byte)b : (byte)0;
                var w1 = k == 1 ? (byte)b : (byte)0;
                var w2 = k == 2 ? (byte)b : (byte)0;
                var w3 = k == 3 ? (byte)b : (byte)0;

                var f0 = w0;
                var f1 = (byte)(w1 ^ GaloisField.Multiply(g0, f0));
                var f2 = (byte)(w2 ^ GaloisField.Multiply(g1, f0) ^ GaloisField.Multiply(g0, f1));
                var f3 = (byte)(w3 ^ GaloisField.Multiply(g2, f0) ^ GaloisField.Multiply(g1, f1) ^ GaloisField.Multiply(g0, f2));

                t[k * 256 + b] = f0 | ((uint)f1 << 8) | ((uint)f2 << 16) | ((uint)f3 << 24);
            }
        }

        s_quadFactorCache[eccCount] = t;
        return t;
    }

    // Quad update tables U: U_k[b] = bytes 0..3 of factor f_k = b's contribution to
    // the updated remainder register (padded[j + 3 - k]·b packed for j = 0..3).
    private static readonly uint[]?[] s_quadUpdateCache = new uint[]?[33];

    private static uint[] GetQuadUpdateTables(int eccCount)
    {
        var cached = s_quadUpdateCache[eccCount];
        if (cached is not null) return cached;

        var padded = GetPaddedGenerator(eccCount);
        var t = new uint[4 * 256];
        for (var k = 0; k < 4; k++)
        {
            for (var b = 0; b < 256; b++)
            {
                var v = 0u;
                for (var j = 0; j < 4; j++)
                {
                    v |= (uint)GaloisField.Multiply(padded[j + 3 - k], (byte)b) << (j * 8);
                }
                t[k * 256 + b] = v;
            }
        }

        s_quadUpdateCache[eccCount] = t;
        return t;
    }

    // Composed T∘U tables: TU_k[b] = T(U_k[b]) — the next quad's factor contribution
    // of current factor f_k = b, folding two dependent lookup rounds into one.
    // Valid because T and U are GF(2)-linear maps.
    private static readonly uint[]?[] s_composedTUCache = new uint[]?[33];

    private static uint[] GetComposedTUTables(int eccCount)
    {
        var cached = s_composedTUCache[eccCount];
        if (cached is not null) return cached;

        var qf = GetQuadFactorTables(eccCount);
        var uf = GetQuadUpdateTables(eccCount);

        var t = new uint[4 * 256];
        for (var k = 0; k < 4; k++)
        {
            for (var b = 0; b < 256; b++)
            {
                var u = uf[k * 256 + b];
                t[k * 256 + b] = qf[u & 0xFF]
                    ^ qf[256 + (int)((u >> 8) & 0xFF)]
                    ^ qf[512 + (int)((u >> 16) & 0xFF)]
                    ^ qf[768 + (int)(u >> 24)];
            }
        }

        s_composedTUCache[eccCount] = t;
        return t;
    }

#if NET10_0_OR_GREATER
    // Fused per-eccCount blob for the GFNI kernel: quad-factor tables, composed TU
    // tables and the padded generator behind a single pointer chase (measured ~13%
    // on the smallest QR block vs separate arrays).
    private const nint BlobQf = 0;      // 4 × 256 uints (4096 bytes)
    private const nint BlobTu = 4096;   // 4 × 256 uints (4096 bytes)
    private const nuint BlobGen = 8192; // 40 bytes: padded generator + shift headroom

    private static readonly byte[]?[] s_blobCache = new byte[]?[33];

    private static byte[] GetBlob(int eccCount)
    {
        var cached = s_blobCache[eccCount];
        if (cached is not null) return cached;

        var blob = new byte[8192 + 40];
        MemoryMarshal.AsBytes(GetQuadFactorTables(eccCount).AsSpan()).CopyTo(blob.AsSpan((int)BlobQf, 4096));
        MemoryMarshal.AsBytes(GetComposedTUTables(eccCount).AsSpan()).CopyTo(blob.AsSpan((int)BlobTu, 4096));
        GetPaddedGenerator(eccCount).CopyTo(blob.AsSpan((int)BlobGen, 32)); // last 8 bytes stay 0

        s_blobCache[eccCount] = blob;
        return blob;
    }

    // GFNI multiply-by-f bit matrices (universal, 2 KB). gf2p8affineqb convention:
    // qword byte (7-i) = matrix row for result bit i, row bit k = bit i of f·2^k
    // (identity matrix = 0x0102040810204080).
    private static readonly ulong[] s_gfniMatrix = BuildGfniMatrices();

    private static ulong[] BuildGfniMatrices()
    {
        var t = new ulong[256];
        for (var f = 0; f < 256; f++)
        {
            var m = 0UL;
            for (var i = 0; i < 8; i++)
            {
                byte row = 0;
                for (var k = 0; k < 8; k++)
                {
                    var prod = GaloisField.Multiply((byte)f, (byte)(1 << k));
                    row |= (byte)(((prod >> i) & 1) << k);
                }
                m |= (ulong)row << ((7 - i) * 8);
            }
            t[f] = m;
        }
        return t;
    }
#endif // NET10_0_OR_GREATER
}
#endif // NET8_0_OR_GREATER
