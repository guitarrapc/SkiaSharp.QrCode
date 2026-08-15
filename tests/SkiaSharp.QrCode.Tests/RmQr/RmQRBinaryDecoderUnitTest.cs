using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// rMQR bit-stream decoding (ISO/IEC 23941 7.4): 3-bit mode indicators, per-version
/// count widths, terminator, ECI segments (parsed even though the encoder never
/// emits them), Kanji reported as unsupported, malformed streams rejected.
/// </summary>
public class RmQRBinaryDecoderUnitTest
{
    private static (QRCodeDecodeStatus Status, string Text) Decode(byte[] data, RmQRVersion version)
    {
        var destination = new char[RmQRMatrixDecoder.GetMaxCharCount(version)];
        var status = RmQRBinaryDecoder.DecodeBitStream(data, data.Length * 8, version, destination, out var written);
        return (status, new string(destination, 0, written));
    }

    private static byte[] Encode(string text, RmQRVersion version, RmQREccLevel ecc)
    {
        var analysis = TextAnalyzer.Analyze(text.AsSpan(), EciMode.Default);
        var data = new byte[RmQRConstants.GetDataCodewordCount(version, ecc)];
        RmQRBinaryEncoder.EncodeDataCodewords(text.AsSpan(), version, ecc, in analysis, data);
        return data;
    }

    /// <summary>Bit-string builder for hand-made streams (MSB first, zero-padded to the codeword count).</summary>
    private static byte[] Bits(string bits, int codewords)
    {
        var clean = bits.Replace(" ", "");
        var result = new byte[codewords];
        for (var i = 0; i < clean.Length; i++)
            if (clean[i] == '1')
                result[i >> 3] |= (byte)(0x80 >> (i & 7));
        return result;
    }

    [Test]
    public async Task Decode_OracleGolden_R7x43M_Numeric1()
    {
        var (status, text) = Decode([0x22, 0x20, 0xEC, 0x11, 0xEC, 0x11], RmQRVersion.R7x43);
        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("1");
    }

    [Test]
    [Arguments("012345678901", RmQRVersion.R7x43, RmQREccLevel.M)]
    [Arguments("ABCDEF $%*+-./:", RmQRVersion.R7x59, RmQREccLevel.M)]
    [Arguments("hello, rMQR!", RmQRVersion.R11x59, RmQREccLevel.H)]
    [Arguments("naïve café", RmQRVersion.R11x59, RmQREccLevel.M)]
    [Arguments("こんにちは世界", RmQRVersion.R13x59, RmQREccLevel.M)]
    [Arguments("", RmQRVersion.R7x43, RmQREccLevel.H)]
    [Arguments("😀🎉", RmQRVersion.R9x77, RmQREccLevel.M)]
    public async Task Decode_EncoderRoundTrip(string text, RmQRVersion version, RmQREccLevel ecc)
    {
        var (status, decoded) = Decode(Encode(text, version, ecc), version);
        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(decoded).IsEqualTo(text);
    }

    [Test]
    public async Task Decode_ExactCapacity_NoTerminator_Succeeds()
    {
        // R7x43-H byte "ab": 3 + 3 + 16 = 22 bits + 2-bit terminator, no padding.
        var (status, decoded) = Decode(Encode("ab", RmQRVersion.R7x43, RmQREccLevel.H), RmQRVersion.R7x43);
        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(decoded).IsEqualTo("ab");
        // 12 digits at R7x43-M: 47 of 48 bits used, 1-bit terminator.
        var (status2, decoded2) = Decode(Encode("999999999999", RmQRVersion.R7x43, RmQREccLevel.M), RmQRVersion.R7x43);
        await Assert.That(status2).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(decoded2).IsEqualTo("999999999999");
    }

    [Test]
    public async Task Decode_EciUtf8Segment_IsParsed()
    {
        // 111 (ECI) + 00011010 (26 = UTF-8) + 011 (byte) + count 3 bits = 2 + "é" as UTF-8 C3 A9 + terminator, R7x43-M (6 codewords).
        var data = Bits("111 00011010 011 010 11000011 10101001 000", 6);
        var (status, text) = Decode(data, RmQRVersion.R7x43);
        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("é");
    }

    [Test]
    public async Task Decode_EciIso88591Segment_IsParsed()
    {
        // ECI 3 = ISO-8859-1, then byte "é" = E9.
        var data = Bits("111 00000011 011 001 11101001 000", 6);
        var (status, text) = Decode(data, RmQRVersion.R7x43);
        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("é");
    }

    [Test]
    public async Task Decode_UnsupportedEci_ReportsUnsupportedContent()
    {
        var data = Bits("111 00010100 011 001 01000001 000", 6); // ECI 20 (Shift JIS): not mapped
        await Assert.That(Decode(data, RmQRVersion.R7x43).Status).IsEqualTo(QRCodeDecodeStatus.UnsupportedContent);
    }

    [Test]
    public async Task Decode_Kanji_ReportsUnsupportedContent()
    {
        var data = Bits("100 00001 1000000000000 000", 6);
        await Assert.That(Decode(data, RmQRVersion.R7x43).Status).IsEqualTo(QRCodeDecodeStatus.UnsupportedContent);
    }

    [Test]
    public async Task Decode_ReservedModes_AreInvalidBitstream()
    {
        await Assert.That(Decode(Bits("101 0000", 6), RmQRVersion.R7x43).Status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
        await Assert.That(Decode(Bits("110 0000", 6), RmQRVersion.R7x43).Status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task Decode_TruncatedCountOrPayload_IsInvalidBitstream()
    {
        // Numeric mode indicator at bit 45 of 48 (after a byte segment and four empty numeric
        // segments that consume bits without terminating): its 4-bit count does not fit.
        var truncatedCount = Bits("011 001 01000001 0010000 0010000 0010000 0010000 001", 6);
        await Assert.That(Decode(truncatedCount, RmQRVersion.R7x43).Status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
        // Byte segment claiming more bytes than the stream holds.
        var truncatedPayload = Bits("011 111 01000001 01000010", 6); // claims 7 bytes (56 bits) with 42 bits left
        await Assert.That(Decode(truncatedPayload, RmQRVersion.R7x43).Status).IsEqualTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task Decode_TerminatorAndPadding_EndTheStream()
    {
        // 011 (byte) count 1 'A' then 000 terminator, then arbitrary pad garbage must be ignored.
        var data = Bits("011 001 01000001 000 11111111 10101010 11001100", 6);
        var (status, text) = Decode(data, RmQRVersion.R7x43);
        await Assert.That(status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEqualTo("A");
        // Empty-count segments are skipped, not terminators (only 000 ends the stream).
        var empty = Bits("001 0000 011 001 01000010 000", 6);
        await Assert.That(Decode(empty, RmQRVersion.R7x43)).IsEqualTo((QRCodeDecodeStatus.Success, "B"));
    }
}
