using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryDecoders;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Parity tests for the syndrome-pass kernels: the scalar log-domain kernel is
/// checked against a naive ISO/IEC 18004 reference (Horner with GaloisField.Multiply),
/// and the GFNI kernel (net10.0+ x64) against the scalar kernel, byte for byte.
/// </summary>
public class EccBinaryDecoderKernelParityTest
{
    [Test]
    public async Task ScalarKernel_MatchesNaiveReference()
    {
        var random = new Random(20260712);
        for (var round = 0; round < 300; round++)
        {
            var length = random.Next(8, 256);
            var eccCount = random.Next(1, 31);
            var codeword = new byte[length];
            if (round % 10 != 0)
                random.NextBytes(codeword); // every 10th round keeps all-zero data

            var expected = new byte[eccCount];
            var expectedHasError = NaiveSyndromes(codeword, eccCount, expected);

            var actual = new byte[eccCount];
            var actualHasError = EccBinaryDecoder.ComputeSyndromesScalar(codeword, eccCount, actual);

            await Assert.That(actualHasError).IsEquivalentTo(expectedHasError);
            await Assert.That(actual).IsEquivalentTo(expected);
        }
    }

#if NET10_0_OR_GREATER
    [Test]
    public async Task GfniKernel_MatchesScalarKernel()
    {
        if (!System.Runtime.Intrinsics.X86.Gfni.V256.IsSupported)
        {
            Skip.Test("GFNI not supported on this machine");
            return;
        }

        var random = new Random(20260712);
        for (var round = 0; round < 300; round++)
        {
            var length = random.Next(8, 256);
            var eccCount = random.Next(1, 31);
            var codeword = new byte[length];
            if (round % 10 != 0)
                random.NextBytes(codeword);

            var expected = new byte[eccCount];
            var expectedHasError = EccBinaryDecoder.ComputeSyndromesScalar(codeword, eccCount, expected);

            var actual = new byte[eccCount];
            var actualHasError = EccBinaryDecoder.ComputeSyndromesGfni(codeword, eccCount, actual);

            await Assert.That(actualHasError).IsEquivalentTo(expectedHasError);
            await Assert.That(actual).IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task GfniIsomorphismConstants_MatchFirstPrinciplesConstruction()
    {
        // Rebuild the GF(0x11D) 竊・GF(0x11B) isomorphism from scratch and verify the
        // constants baked into EccBinaryDecoder.Simd.cs. ﾎｲ is the first root of
        // x^8+x^4+x^3+x^2+1 in GF(0x11B); ﾏ・maps by linearity over the ﾎｲ^i basis.
        var beta = 0;
        for (var cand = 2; cand < 256; cand++)
        {
            var p2 = MulPoly((byte)cand, (byte)cand, 0x11b);
            var p3 = MulPoly(p2, (byte)cand, 0x11b);
            var p4 = MulPoly(p2, p2, 0x11b);
            var p8 = MulPoly(p4, p4, 0x11b);
            if ((p8 ^ p4 ^ p3 ^ p2 ^ 1) == 0)
            {
                beta = cand;
                break;
            }
        }
        await Assert.That(beta).IsNotEqualTo(0);

        Span<byte> basis = stackalloc byte[8];
        basis[0] = 1;
        for (var i = 1; i < 8; i++)
        {
            basis[i] = MulPoly(basis[i - 1], (byte)beta, 0x11b);
        }
        var phi = new byte[256];
        var psi = new byte[256];
        for (var v = 0; v < 256; v++)
        {
            byte m = 0;
            for (var i = 0; i < 8; i++)
            {
                if ((v & (1 << i)) != 0) m ^= basis[i];
            }
            phi[v] = m;
        }
        for (var v = 0; v < 256; v++)
        {
            psi[phi[v]] = (byte)v;
        }

        // Exhaustive multiplicative homomorphism check
        for (var a = 0; a < 256; a++)
        {
            for (var b = 0; b < 256; b++)
            {
                await Assert.That(MulPoly(phi[a], phi[b], 0x11b)).IsEquivalentTo(phi[MulPoly((byte)a, (byte)b, 0x11d)]);
            }
        }

        // Baked matrix constants (ﾏ・happens to be an involution: ﾏ・== ﾏ・
        await Assert.That(BuildAffineMatrix(phi)).IsEquivalentTo(EccBinaryDecoder.GfniPhiMatrix);
        await Assert.That(BuildAffineMatrix(psi)).IsEquivalentTo(EccBinaryDecoder.GfniPhiMatrix);

        // Baked per-lane constants: lane i = ﾏ・ﾎｱ^ki) in the 0x11B domain, k = 1..4
        for (var i = 0; i < 32; i++)
        {
            await Assert.That(phi[GaloisField.Exp[i]]).IsEquivalentTo(EccBinaryDecoder.GfniAlphas[i]);
            await Assert.That(phi[GaloisField.Exp[2 * i]]).IsEquivalentTo(EccBinaryDecoder.GfniAlphasSquared[i]);
            await Assert.That(phi[GaloisField.Exp[3 * i]]).IsEquivalentTo(EccBinaryDecoder.GfniAlphasCubed[i]);
            await Assert.That(phi[GaloisField.Exp[4 * i]]).IsEquivalentTo(EccBinaryDecoder.GfniAlphasFourth[i]);
        }
    }

    private static ulong BuildAffineMatrix(byte[] map)
    {
        var m = 0UL;
        for (var i = 0; i < 8; i++)
        {
            byte row = 0;
            for (var k = 0; k < 8; k++)
            {
                row |= (byte)(((map[1 << k] >> i) & 1) << k);
            }
            m |= (ulong)row << ((7 - i) * 8);
        }
        return m;
    }

    private static byte MulPoly(byte a, byte b, int poly)
    {
        var r = 0;
        var x = (int)a;
        var y = (int)b;
        while (y != 0)
        {
            if ((y & 1) != 0) r ^= x;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= poly;
            y >>= 1;
        }
        return (byte)r;
    }
#endif

    private static bool NaiveSyndromes(ReadOnlySpan<byte> codeword, int eccCount, Span<byte> syndromes)
    {
        var hasError = false;
        for (var i = 0; i < eccCount; i++)
        {
            var x = GaloisField.Exp[i];
            byte value = 0;
            for (var j = 0; j < codeword.Length; j++)
            {
                value = (byte)(GaloisField.Multiply(value, x) ^ codeword[j]);
            }
            syndromes[i] = value;
            hasError |= value != 0;
        }
        return hasError;
    }
}
