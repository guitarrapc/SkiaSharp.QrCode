using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.MicroQR;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Bitstream decoding: corrected Micro QR data codewords back to text. Golden
/// vectors are independent of the encoder (hand-derived per ISO/IEC 18004 rules);
/// encoder round-trips cover every version/ECC/mode/length combination; negative
/// cases cover malformed streams (invalid modes, out-of-range group values,
/// truncated segments) that must not decode.
/// </summary>
public class MicroQRBinaryDecoderUnitTest
{
    private static (QRCodeDecodeStatus status, string text) Decode(byte[] data, int dataBitCount, MicroQRVersion version)
    {
        var destination = new char[64];
        var status = MicroQRBinaryDecoder.DecodeBitStream(data, dataBitCount, version, destination, out var charsWritten);
        return (status, new string(destination, 0, charsWritten));
    }

    [Test]
    [Arguments("99999", new byte[] { 0xBF, 0x3E, 0x30 })] // exactly fills 20 bits, no terminator
    [Arguments("9999", new byte[] { 0x9F, 0x3C, 0x80 })]  // terminator exactly fills capacity
    [Arguments("123", new byte[] { 0x63, 0xD8, 0x00 })]   // 011 + 0001111011 + 3-bit terminator + zeros (final 4-bit pad 0000)
    public async Task DecodeBitStream_M1GoldenVectors(string expected, byte[] codewords)
    {
        var (status, text) = Decode(codewords, dataBitCount: 20, MicroQRVersion.M1);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    public async Task DecodeBitStream_M2L_IsoExample01234567()
    {
        // ISO/IEC 18004 Micro QR encoding example: "01234567" in M2-L.
        var (status, text) = Decode([0x40, 0x18, 0xAC, 0xC3, 0x00], dataBitCount: 40, MicroQRVersion.M2);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("01234567");
    }

    public static IEnumerable<(string text, MicroQRVersion version, MicroQREccLevel ecc)> RoundTripCases()
    {
        // Every version/ECC combination, every supported mode, boundary and
        // mid-fill lengths (padding paths: full capacity, terminator + pads).
        yield return ("1", MicroQRVersion.M1, MicroQREccLevel.ErrorDetectionOnly);
        yield return ("12345", MicroQRVersion.M1, MicroQREccLevel.ErrorDetectionOnly);
        yield return ("0123456789", MicroQRVersion.M2, MicroQREccLevel.L);
        yield return ("12345678", MicroQRVersion.M2, MicroQREccLevel.M);
        yield return ("AC-42", MicroQRVersion.M2, MicroQREccLevel.L);
        yield return ("HELLO", MicroQRVersion.M2, MicroQREccLevel.M);
        yield return ("12345678901234567890123", MicroQRVersion.M3, MicroQREccLevel.L);
        yield return ("HELLO WORLD 14", MicroQRVersion.M3, MicroQREccLevel.L);
        yield return ("byte hi", MicroQRVersion.M3, MicroQREccLevel.M);
        yield return ("x", MicroQRVersion.M3, MicroQREccLevel.L);
        yield return ("HELLO WORLD PLUS 21ST", MicroQRVersion.M4, MicroQREccLevel.L);
        yield return ("bytes m4 mode", MicroQRVersion.M4, MicroQREccLevel.M);
        yield return ("bytes!!!!", MicroQRVersion.M4, MicroQREccLevel.Q);
        yield return ("99999999999999999999999999999999999", MicroQRVersion.M4, MicroQREccLevel.L); // 35 digits = M4-L numeric capacity
        yield return ("fifteen bytes!!", MicroQRVersion.M4, MicroQREccLevel.L); // 15 bytes = M4-L byte capacity
        yield return ("Café au lait", MicroQRVersion.M4, MicroQREccLevel.L); // Latin-1 high byte
        yield return ("こんにちは", MicroQRVersion.M4, MicroQREccLevel.L); // UTF-8, 15 bytes
    }

    [Test]
    [MethodDataSource(nameof(RoundTripCases))]
    public async Task DecodeBitStream_EncoderRoundTrip(string text, MicroQRVersion version, MicroQREccLevel ecc)
    {
        var analysis = TextAnalyzer.Analyze(text.AsSpan(), EciMode.Default);
        var codewords = new byte[16];
        MicroQRBinaryEncoder.EncodeDataCodewords(text.AsSpan(), version, ecc, analysis.EncodingMode, codewords);

        var (status, decoded) = Decode(codewords, MicroQRConstants.GetDataBitCapacity(version, ecc), version);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(decoded).IsEqualTo(text);
    }

    [Test]
    public async Task DecodeBitStream_KanjiModeIndicator_ReportsUnsupportedContent()
    {
        // M4: 3-bit mode indicator 011 = Kanji, then a plausible count field.
        var (status, _) = Decode([0b011_00001, 0x00, 0x00], dataBitCount: 80, MicroQRVersion.M4);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.UnsupportedContent);
    }

    [Test]
    [Arguments(new byte[] { 0b100_00000, 0x00 })] // mode 100 (undefined)
    [Arguments(new byte[] { 0b111_00000, 0x00 })] // mode 111 (undefined)
    public async Task DecodeBitStream_UndefinedM4ModeIndicator_ReportsInvalidBitstream(byte[] codewords)
    {
        var (status, _) = Decode(codewords, dataBitCount: 80, MicroQRVersion.M4);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task DecodeBitStream_NumericGroupAboveRange_ReportsInvalidBitstream()
    {
        // M1: count = 3 (bits 011), then a 10-bit group of all ones (1023 > 999).
        // Stream: 011 1111111111 -> 0111 1111 1111 1000 -> 0x7F 0xF8
        var (status, _) = Decode([0x7F, 0xF8, 0x00], dataBitCount: 20, MicroQRVersion.M1);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task DecodeBitStream_AlphanumericPairAboveRange_ReportsInvalidBitstream()
    {
        // M2: mode 1 (alnum), count 2 (bits 010), 11-bit pair of all ones (2047 >= 45*45=2025).
        // Stream: 1 010 11111111111 -> 1010 1111 1111 1110 -> 0xAF 0xFE
        var (status, _) = Decode([0xAF, 0xFE, 0x00, 0x00, 0x00], dataBitCount: 40, MicroQRVersion.M2);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task DecodeBitStream_ByteSegmentOverrunsCapacity_ReportsInvalidBitstream()
    {
        // M3: mode 10 (byte), count 9 = 72 data bits needed, but M3-M capacity
        // after the 6 header bits leaves only 62.
        // Stream: 10 1001 ... -> 0b10_1001_00
        var (status, _) = Decode([0b10_1001_00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], dataBitCount: 68, MicroQRVersion.M3);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task DecodeBitStream_ZeroCountNonNumericSegment_DecodesAsEmpty()
    {
        // M2: mode 1 (alnum), count 0 — an empty segment is not a terminator
        // (the terminator is numeric-mode-with-zero-count); the stream then ends.
        var (status, text) = Decode([0b1_000_0000, 0x00, 0x00, 0x00, 0x00], dataBitCount: 40, MicroQRVersion.M2);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("");
    }

    [Test]
    public async Task DecodeBitStream_DestinationTooSmall_ReportsStatus()
    {
        var codewords = new byte[16];
        MicroQRBinaryEncoder.EncodeDataCodewords("0123456789".AsSpan(), MicroQRVersion.M2, MicroQREccLevel.L, EncodingMode.Numeric, codewords);

        var destination = new char[4];
        var status = MicroQRBinaryDecoder.DecodeBitStream(codewords.AsSpan(0, 5), 40, MicroQRVersion.M2, destination, out _);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.DestinationTooSmall);
    }

    [Test]
    public async Task DecodeBitStream_HalfCodewordBitsBeyondCapacity_AreNeverRead()
    {
        // M3-L: 84 data bits = 10.5 codewords; the low nibble of the final
        // codeword is outside the data capacity. Fill it with garbage — the
        // decoder must ignore it.
        var codewords = new byte[16];
        var analysis = TextAnalyzer.Analyze("ABC".AsSpan(), EciMode.Default);
        MicroQRBinaryEncoder.EncodeDataCodewords("ABC".AsSpan(), MicroQRVersion.M3, MicroQREccLevel.L, analysis.EncodingMode, codewords);
        codewords[10] |= 0x0F; // garbage in the forced-zero low nibble

        var (status, text) = Decode(codewords, dataBitCount: 84, MicroQRVersion.M3);

        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("ABC");
    }
}
