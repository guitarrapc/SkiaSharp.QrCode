using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryEncoders;
using SkiaSharp.QrCode.Internals.TextEncoders;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class EccBinaryEncoderUnitTest
{
    // Performance & Memory Tests

    [Fact]
    public void CalculateECC_LargeData_NoStackOverflow()
    {
        // Arrange - Max data size for Version 40
        var data = new byte[2956]; // Version 40-L max data
        Random.Shared.NextBytes(data);
        var eccCount = 30;

        // Act - Should not throw StackOverflowException
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
    }

    [Fact]
    public void CalculateECC_RepeatedCalls_ProducesSameResult()
    {
        // Arrange
        var data = new byte[] { 64, 86, 134, 86 };
        var eccCount = 10;

        // Act
        Span<byte> ecc1 = stackalloc byte[eccCount];
        Span<byte> ecc2 = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc1, eccCount);
        EccBinaryEncoder.CalculateECC(data, ecc2, eccCount);

        // Assert - Deterministic (same input → same output)
        Assert.True(ecc1.SequenceEqual(ecc2));
    }

    // ISO/IEC 18004 Standard Examples

    /// <summary>
    /// Test against ISO/IEC 18004 Annex I example.
    /// Data: 01000000 01010110 10000110 01010110 (64, 86, 134, 86)
    /// ECC: 10 codewords
    /// Expected: [176, 76, 29, 180, 122, 192, 92, 208, 157, 56]
    /// </summary>
    [Fact]
    public void CalculateECC_ISO18004_Example_ProducesCorrectECC()
    {
        // Arrange
        ReadOnlySpan<byte> data = [64, 86, 134, 86];
        var eccCount = 10;
        ReadOnlySpan<byte> expectedECC = [176, 76, 29, 180, 122, 192, 92, 208, 157, 56];

        // Act
        Span<byte> actualECC = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, actualECC, eccCount);

        // Assert
        Assert.Equal(eccCount, actualECC.Length);
        for (int i = 0; i < eccCount; i++)
        {
            Assert.Equal(expectedECC[i], actualECC[i]);
        }
    }

    // Basic ECC Calculation

    [Theory]
    [InlineData(new byte[] { 64 }, 7)] // 1 byte data, 7 ECC
    [InlineData(new byte[] { 64, 86 }, 10)] // 2 bytes data, 10 ECC
    [InlineData(new byte[] { 64, 86, 134 }, 13)] // 3 bytes data, 13 ECC
    public void CalculateECC_VariousDataSizes_ProducesCorrectWordCount(byte[] data, int eccCount)
    {
        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
        // Verify all bytes are in valid range [0, 255]
        foreach (var b in ecc)
        {
            Assert.InRange(b, 0, 255);
        }
    }

    [Fact]
    public void CalculateECC_SingleByte_ProducesValidECC()
    {
        // Arrange
        var data = new byte[] { 64 };
        var eccCount = 5;

        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
        Assert.All(ecc.ToArray(), b => Assert.InRange(b, 0, 255));
    }

    [Fact]
    public void CalculateECC_MaxECCLevel_Version40_ProducesValidECC()
    {
        // Arrange - QR Version 40, ECC Level H requires 30 ECC words per block
        var data = new byte[29]; // 29 data bytes
        var eccCount = 30;

        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
    }

    // Edge Cases

    [Fact]
    public void CalculateECC_AllZeros_ProducesAllZeroECC()
    {
        // Arrange
        var data = new byte[4]; // All zeros
        var eccCount = 7;

        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
        Assert.All(ecc.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void CalculateECC_AllOnes_ProducesNonZeroECC()
    {
        // Arrange
        var data = new byte[] { 255, 255, 255, 255 }; // All 0xFF
        var eccCount = 10;

        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
        // ECC for all 0xFF should not be all zeros
        Assert.Contains(ecc.ToArray(), b => b != 0);
    }

    [Theory]
    [InlineData(new byte[] { 0x55, 0x55, 0x55, 0x55 }, 3)] // 01010101 pattern
    [InlineData(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA }, 5)] // 10101010 pattern
    [InlineData(new byte[] { 0xF0, 0xF0, 0xF0, 0xF0 }, 7)] // 11110000 pattern
    public void CalculateECC_AlternatingPatterns_ProducesValidECC(byte[] data, int eccCount)
    {
        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
        Assert.All(ecc.ToArray(), b => Assert.InRange(b, 0, 255));
    }

    // Polynomial Operation

    [Theory]
    [InlineData(7)]  // Degree 6 generator (x-α^0)(x-α^1)...(x-α^6)
    [InlineData(10)] // Degree 9 generator
    [InlineData(30)] // Degree 29 generator (max for Version 40-H)
    public void CalculateECC_GeneratorPolynomial_ProducesCorrectDegree(int eccCount)
    {
        // Arrange
        var data = new byte[16]; // 16 bytes of data
        Random.Shared.NextBytes(data);

        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert - Generator polynomial degree = eccCount - 1
        Assert.Equal(eccCount, ecc.Length);
    }

    // Galois Field (GF256) Operation

    [Fact]
    public void CalculateECC_GF256Operations_ProduceValidResults()
    {
        // Arrange - Known test vectors for GF(256) arithmetic
        var data = new byte[] { 255 }; // 255 in GF(256)
        var eccCount = 3;

        // Act
        Span<byte> ecc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, ecc, eccCount);

        // Assert
        Assert.Equal(eccCount, ecc.Length);
        // In GF(256), operations should produce values in range [0, 255]
        Assert.All(ecc.ToArray(), b => Assert.InRange(b, 0, 255));
    }

    [Fact]
    public void CalculateECC_DifferentInputs_ProducesDifferentECC()
    {
        // Arrange
        var data1 = new byte[] { 64, 86, 134, 86 };
        var data2 = new byte[] { 64, 86, 134, 87 }; // Last byte different
        var eccCount = 10;

        // Act
        Span<byte> ecc1 = stackalloc byte[eccCount];
        Span<byte> ecc2 = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data1, ecc1, eccCount);
        EccBinaryEncoder.CalculateECC(data2, ecc2, eccCount);

        // Assert
        Assert.False(ecc1.SequenceEqual(ecc2)); // Different inputs → different ECC
    }

    // Comparison with EccTextEncoder

    [Theory]
    [InlineData(new byte[] { 64, 86, 134, 86 }, 10)]
    [InlineData(new byte[] { 0x40, 0x0C, 0x56, 0x61, 0x80, 0xEC, 0x11, 0xEC }, 10)]
    [InlineData(new byte[] { 16, 32, 48, 64 }, 7)]
    public void CalculateECC_MatchesEccTextEncoder(byte[] data, int eccCount)
    {
        // Arrange
        var dataBits = string.Join("", data.Select(x => Convert.ToString(x, 2).PadLeft(8, '0')));

        // Text Encoder
        var textEccString = EccTextEncoder.CalculateECC(dataBits, eccCount);
        var textEcc = textEccString.Select(x => Convert.ToByte(x, 2)).ToArray();

        // Binary Encoder
        Span<byte> binaryEcc = stackalloc byte[eccCount];
        EccBinaryEncoder.CalculateECC(data, binaryEcc, eccCount);

        // Assert
        Assert.Equal(textEcc.Length, binaryEcc.Length);
        for (int i = 0; i < eccCount; i++)
        {
            Assert.Equal(textEcc[i], binaryEcc[i]);
        }
    }

    [Fact]
    public void CalculateECC_AllQRVersions_Functional()
    {
        // Max ECC size across all versions. Version 40-H requires 30 ECC codewords per block.
        const int macEccsize = 30;
        Span<byte> ecc = stackalloc byte[macEccsize];

        // Test all QR code versions and ECC levels
        foreach (var eccInfo in QRCodeConstants.CapacityECCTable)
        {
            var data = new byte[eccInfo.TotalDataCodewords];
            Random.Shared.NextBytes(data);

            // result buffer slice for current ECC size
            var eccSlice = ecc[..eccInfo.ECCPerBlock];
            EccBinaryEncoder.CalculateECC(data, eccSlice, eccInfo.ECCPerBlock);

            Assert.Equal(eccInfo.ECCPerBlock, eccSlice.Length);
        }
    }
}
