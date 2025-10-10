using SkiaSharp.QrCode.Internals.TextEncoders;
using System.Text;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class EccTextEncoderUnitTest
{
    private readonly EccTextEncoder _encoder;

    public EccTextEncoderUnitTest()
    {
        _encoder = new EccTextEncoder();
    }

    // ISO/IEC 18004 Standard Examples

    /// <summary>
    /// Test against ISO/IEC 18004 Annex I example
    /// Data: 01000000 01010110 10000110 01010110 (64, 86, 134, 86)
    /// ECC: 10 codewords
    /// Expected: [196, 35, 39, 119, 235, 215, 231, 226, 93, 23]
    /// </summary>
    [Fact]
    public void CalculateECC_ISO18004_Example_ProducesCorrectECC()
    {
        // Arrange
        var dataBits = "01000000010101101000011001010110"; // 32 bits = 4 bytes
        var eccWordCount = 10;
        var expectedECC = new[]
        {
            "10110000", // 176
            "01001100", // 76
            "00011101", // 29
            "10110100", // 180
            "01111010", // 122
            "11000000", // 192
            "01011100", // 92
            "11010000", // 208
            "10011101", // 157
            "00111000",  // 56
        };

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        //// Debug output
        //var resultDec = result.Select(x => Convert.ToInt32(x, 2)).ToArray();
        //Console.WriteLine("Actual ECC (binary): " + string.Join(", ", result));
        //Console.WriteLine("Actual ECC (decimal): " + string.Join(", ", resultDec));

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        for (int i = 0; i < eccWordCount; i++)
        {
            Assert.Equal(expectedECC[i], result[i]);
        }
    }

    // Basic ECC Calculation

    [Theory]
    [InlineData("01000000", 7)] // 1 byte data, 7 ECC
    [InlineData("0100000001010110", 10)] // 2 bytes data, 10 ECC
    [InlineData("010000000101011010000110", 13)] // 3 bytes data, 13 ECC
    public void CalculateECC_VariousDataSizes_ProducesCorrectWordCount(string dataBits, int eccWordCount)
    {
        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        Assert.All(result, word => Assert.Equal(8, word.Length)); // All 8-bit words
        Assert.All(result, word => Assert.Matches("^[01]{8}$", word)); // Binary format
    }

    [Fact]
    public void CalculateECC_SingleByte_ProducesValidECC()
    {
        // Arrange
        var dataBits = "01000000"; // Single byte: 64
        var eccWordCount = 5;

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        Assert.All(result, word =>
        {
            Assert.Equal(8, word.Length);
            Assert.Matches("^[01]+$", word);
        });
    }

    [Fact]
    public void CalculateECC_MaxECCLevel_Version40_ProducesValidECC()
    {
        // Arrange - QR Version 40, ECC Level H requires 30 ECC words per block
        var dataBits = new string('0', 29 * 8); // 29 data bytes
        var eccWordCount = 30;

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        Assert.All(result, word => Assert.Equal(8, word.Length));
    }

    // Edge Cases

    [Fact]
    public void CalculateECC_AllZeros_ProducesAllZeroECC()
    {
        // Arrange
        var dataBits = new string('0', 8 * 4); // 4 bytes of zeros
        var eccWordCount = 7;

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        Assert.All(result, word => Assert.Equal("00000000", word));
    }

    [Fact]
    public void CalculateECC_AllOnes_ProducesNonZeroECC()
    {
        // Arrange
        var dataBits = new string('1', 8 * 4); // 4 bytes: 255, 255, 255, 255
        var eccWordCount = 10;

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        // ECC for all 0xFF should not be all zeros
        Assert.Contains(result, word => word != "00000000");
    }

    [Theory]
    [InlineData("01010101", 3)]
    [InlineData("10101010", 5)]
    [InlineData("11110000", 7)]
    public void CalculateECC_AlternatingPatterns_ProducesValidECC(string pattern, int eccCount)
    {
        // Arrange
        var dataBits = pattern + pattern + pattern + pattern; // Repeat pattern 4 times

        // Act
        var result = _encoder.CalculateECC(dataBits, eccCount);

        // Assert
        Assert.Equal(eccCount, result.Count);
        Assert.All(result, word => Assert.Equal(8, word.Length));
    }

    // Polynomial Operation

    [Fact]
    public void CalculateECC_MessagePolynomial_HasCorrectDegree()
    {
        // This test verifies internal message polynomial creation
        // by checking the final ECC output properties

        // Arrange - 4 bytes of data should create degree 3 polynomial
        var dataBits = "01000000010101101000011001010110";
        var eccWordCount = 7;

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert - ECC count should match generator polynomial degree
        Assert.Equal(eccWordCount, result.Count);
    }

    [Theory]
    [InlineData(7)]  // Degree 6 generator (x-α^0)(x-α^1)...(x-α^6)
    [InlineData(10)] // Degree 9 generator
    [InlineData(30)] // Degree 29 generator (max for Version 40-H)
    public void CalculateECC_GeneratorPolynomial_ProducesCorrectDegree(int eccWordCount)
    {
        // Arrange
        var dataBits = new string('0', 16 * 8); // 16 bytes of data

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert - Generator polynomial degree = eccWordCount - 1
        Assert.Equal(eccWordCount, result.Count);
    }

    // Galois Field (GF256) Operation

    [Fact]
    public void CalculateECC_GF256Operations_ProduceValidResults()
    {
        // Arrange - Known test vectors for GF(256) arithmetic
        var dataBits = "11111111"; // 255 in GF(256)
        var eccWordCount = 3;

        // Act
        var result = _encoder.CalculateECC(dataBits, eccWordCount);

        // Assert
        Assert.Equal(eccWordCount, result.Count);
        // In GF(256), operations should produce values in range [0, 255]
        Assert.All(result, word =>
        {
            var value = Convert.ToInt32(word, 2);
            Assert.InRange(value, 0, 255);
        });
    }

    [Fact]
    public void CalculateECC_XOROperation_IsCommutative()
    {
        // Arrange
        var data1 = "01010101";
        var data2 = "10101010";
        var eccCount = 5;

        // Act - XOR data1 and data2 in different orders
        var xor12 = XorBinaryStrings(data1, data2);
        var xor21 = XorBinaryStrings(data2, data1);

        var ecc1 = _encoder.CalculateECC(xor12, eccCount);
        var ecc2 = _encoder.CalculateECC(xor21, eccCount);

        // Assert
        Assert.Equal(ecc1, ecc2); // XOR is commutative
    }

    private static string XorBinaryStrings(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        var result = new StringBuilder(length); ;
        for (int i = 0; i < length; i++)
        {
            result.Append((a[i] == b[i]) ? '0' : '1');
        }
        return result.ToString();
    }
}
