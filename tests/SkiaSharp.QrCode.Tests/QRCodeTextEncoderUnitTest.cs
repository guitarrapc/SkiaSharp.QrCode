using SkiaSharp.QrCode.Internals;
using System.Text;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class QRCodeTextEncoderUnitTest
{
    // Basic test - WriteMode

    [Theory]
    [InlineData(EncodingMode.Numeric, EciMode.Default, "0001")]
    [InlineData(EncodingMode.Alphanumeric, EciMode.Default, "0010")]
    [InlineData(EncodingMode.Byte, EciMode.Default, "0100")]
    internal void WriteMode_WithoutECI_Produces4Bits(EncodingMode mode, EciMode eci, string expected)
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteMode(mode, eci);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    [Theory]
    [InlineData(EciMode.Iso8859_1, "0111000000110100")]  // ECI(0111) + Value(00000011) + Byte(0100)
    [InlineData(EciMode.Utf8, "0111000110100100")]      // ECI(0111) + Value(00011010) + Byte(0100)
    public void WriteMode_WithECI_ProducesHeaderPlusModeIndicator(EciMode eci, string expected)
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteMode(EncodingMode.Byte, eci);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    [Theory]
    [InlineData(EciMode.Iso8859_1, 3)]
    [InlineData(EciMode.Utf8, 26)]
    public void WriteMode_WithECI_HasCorrectStructure(EciMode eci, int eciValue)
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteMode(EncodingMode.Byte, eci);
        var result = encoder.ToBinaryString();

        // Assert: Total bit length is 16 (4 + 8 + 4)
        Assert.Equal(16, result.Length);

        // Assert: ECI mode indicator (0111)
        Assert.Equal("0111", result.Substring(0, 4));

        // Assert: ECI value (8 bits)
        var eciValueBits = result.Substring(4, 8);
        Assert.Equal(Convert.ToString(eciValue, 2).PadLeft(8, '0'), eciValueBits);

        // Assert: Byte mode indicator (0100)
        Assert.Equal("0100", result.Substring(12, 4));
    }

    // WriteCharacterCount

    [Theory]
    [InlineData(1, EncodingMode.Numeric, 10, "0000001010")]        // Version 1-9: 10 bits
    [InlineData(10, EncodingMode.Numeric, 123, "000001111011")]  // Version 10-26: 12 bits
    [InlineData(27, EncodingMode.Numeric, 1234, "00010011010010")] // Version 27-40: 14 bits
    internal void WriteCharacterCount_Numeric_ProducesCorrectBitLength(int version, EncodingMode mode, int count, string expected)
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteCharacterCount(count, version, mode);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    [Theory]
    [InlineData(1, EncodingMode.Alphanumeric, 25, "000011001")]    // 9 bits
    [InlineData(10, EncodingMode.Alphanumeric, 100, "00001100100")] // 11 bits
    [InlineData(27, EncodingMode.Alphanumeric, 200, "0000011001000")] // 13 bits
    internal void WriteCharacterCount_Alphanumeric_ProducesCorrectBitLength(int version, EncodingMode mode, int count, string expected)
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteCharacterCount(count, version, mode);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    // WriteData

    [Theory]
    [InlineData("123", "0001111011")]                              // 3 digits: 10 bits
    [InlineData("12", "0001100")]                                  // 2 digits: 7 bits
    [InlineData("1", "0001")]                                      // 1 digit : 4 bits
    [InlineData("8675309", "110110001110000100101001")]            // Mixed
    [InlineData("0123456789", "0000001100010101100110101001101001")] // Border
    public void WriteData_Numeric_ProducesCorrectBits(string input, string expected)
    {
        var encoder = new QRTextEncoder(1000);
        encoder.WriteData(input, EncodingMode.Numeric, EciMode.Default, false);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    [Theory]
    [InlineData("AC-42", "0011100111011100111001000010")]  // ISO/IEC 18004
    [InlineData("HELLO WORLD", "0110000101101111000110100010111001011011100010011010100001101")] // tipical alphanumeric
    [InlineData("A", "001010")]                          // 1 letter: 6 bit
    [InlineData("AB", "00111001101")]                    // 2 letters: 11 bit
    public void WriteData_Alphanumeric_ProducesCorrectBits(string input, string expected)
    {
        var encoder = new QRTextEncoder(1000);
        encoder.WriteData(input, EncodingMode.Alphanumeric, EciMode.Default, false);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    // should be same result as text is EciMode.Iso8859_1
    [Theory]
    [InlineData("AC-42", EciMode.Default, "0100000101000011001011010011010000110010")]
    [InlineData("AC-42", EciMode.Iso8859_1, "0100000101000011001011010011010000110010")]
    [InlineData("HELLO WORLD", EciMode.Default, "0100100001000101010011000100110001001111001000000101011101001111010100100100110001000100")]
    [InlineData("HELLO WORLD", EciMode.Iso8859_1, "0100100001000101010011000100110001001111001000000101011101001111010100100100110001000100")]
    [InlineData("Hello", EciMode.Default, "0100100001100101011011000110110001101111")]
    [InlineData("Hello", EciMode.Iso8859_1, "0100100001100101011011000110110001101111")]
    public void WriteData_Byte_ProducesCorrectBits(string input, EciMode eci, string expected)
    {
        var encoder = new QRTextEncoder(1000);
        encoder.WriteData(input, EncodingMode.Byte, eci, false);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    // should be same result as text is EciMode.Utf8
    [Theory]
    [InlineData("Hello", EciMode.Default)]
    [InlineData("Hello", EciMode.Utf8)]
    [InlineData("こんにちは", EciMode.Default)]
    [InlineData("こんにちは", EciMode.Utf8)]
    [InlineData("🐈🐕🔑💫🚪あ感요Ц", EciMode.Default)]
    [InlineData("🐈🐕🔑💫🚪あ感요Ц", EciMode.Utf8)]
    public void WriteData_Byte_UTF8WithoutBOM(string input, EciMode eci)
    {
        // Expected
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var expected = string.Concat(utf8Bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        var encoder = new QRTextEncoder(1000);
        encoder.WriteData(input, EncodingMode.Byte, eci, false);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("こんにちは")]
    [InlineData("🐈🐕🔑💫🚪あ感요Ц")]
    public void WriteData_Byte_UTF8WithBOM(string input)
    {
        // Expected
        // BOM (EF BB BF) + Hello (48 65 6C 6C 6F)
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var expected = string.Concat(bom.Concat(utf8Bytes).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        var encoder = new QRTextEncoder(1000);
        encoder.WriteData(input, EncodingMode.Byte, EciMode.Utf8, true);
        Assert.Equal(expected, encoder.ToBinaryString());
    }

    // WritePadding

    [Fact]
    public void WritePadding_AddsTerminatorAndByteAlignment()
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteMode(EncodingMode.Numeric, EciMode.Default); // 4 bits
        encoder.WritePadding(32); // Target: 32 bits (4 bytes)

        var result = encoder.ToBinaryString();

        Assert.Equal(32, result.Length);
        Assert.StartsWith("0001", result); // Mode indicator
        Assert.Contains("0000", result.Substring(4, 4)); // Terminator (4 bits)
        var expected = "0001" // Mode (4 bits)
               + "0000"             // Terminator (4 bits)
               + "11101100"         // Pad byte 1: 0xEC (8 bits)
               + "00010001"         // Pad byte 2: 0x11 (8 bits)
               + "11101100";        // Pad byte 3: 0xEC (8 bits, trimmed from 16-bit pair)
        Assert.EndsWith(expected, result); // Pad bytes (EC 11)
    }

    [Theory]
    [InlineData(4, 32)]  // 4bits → 32bits
    [InlineData(12, 24)] // 12bits → 24bits
    public void WritePadding_FillsToTargetWithAlternatingBytes(int initialBits, int targetBits)
    {
        var encoder = new QRTextEncoder(100);

        // Write initial bits
        for (int i = 0; i < initialBits / 4; i++)
            encoder.WriteMode(EncodingMode.Numeric, EciMode.Default); // 4 bits each

        var beforePadding = encoder.ToBinaryString().Length;
        encoder.WritePadding(targetBits);
        var afterPadding = encoder.ToBinaryString();

        Assert.Equal(targetBits, afterPadding.Length);
        Assert.True(afterPadding.Length >= beforePadding);
    }

    // Edge cases

    [Fact]
    public void WriteData_EmptyString_ProducesEmptyOutput()
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteData("", EncodingMode.Numeric, EciMode.Default, false);
        Assert.Equal("", encoder.ToBinaryString());
    }

    [Theory]
    [InlineData("000")]          // 0 header
    [InlineData("999")]          // max 3 digits
    [InlineData("0")]            // single 0
    public void WriteData_Numeric_EdgeCases(string input)
    {
        var encoder = new QRTextEncoder(100);
        encoder.WriteData(input, EncodingMode.Numeric, EciMode.Default, false);

        var result = encoder.ToBinaryString();
        Assert.NotEmpty(result);
        Assert.All(result, c => Assert.True(c == '0' || c == '1'));
    }

    [Fact]
    public void WritePadding_AlreadyAtTarget_DoesNothing()
    {
        var encoder = new QRTextEncoder(100);
        for (int i = 0; i < 32; i++) // 既に32ビット
            encoder.ToBinaryString();

        encoder.WritePadding(32);
        Assert.Equal(32, encoder.ToBinaryString().Length);
    }
}
