using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryDecoders;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Unit tests for format information decoding: exact match for all 32 valid
/// patterns, BCH(15,5) correction up to 3 bit errors, copy selection, and
/// rejection beyond correction distance.
/// </summary>
public class FormatInformationDecoderUnitTest
{
    [Fact]
    public void AllValidPatterns_DecodeExactly()
    {
        for (var level = 0; level < 4; level++)
        {
            for (var mask = 0; mask < 8; mask++)
            {
                var bits = QRCodeConstants.GetFormatBits((ECCLevel)level, mask);

                Assert.True(FormatInformationDecoder.TryDecode(bits, bits, out var eccLevel, out var maskPattern));
                Assert.Equal((ECCLevel)level, eccLevel);
                Assert.Equal(mask, maskPattern);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BitErrorsWithinBchCapacity_AreCorrected(int errorBits)
    {
        // Flip N distinct bits in every valid pattern; BCH(15,5) has minimum
        // distance 7, so up to 3 errors must decode to the original.
        for (var level = 0; level < 4; level++)
        {
            for (var mask = 0; mask < 8; mask++)
            {
                var bits = QRCodeConstants.GetFormatBits((ECCLevel)level, mask);
                var corrupted = bits;
                for (var b = 0; b < errorBits; b++)
                {
                    corrupted ^= (ushort)(1 << (b * 5 % 15)); // distinct positions
                }

                Assert.True(FormatInformationDecoder.TryDecode(corrupted, corrupted, out var eccLevel, out var maskPattern), $"level={(ECCLevel)level}, mask={mask}, errors={errorBits}");
                Assert.Equal((ECCLevel)level, eccLevel);
                Assert.Equal(mask, maskPattern);
            }
        }
    }

    [Fact]
    public void CorruptedFirstCopy_IntactSecondCopy_DecodesViaSecond()
    {
        var bits = QRCodeConstants.GetFormatBits(ECCLevel.Q, 5);
        var destroyed = FindPatternFarFromAllFormats();

        Assert.True(FormatInformationDecoder.TryDecode(destroyed, bits, out var eccLevel, out var maskPattern));
        Assert.Equal(ECCLevel.Q, eccLevel);
        Assert.Equal(5, maskPattern);
    }

    [Fact]
    public void BothCopiesBeyondCorrectionDistance_ReturnsFalse()
    {
        var destroyed = FindPatternFarFromAllFormats();

        Assert.False(FormatInformationDecoder.TryDecode(destroyed, destroyed, out _, out _));
    }

    /// <summary>
    /// Finds a 15-bit pattern with Hamming distance &gt; 3 from every valid masked
    /// format pattern (flipping arbitrary bits is not enough: the complement of a
    /// codeword can be close to another codeword).
    /// </summary>
    private static ushort FindPatternFarFromAllFormats()
    {
        for (var pattern = 0; pattern < 0x8000; pattern++)
        {
            var minDistance = int.MaxValue;
            for (var level = 0; level < 4; level++)
            {
                for (var mask = 0; mask < 8; mask++)
                {
                    var candidate = QRCodeConstants.GetFormatBits((ECCLevel)level, mask);
                    var distance = CountBits((ushort)(pattern ^ candidate));
                    if (distance < minDistance)
                        minDistance = distance;
                }
            }
            if (minDistance > 3)
                return (ushort)pattern;
        }
        throw new InvalidOperationException("no pattern found beyond correction distance");
    }

    private static int CountBits(ushort v)
    {
        var count = 0;
        while (v != 0)
        {
            count += v & 1;
            v >>= 1;
        }
        return count;
    }
}
