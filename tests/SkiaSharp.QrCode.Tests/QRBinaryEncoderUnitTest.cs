using SkiaSharp.QrCode.Internals;
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
    [InlineData("AC-42", "0011100111011100111001000010")]  // ISO/IEC 18004
    [InlineData("HELLO WORLD", "0110000101101111000110100010111001011011100010011010100001101")] // tipical alphanumeric
    [InlineData("A", "001010")]                          // 1 letter: 6 bit
    [InlineData("AB", "00111001101")]                    // 2 letters: 11 bit
    public void WriteData_Alphanumeric_ProducesCorrectBits(string input, string expected)
    {
        Span<byte> buffer = stackalloc byte[8];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteAlphanumericData(Encoding.ASCII.GetBytes(input));

        var actual = ToBinaryString(encoder.GetEncodedData());
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
        Span<byte> buffer = stackalloc byte[12];

        // Expected
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var expected = string.Concat(utf8Bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        var encoder = new QRBinaryEncoder(buffer);
        encoder.WriteByteData(Encoding.UTF8.GetBytes(input));

        var actual = ToBinaryString(encoder.GetEncodedData());
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("こんにちは")]
    [InlineData("🐈🐕🔑💫🚪あ感요Ц")]
    public void WriteData_Byte_UTF8WithBOM(string input)
    {
        Span<byte> buffer = stackalloc byte[12];

        // Expected
        // BOM (EF BB BF) + Hello (48 65 6C 6C 6F)
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var expected = string.Concat(bom.Concat(utf8Bytes).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        var encoder = new QRBinaryEncoder(buffer);
        encoder.WriteByteData(Encoding.UTF8.GetBytes(input));

        var actual = ToBinaryString(encoder.GetEncodedData());
        Assert.Equal(expected, actual);
    }

    // Encoding

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

    // WriteNumericData

    [Theory]
    [InlineData("1", "0001")] // 1 = 0001 (4 bits)
    [InlineData("12", "0001100")] // 12 = 0001100 (7 bits)
    [InlineData("123", "00_0111_1011")] // 123 = 0001111011 (10 bits)
    [InlineData("12345", "00011110_11010110_10000000")] // 123(10) + 45(10)
    public void WriteNumericData_ProducesCorrectBits(string digits, string expectedBits)
    {
        Span<byte> buffer = stackalloc byte[10];
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteNumericData(Encoding.ASCII.GetBytes(digits));

        var actual = ToBinaryString(encoder.GetEncodedData());
        var expected = expectedBits.Replace("_", "");
        Assert.StartsWith(expected, actual);
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
        encoder.WritePadding(16); // Taget 2 bytes

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
        textEncoder.WriteCharacterCount(input.Length, version, mode);
        textEncoder.WriteData(input, mode, EciMode.Default, false);
        textEncoder.WritePadding(targetBits);
        var textBits = textEncoder.ToBinaryString();

        // Act - Binary Encoder
        Span<byte> buffer = stackalloc byte[(targetBits + 7) / 8];
        var binaryEncoder = new QRBinaryEncoder(buffer);
        binaryEncoder.WriteMode(mode, EciMode.Default);
        int countBitLength = mode.GetCountIndicatorLength(version);
        binaryEncoder.WriteCharacterCount(input.Length, countBitLength);
        binaryEncoder.WriteNumericData(Encoding.ASCII.GetBytes(input));
        binaryEncoder.WritePadding(targetBits);
        var binaryBits = ToBinaryString(binaryEncoder.GetEncodedData());

        // Assert
        Assert.Equal(textBits, binaryBits);
    }

    // helpers

    private static string ToBinaryString(ReadOnlySpan<byte> data)
    {
        return string.Concat(data.ToArray().Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
    }
}
