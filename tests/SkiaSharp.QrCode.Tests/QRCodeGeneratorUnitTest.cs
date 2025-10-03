using System;
using Xunit;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;
using static SkiaSharp.QrCode.QRCodeGenerator;

namespace SkiaSharp.QrCode.Tests;

public class QRCodeGeneratorUnitTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    internal void CalculateMaxBitStringLength_InvalidVersionShouldFail(int version)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CalculateMaxBitStringLength(version, ECCLevel.L, EncodingMode.Alphanumeric));
        Assert.Contains($"Version must be 1-40, but was {version}", ex.Message);
    }

    [Theory]
    [InlineData(1, ECCLevel.L, EncodingMode.Numeric, 152)]         // 19 × 8
    [InlineData(1, ECCLevel.M, EncodingMode.Alphanumeric, 128)]    // 16 × 8
    [InlineData(1, ECCLevel.Q, EncodingMode.Byte, 104)]            // 13 × 8
    [InlineData(1, ECCLevel.H, EncodingMode.Byte, 72)]             // 9 × 8
    [InlineData(40, ECCLevel.L, EncodingMode.Numeric, 23648)]      // 2956 × 8
    [InlineData(40, ECCLevel.M, EncodingMode.Alphanumeric, 18672)] // 2334 × 8
    [InlineData(40, ECCLevel.Q, EncodingMode.Byte, 13328)]         // 1666 × 8
    [InlineData(40, ECCLevel.H, EncodingMode.Byte, 10208)]         // 1276 × 8
    internal void CalculateMaxBitStringLength_ReturnsCapacityWithBuffer(int version, ECCLevel eccLevel, EncodingMode encoding, int expected)
    {
        var actual = CalculateMaxBitStringLength(version, eccLevel, encoding);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CalculateMaxBitStringLength_IsIndependentOfInputText()
    {
        // Capacity doesn't depend on input text (always padded to full capacity)
        var capacity1 = CalculateMaxBitStringLength(1, ECCLevel.L, EncodingMode.Byte);
        var capacity2 = CalculateMaxBitStringLength(1, ECCLevel.L, EncodingMode.Byte);

        Assert.Equal(capacity1, capacity2);
    }
}
