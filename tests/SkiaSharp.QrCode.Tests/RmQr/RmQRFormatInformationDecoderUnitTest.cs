using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// rMQR format information decoding: each 18-bit copy is matched against the 64
/// valid words of its side (finder-side / sub-finder-side XOR masks), up to 3 bit
/// errors corrected (BCH(18,6) minimum distance ≥ 7); with two copies the closer
/// valid one wins (ties → finder side). Verified exhaustively against a naive
/// nearest-candidate reference over the full 18-bit space for both sides.
/// </summary>
public class RmQRFormatInformationDecoderUnitTest
{
    /// <summary>A raw word farther than 3 bits from every valid word of the side.</summary>
    private static int FindUndecodable(bool subFinderSide)
    {
        for (var raw = 0; raw < 1 << 18; raw++)
        {
            NaiveNearest(raw, subFinderSide, out var distance);
            if (distance > 3)
                return raw;
        }
        throw new InvalidOperationException("every 18-bit word decodes?");
    }

    private static int NaiveNearest(int raw, bool subFinderSide, out int distance)
    {
        var best = -1;
        distance = int.MaxValue;
        for (var index = 0; index < 64; index++)
        {
            var version = (RmQRVersion)((index & 31) + 1);
            var ecc = (RmQREccLevel)(index >> 5);
            var word = RmQRConstants.GetFormatBits(version, ecc, subFinderSide);
            var d = System.Numerics.BitOperations.PopCount((uint)(raw ^ word));
            if (d < distance)
            {
                distance = d;
                best = index;
            }
        }
        return best;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TryDecodeCopy_MatchesNaiveNearest_OverTheFull18BitSpace(bool subFinderSide)
    {
        var mismatches = 0;
        for (var raw = 0; raw < 1 << 18; raw++)
        {
            var expectedIndex = NaiveNearest(raw, subFinderSide, out var expectedDistance);
            var ok = RmQRFormatInformationDecoder.TryDecodeCopy(raw, subFinderSide, out var version, out var ecc, out var distance);
            var expectedOk = expectedDistance <= 3;
            if (ok != expectedOk)
            {
                mismatches++;
                continue;
            }
            if (ok && (((int)version - 1) | ((int)ecc << 5)) != expectedIndex || (ok && distance != expectedDistance))
                mismatches++;
        }
        await Assert.That(mismatches).IsEqualTo(0);
    }

    [Test]
    public async Task TryDecode_TwoCopies_CloserValidCopyWins_TiesPreferFinderSide()
    {
        var left = RmQRConstants.GetFormatBits(RmQRVersion.R13x77, RmQREccLevel.H, false);
        var right = RmQRConstants.GetFormatBits(RmQRVersion.R13x77, RmQREccLevel.H, true);

        // Both clean → agree.
        await Assert.That(RmQRFormatInformationDecoder.TryDecode(left, right, out var v, out var e, out var d)).IsTrue();
        await Assert.That((v, e, d)).IsEqualTo((RmQRVersion.R13x77, RmQREccLevel.H, 0));

        // Finder side has 3 errors, sub-finder side clean → sub-finder wins with distance 0.
        await Assert.That(RmQRFormatInformationDecoder.TryDecode(left ^ 0b111, right, out v, out e, out d)).IsTrue();
        await Assert.That((v, e, d)).IsEqualTo((RmQRVersion.R13x77, RmQREccLevel.H, 0));

        // Finder side beyond correction (4+ errors, chosen so no word is within 3), sub-finder 2 errors → sub-finder wins.
        var invalidLeft = FindUndecodable(false);
        var invalidRight = FindUndecodable(true);
        await Assert.That(RmQRFormatInformationDecoder.TryDecode(invalidLeft, right ^ 0b11, out v, out e, out d)).IsTrue();
        await Assert.That((v, e, d)).IsEqualTo((RmQRVersion.R13x77, RmQREccLevel.H, 2));

        // Both copies decode to different symbols: the closer one wins.
        var otherLeft = RmQRConstants.GetFormatBits(RmQRVersion.R7x43, RmQREccLevel.M, false);
        await Assert.That(RmQRFormatInformationDecoder.TryDecode(otherLeft ^ 0b1, right ^ 0b111, out v, out e, out d)).IsTrue();
        await Assert.That((v, e, d)).IsEqualTo((RmQRVersion.R7x43, RmQREccLevel.M, 1));

        // Tie at equal distance and different symbols → finder side.
        await Assert.That(RmQRFormatInformationDecoder.TryDecode(otherLeft ^ 0b1, right ^ 0b1, out v, out e, out d)).IsTrue();
        await Assert.That((v, e, d)).IsEqualTo((RmQRVersion.R7x43, RmQREccLevel.M, 1));

        // Both beyond the correction distance → fail.
        await Assert.That(RmQRFormatInformationDecoder.TryDecode(invalidLeft, invalidRight, out _, out _, out _)).IsFalse();
        await Assert.That(RmQRFormatInformationDecoder.TryDecodeCopy(invalidLeft, false, out _, out _, out _)).IsFalse();
    }
}
