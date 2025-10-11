using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryEncoders;
using System.Text;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class QRBinaryEncoderUnitTest
{
    // Basic test - WriteMode

    [Theory]
    [InlineData(EncodingMode.Numeric, EciMode.Default, 0b_00010000)]
    [InlineData(EncodingMode.Alphanumeric, EciMode.Default, 0b_00100000)]
    [InlineData(EncodingMode.Byte, EciMode.Default, 0b_01000000)]
    internal void WriteMode_Numeric_Produces4Bits(EncodingMode encoding, EciMode eci, int expected)
    {
        Span<byte> buffer = stackalloc byte[1];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteMode(encoding, eci);

        Assert.Equal(expected, buffer[0]);
    }

    [Theory]
    [InlineData(EciMode.Iso8859_1, 0b_0111_0000, 0b_0011_0100)]  // ECI(0111) + Value(00000011) + Byte(0100)
    [InlineData(EciMode.Utf8, 0b_0111_0001, 0b_1010_0100)]      // ECI(0111) + Value(00011010) + Byte(0100)
    internal void WriteMode_WithECI_Produces16Bits(EciMode eci, int expected0, int expected1)
    {
        Span<byte> buffer = stackalloc byte[2];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteMode(EncodingMode.Byte, eci);

        Assert.Equal(expected0, buffer[0]); // ECI mode + part of UTF-8
        Assert.Equal(expected1, buffer[1]); // rest of UTF-8 + Byte mode
    }

    [Theory]
    [InlineData(EciMode.Iso8859_1, 3)]
    [InlineData(EciMode.Utf8, 26)]
    public void WriteMode_WithECI_HasCorrectStructure(EciMode eci, int eciValue)
    {
        Span<byte> buffer = stackalloc byte[2];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteMode(EncodingMode.Byte, eci);
        var result = ToBinaryString(buffer, encoder.BitPosition);

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
    [InlineData(10, 10, 0b_0000_0010_1000_0000)] // 10 in 10 bits
    [InlineData(255, 16, 0b_0000_0000_1111_1111)] // 255 in 16 bits (first 2 bytes)
    public void WriteCharacterCount_ProducesCorrectBits(int count, int bitsLength, int expectedFirstByte)
    {
        Span<byte> buffer = stackalloc byte[3];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteCharacterCount(count, bitsLength);

        var result = buffer[0] << 8 | buffer[1];
        Assert.Equal(expectedFirstByte, result);
    }

    [Theory]
    [InlineData(10, 10, "0000001010")]        // Version 1-9 Numeric: 10 bits
    [InlineData(123, 12, "000001111011")]     // Version 10-26 Numeric: 12 bits
    [InlineData(1234, 14, "00010011010010")]  // Version 27-40 Numeric: 14 bits
    [InlineData(25, 9, "000011001")]          // Version 1-9 Alphanumeric: 9 bits
    [InlineData(100, 11, "00001100100")]      // Version 10-26 Alphanumeric: 11 bits
    public void WriteCharacterCount_VariousBitLengths_ProducesCorrectBits(int count, int bitsLength, string expectedBits)
    {
        Span<byte> buffer = stackalloc byte[3];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteCharacterCount(count, bitsLength);

        var actual = ToBinaryString(encoder.GetEncodedData(), encoder.BitPosition);
        Assert.Equal(expectedBits, actual);
    }

    // WriteData

    [Theory]
    [InlineData("1", "0001")] // 1 = 0001 (4 bits)
    [InlineData("12", "000_1100")] // 12 = 0001100 (7 bits)
    [InlineData("123", "00_0111_1011")] // 123 = 0001111011 (10 bits)
    [InlineData("12345", "00011110_11010110_1")] // 123(10) + 45(10)
    [InlineData("8675309", "110110001110000100101001")]            // Mixed
    [InlineData("0123456789", "0000001100010101100110101001101001")] // Border
    public void WriteNumericData_ProducesCorrectBits(string input, string expectedBits)
    {
        Span<byte> buffer = stackalloc byte[10];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteData(input, EncodingMode.Numeric, EciMode.Default, false);

        var actual = ToBinaryString(encoder.GetEncodedData(), encoder.BitPosition);
        var expected = expectedBits.Replace("_", "");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("AC-42", EciMode.Default, "0011100111011100111001000010")]  // ISO/IEC 18004
    [InlineData("AC-42", EciMode.Iso8859_1, "0011100111011100111001000010")]  // ISO/IEC 18004
    [InlineData("HELLO WORLD", EciMode.Default, "0110000101101111000110100010111001011011100010011010100001101")] // typical alphanumeric
    [InlineData("HELLO WORLD", EciMode.Iso8859_1, "0110000101101111000110100010111001011011100010011010100001101")] // typical alphanumeric
    [InlineData("A", EciMode.Default, "001010")]                          // 1 letter: 6 bit
    [InlineData("A", EciMode.Iso8859_1, "001010")]                          // 1 letter: 6 bit
    [InlineData("AB", EciMode.Default, "00111001101")]                    // 2 letters: 11 bit
    [InlineData("AB", EciMode.Iso8859_1, "00111001101")]                    // 2 letters: 11 bit
    public void WriteData_Alphanumeric_ProducesCorrectBits(string input, EciMode eci, string expectedBits)
    {
        Span<byte> buffer = stackalloc byte[8];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteData(input, EncodingMode.Alphanumeric, eci, false);

        var actual = ToBinaryString(encoder.GetEncodedData(), encoder.BitPosition);
        var expected = expectedBits.Replace("_", "");
        Assert.Equal(expected, actual);
    }

    // should be same result as text is EciMode.Iso8859_1
    [Theory]
    [InlineData("AC-42", EciMode.Default, "0100000101000011001011010011010000110010")]
    [InlineData("AC-42", EciMode.Iso8859_1, "0100000101000011001011010011010000110010")]
    [InlineData("HELLO WORLD", EciMode.Default, "0100100001000101010011000100110001001111001000000101011101001111010100100100110001000100")]
    [InlineData("HELLO WORLD", EciMode.Iso8859_1, "0100100001000101010011000100110001001111001000000101011101001111010100100100110001000100")]
    [InlineData("Hello", EciMode.Default, "0100100001100101011011000110110001101111")]
    [InlineData("Hello", EciMode.Iso8859_1, "0100100001100101011011000110110001101111")]
    public void WriteData_Byte_ProducesCorrectBits(string input, EciMode eci, string expectedBits)
    {
        Span<byte> buffer = stackalloc byte[12];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteData(input, EncodingMode.Byte, eci, false);

        var actual = ToBinaryString(encoder.GetEncodedData(), encoder.BitPosition);
        var expected = expectedBits.Replace("_", "");
        Assert.Equal(expected, actual);
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
        Span<byte> buffer = stackalloc byte[32];

        // Expected
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var expected = string.Concat(utf8Bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        var encoder = new QRBinaryEncoder(buffer);
        encoder.WriteData(input, EncodingMode.Byte, eci, false);

        var actual = ToBinaryString(encoder.GetEncodedData(), encoder.BitPosition);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("こんにちは")]
    [InlineData("🐈🐕🔑💫🚪あ感요Ц")]
    public void WriteData_Byte_UTF8WithBOM(string input)
    {
        Span<byte> buffer = stackalloc byte[34];

        // Expected
        // BOM (EF BB BF) + Hello (48 65 6C 6C 6F)
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var expected = string.Concat(bom.Concat(utf8Bytes).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        var encoder = new QRBinaryEncoder(buffer);
        encoder.WriteData(input, EncodingMode.Byte, EciMode.Utf8, true);

        var actual = ToBinaryString(encoder.GetEncodedData(), encoder.BitPosition);
        Assert.Equal(expected, actual);
    }

    // WritePadding

    [Fact]
    public void WritePadding_FillsToTargetWithAlternatingBytes()
    {
        Span<byte> buffer = stackalloc byte[4];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteMode(EncodingMode.Numeric, EciMode.Default); // 4 bits
        encoder.WritePadding(32); // Target: 32 bits (4 bytes)

        // Expected: 0001_0000           | 1110_1100 | 0001_0001 | 1110_1100
        //           Mode(4) + Term(4)   | Pad1(8)   | Pad2(8)   | Pad3(8)
        Assert.Equal(0b_0001_0000, buffer[0]); // Mode indicator + Terminator
        Assert.Equal(0b_1110_1100, buffer[1]); // Pad byte 1: 0xEC
        Assert.Equal(0b_0001_0001, buffer[2]); // Pad byte 2: 0x11
        Assert.Equal(0b_1110_1100, buffer[3]); // Pad byte 3: 0xEC
    }

    [Fact]
    public void WritePadding_WithByteAlignment_AddsZeros()
    {
        Span<byte> buffer = stackalloc byte[2];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteMode(EncodingMode.Numeric, EciMode.Default); // 4 bits
        encoder.WriteCharacterCount(1, 10); // 10 bits
        encoder.WritePadding(16); // target 2 bytes

        // Expected: 0001_0000_0000_01   | 00_1110_1100
        //           Mode(4) + Count(10) | Align(2) + Pad(8 bits but only 6 bits space)
        Assert.Equal(0b_0001_0000, buffer[0]); // Mode indicator + part of count
        Assert.Equal(0b_0000_0100, buffer[1]); // rest
    }

    // Works as same as QRTextEncoder

    [Theory]
    [InlineData("123", EncodingMode.Numeric, 1)] // Version 1
    [InlineData("12345678", EncodingMode.Numeric, 1)]
    [InlineData("0123456789", EncodingMode.Numeric, 5)]
    internal void QRBinaryEncoder_MatchesQRTextEncoder(string input, EncodingMode mode, int version)
    {
        // Arrange
        var eccInfo = QRCodeConstants.CapacityECCTable
            .First(x => x.Version == version && x.ErrorCorrectionLevel == ECCLevel.H);
        int targetBits = eccInfo.TotalDataCodewords * 8;

        // Act - Text Encoder
        var textEncoder = new Internals.TextEncoders.QRTextEncoder(targetBits);
        textEncoder.WriteMode(mode, EciMode.Default);
        textEncoder.WriteCharacterCount(input.Length, mode.GetCountIndicatorLength(version));
        textEncoder.WriteData(input, mode, EciMode.Default, false);
        textEncoder.WritePadding(targetBits);
        var textBits = textEncoder.ToBinaryString();

        // Act - Binary Encoder
        Span<byte> buffer = stackalloc byte[(targetBits + 7) / 8];
        var binaryEncoder = new QRBinaryEncoder(buffer);
        binaryEncoder.WriteMode(mode, EciMode.Default);
        int countBitLength = mode.GetCountIndicatorLength(version);
        binaryEncoder.WriteCharacterCount(input.Length, countBitLength);
        binaryEncoder.WriteData(input, mode, EciMode.Default, false);
        binaryEncoder.WritePadding(targetBits);
        var binaryBits = ToBinaryString(binaryEncoder.GetEncodedData(), binaryEncoder.BitPosition);

        // Assert
        Assert.Equal(textBits, binaryBits);
    }

    // Edge cases

    [Fact]
    public void WriteData_EmptyString_ProducesEmptyOutput()
    {
        Span<byte> buffer = stackalloc byte[10];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteData("", EncodingMode.Numeric, EciMode.Default, false);
        var result = encoder.GetEncodedData();

        Assert.Equal(0, result.Length);
    }

    [Theory]
    [InlineData("000")] // Leading zeros
    [InlineData("999")] // Max 3 digits
    [InlineData("0")]   // Single zero
    public void WriteData_Numeric_EdgeCases(string input)
    {
        Span<byte> buffer = stackalloc byte[10];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteData(input, EncodingMode.Numeric, EciMode.Default, false);
        var result = encoder.GetEncodedData();

        Assert.True(result.Length > 0);
        // Verify all bytes contain valid bit patterns
    }

    [Fact]
    public void WritePadding_AlreadyAtTarget_DoesNotModify()
    {
        Span<byte> buffer = stackalloc byte[5];
        var encoder = new QRBinaryEncoder(buffer);

        // Fill exactly 32 bits
        encoder.WriteMode(EncodingMode.Numeric, EciMode.Default); // 4 bits
        encoder.WriteCharacterCount(1234, 14); // 14 bits
        encoder.WriteData("12345", EncodingMode.Numeric, EciMode.Default, false); // 14 bits (3+2 digits)
                                                                                  // Total: 32 bits

        var beforePadding = encoder.GetEncodedData().ToArray();
        encoder.WritePadding(32);
        var afterPadding = encoder.GetEncodedData().ToArray();

        Assert.Equal(beforePadding, afterPadding);
    }

    // helpers

    private static string ToBinaryString(ReadOnlySpan<byte> data, int bitCount)
    {
        var binaryString = string.Concat(data.ToArray().Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        // Trim to actual bit count
        return binaryString.Substring(0, bitCount);
    }
}
