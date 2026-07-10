using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryEncoders;
using Xunit;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Verifies that the optimized InterleaveCodewords (single-block identity fast path +
/// ref-arithmetic transpose with additive strides) produces byte-identical output to
/// a naive per-element reference implementation of ISO/IEC 18004 Section 7.6
/// (codeword interleaving). The reference walks rows with per-element index
/// multiplication and range conditions, deliberately independent from the production
/// pointer arithmetic.
/// </summary>
public class BinaryInterleaverParityTest
{
    public static TheoryData<int, ECCLevel> AllVersionLevelCombinations()
    {
        var data = new TheoryData<int, ECCLevel>();
        for (var version = 1; version <= 40; version++)
        {
            foreach (var level in new[] { ECCLevel.L, ECCLevel.M, ECCLevel.Q, ECCLevel.H })
            {
                data.Add(version, level);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllVersionLevelCombinations))]
    public void InterleaveCodewords_MatchesReference_AllVersionsAndLevels(int version, ECCLevel level)
    {
        var eccInfo = GetEccInfo(version, level);
        AssertMatchesReference(version, eccInfo);
    }

    [Fact]
    public void InterleaveCodewords_MatchesReference_NonStandardPatterns()
    {
        // Patterns outside the real capacity table exercise the general path's
        // structural edges: equal block lengths across groups, single blocks per
        // group, group 2 only.
        (int Version, ECCInfo EccInfo)[] patterns =
        [
            (1, new ECCInfo(1, ECCLevel.M, 6, 2, 2, 3, 0, 0)),   // group 1 only, 2 blocks
            (5, new ECCInfo(5, ECCLevel.H, 7, 2, 1, 3, 1, 4)),   // 1+1 blocks, cw2 = cw1 + 1
            (5, new ECCInfo(5, ECCLevel.H, 12, 2, 2, 3, 2, 3)),  // cw2 == cw1 (no tail row)
            (5, new ECCInfo(5, ECCLevel.H, 8, 2, 0, 0, 2, 4)),   // group 2 only
        ];

        foreach (var (version, eccInfo) in patterns)
        {
            AssertMatchesReference(version, eccInfo);
        }
    }

    [Fact]
    public void InterleaveCodewords_UndersizedDataBuffer_Throws()
    {
        var eccInfo = GetEccInfo(5, ECCLevel.Q);
        Assert.Throws<ArgumentException>(() =>
        {
            var data = new byte[eccInfo.TotalDataCodewords - 1];
            var ecc = new byte[(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock];
            var output = new byte[BinaryInterleaver.CalculateInterleavedSize(eccInfo, 5)];
            BinaryInterleaver.InterleaveCodewords(data, ecc, output, 5, eccInfo);
        });
    }

    [Fact]
    public void InterleaveCodewords_UndersizedEccBuffer_Throws()
    {
        var eccInfo = GetEccInfo(5, ECCLevel.Q);
        Assert.Throws<ArgumentException>(() =>
        {
            var data = new byte[eccInfo.TotalDataCodewords];
            var ecc = new byte[(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock - 1];
            var output = new byte[BinaryInterleaver.CalculateInterleavedSize(eccInfo, 5)];
            BinaryInterleaver.InterleaveCodewords(data, ecc, output, 5, eccInfo);
        });
    }

    [Fact]
    public void InterleaveCodewords_UndersizedOutputBuffer_Throws()
    {
        var eccInfo = GetEccInfo(5, ECCLevel.Q);
        Assert.Throws<ArgumentException>(() =>
        {
            var data = new byte[eccInfo.TotalDataCodewords];
            var ecc = new byte[(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock];
            var output = new byte[data.Length + ecc.Length - 1];
            BinaryInterleaver.InterleaveCodewords(data, ecc, output, 5, eccInfo);
        });
    }

    private static void AssertMatchesReference(int version, in ECCInfo eccInfo)
    {
        var dataLen = eccInfo.BlocksInGroup1 * eccInfo.CodewordsInGroup1
                    + eccInfo.BlocksInGroup2 * eccInfo.CodewordsInGroup2;
        var eccLen = (eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock;
        var outputSize = BinaryInterleaver.CalculateInterleavedSize(eccInfo, version);

        for (var seed = 0; seed < 3; seed++)
        {
            var data = new byte[dataLen];
            var ecc = new byte[eccLen];
            new Random(seed).NextBytes(data);
            new Random(seed + 100).NextBytes(ecc);

            var expected = new byte[outputSize];
            ReferenceInterleaveCodewords(data, ecc, expected, eccInfo);

            var actual = new byte[outputSize];
            BinaryInterleaver.InterleaveCodewords(data, ecc, actual, version, eccInfo);

            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// Naive reference: round-robin over rows with per-element index multiplication
    /// and per-element range conditions (the pre-optimization implementation shape).
    /// </summary>
    private static void ReferenceInterleaveCodewords(ReadOnlySpan<byte> data, ReadOnlySpan<byte> ecc, Span<byte> output, in ECCInfo eccInfo)
    {
        var outputIndex = 0;
        var totalBlocks = eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2;
        var maxCodewordCount = Math.Max(eccInfo.CodewordsInGroup1, eccInfo.CodewordsInGroup2);

        for (var i = 0; i < maxCodewordCount; i++)
        {
            for (var blockIndex = 0; blockIndex < eccInfo.BlocksInGroup1; blockIndex++)
            {
                if (i < eccInfo.CodewordsInGroup1)
                {
                    var dataIndex = blockIndex * eccInfo.CodewordsInGroup1 + i;
                    output[outputIndex++] = data[dataIndex];
                }
            }

            var group2Offset = eccInfo.BlocksInGroup1 * eccInfo.CodewordsInGroup1;
            for (var blockIndex = 0; blockIndex < eccInfo.BlocksInGroup2; blockIndex++)
            {
                if (i < eccInfo.CodewordsInGroup2)
                {
                    var dataOffset = group2Offset + blockIndex * eccInfo.CodewordsInGroup2 + i;
                    output[outputIndex++] = data[dataOffset];
                }
            }
        }

        for (var i = 0; i < eccInfo.ECCPerBlock; i++)
        {
            for (var blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
            {
                var eccOffset = blockIndex * eccInfo.ECCPerBlock;
                output[outputIndex] = ecc[eccOffset + i];
                outputIndex++;
            }
        }
    }
}
