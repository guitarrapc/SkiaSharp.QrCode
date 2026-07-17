using SkiaSharp.QrCode.Internals.MicroQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Format information decoding: the single 15-bit Micro QR copy is matched
/// against all 32 valid patterns (8 symbol numbers × 4 masks) with BCH(15,5)
/// distance-3 tolerance. Verified exhaustively against a naive Hamming-distance
/// reference over the full 15-bit space.
/// </summary>
public class MicroQrFormatInformationDecoderUnitTest
{
    public static IEnumerable<(MicroQrVersion version, MicroQrEccLevel ecc)> ValidCombinations()
    {
        yield return (MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly);
        yield return (MicroQrVersion.M2, MicroQrEccLevel.L);
        yield return (MicroQrVersion.M2, MicroQrEccLevel.M);
        yield return (MicroQrVersion.M3, MicroQrEccLevel.L);
        yield return (MicroQrVersion.M3, MicroQrEccLevel.M);
        yield return (MicroQrVersion.M4, MicroQrEccLevel.L);
        yield return (MicroQrVersion.M4, MicroQrEccLevel.M);
        yield return (MicroQrVersion.M4, MicroQrEccLevel.Q);
    }

    [Test]
    [MethodDataSource(nameof(ValidCombinations))]
    public async Task TryDecode_ExactPattern_RoundTripsAllMasks(MicroQrVersion version, MicroQrEccLevel ecc)
    {
        for (var mask = 0; mask < 4; mask++)
        {
            var bits = MicroQrConstants.GetFormatBits(version, ecc, mask);

            var success = MicroQrFormatInformationDecoder.TryDecode(bits, out var decodedVersion, out var decodedEcc, out var decodedMask);

            await Assert.That(success).IsTrue();
            await Assert.That(decodedVersion).IsEqualTo(version);
            await Assert.That(decodedEcc).IsEqualTo(ecc);
            await Assert.That(decodedMask).IsEqualTo(mask);
        }
    }

    [Test]
    [MethodDataSource(nameof(ValidCombinations))]
    public async Task TryDecode_UpToThreeFlippedBits_StillDecodes(MicroQrVersion version, MicroQrEccLevel ecc)
    {
        // Deterministic 1/2/3-bit error patterns spread over the 15-bit word.
        ReadOnlyMemory<int[]> flipSets = new[]
        {
            new[] { 0 }, new[] { 14 }, new[] { 7 },
            new[] { 0, 14 }, new[] { 3, 11 },
            new[] { 0, 7, 14 }, new[] { 1, 6, 12 },
        };

        for (var mask = 0; mask < 4; mask++)
        {
            var bits = MicroQrConstants.GetFormatBits(version, ecc, mask);
            foreach (var flips in flipSets.ToArray())
            {
                var damaged = bits;
                foreach (var bit in flips)
                {
                    damaged ^= (ushort)(1 << bit);
                }

                var success = MicroQrFormatInformationDecoder.TryDecode(damaged, out var decodedVersion, out var decodedEcc, out var decodedMask);

                await Assert.That(success).IsTrue();
                await Assert.That(decodedVersion).IsEqualTo(version);
                await Assert.That(decodedEcc).IsEqualTo(ecc);
                await Assert.That(decodedMask).IsEqualTo(mask);
            }
        }
    }

    [Test]
    public async Task TryDecode_ExhaustiveRawSpace_MatchesNaiveNearestCandidateReference()
    {
        // Naive reference: nearest candidate by Hamming distance, accepted at ≤ 3.
        var candidates = new List<(ushort bits, MicroQrVersion version, MicroQrEccLevel ecc, int mask)>();
        foreach (var (version, ecc) in ValidCombinations())
        {
            for (var mask = 0; mask < 4; mask++)
            {
                candidates.Add((MicroQrConstants.GetFormatBits(version, ecc, mask), version, ecc, mask));
            }
        }
        await Assert.That(candidates.Count).IsEqualTo(32);

        for (var raw = 0; raw < (1 << 15); raw++)
        {
            var bestDistance = int.MaxValue;
            var best = candidates[0];
            foreach (var candidate in candidates)
            {
                var distance = System.Numerics.BitOperations.PopCount((uint)(raw ^ candidate.bits));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            var success = MicroQrFormatInformationDecoder.TryDecode((ushort)raw, out var decodedVersion, out var decodedEcc, out var decodedMask);

            if (bestDistance <= 3)
            {
                if (!success || decodedVersion != best.version || decodedEcc != best.ecc || decodedMask != best.mask)
                {
                    await Assert.That(success).IsTrue();
                    await Assert.That(decodedVersion).IsEqualTo(best.version);
                    await Assert.That(decodedEcc).IsEqualTo(best.ecc);
                    await Assert.That(decodedMask).IsEqualTo(best.mask);
                }
            }
            else if (success)
            {
                // BCH(15,5) min distance is 7, so distance > 3 must be rejected.
                await Assert.That(success).IsFalse();
            }
        }
    }

    [Test]
    [Arguments(MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly, 0)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.L, 1)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.M, 2)]
    [Arguments(MicroQrVersion.M3, MicroQrEccLevel.L, 2)]
    [Arguments(MicroQrVersion.M3, MicroQrEccLevel.M, 4)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.L, 3)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.M, 5)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.Q, 7)]
    public async Task GetErrorCorrectionCapacity_MatchesIsoTable9(MicroQrVersion version, MicroQrEccLevel ecc, int expectedCapacity)
    {
        // ISO/IEC 18004 Table 9: 2t + p = ecc codewords (M1 p=2, M2-L p=3,
        // M2-M/M3-L/M4-L p=2, others p=0).
        await Assert.That(MicroQrConstants.GetErrorCorrectionCapacity(version, ecc)).IsEqualTo(expectedCapacity);
    }
}
