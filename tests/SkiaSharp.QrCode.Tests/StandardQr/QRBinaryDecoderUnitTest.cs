using SkiaSharp.QrCode.Internals.StandardQr;
using SkiaSharp.QrCode.Internals.BinaryDecoders;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Branch-level tests for the bitstream decoder using handcrafted segment streams
/// (version 1 count-indicator widths). Covers the decision branches that
/// encoder-round-trip tests cannot reach: multi-segment streams, unsupported modes,
/// invalid segment values, ECI designator forms, and truncation. The decoder parses
/// untrusted input, so negative cases outnumber positive ones.
/// </summary>
public class QRBinaryDecoderUnitTest
{
    private const int Version = 1;

    // Mode indicator constants (ISO/IEC 18004 Table 2)
    private const int ModeNumeric = 0b0001;
    private const int ModeAlphanumeric = 0b0010;
    private const int ModeStructuredAppend = 0b0011;
    private const int ModeByte = 0b0100;
    private const int ModeFnc1First = 0b0101;
    private const int ModeEci = 0b0111;
    private const int ModeKanji = 0b1000;
    private const int ModeFnc1Second = 0b1001;
    private const int ModeTerminator = 0b0000;

    // Positive cases

    [Test]
    public async Task MultiSegment_NumericAlphanumericByte_ConcatenatesInOrder()
    {
        // "12" (numeric) + "A" (alphanumeric) + "!" (byte) + terminator
        var data = Build(
            (ModeNumeric, 4), (2, 10), (12, 7),
            (ModeAlphanumeric, 4), (1, 9), (10, 6),
            (ModeByte, 4), (1, 8), ('!', 8),
            (ModeTerminator, 4));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("12A!");
    }

    [Test]
    public async Task EciUtf8_ByteSegment_DecodesUtf8()
    {
        // ECI 26 (UTF-8) + byte segment with a 3-byte UTF-8 char (U+3042 あ)
        var data = Build(
            (ModeEci, 4), (26, 8),
            (ModeByte, 4), (3, 8), (0xE3, 8), (0x81, 8), (0x82, 8),
            (ModeTerminator, 4));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("あ");
    }

    [Test]
    public async Task EciUtf8_WithBom_BomIsStripped()
    {
        var data = Build(
            (ModeEci, 4), (26, 8),
            (ModeByte, 4), (4, 8), (0xEF, 8), (0xBB, 8), (0xBF, 8), ('A', 8),
            (ModeTerminator, 4));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("A");
    }

    [Test]
    public async Task EciIso8859_1_BomLikeBytes_AreLatin1Text_NotBom()
    {
        // ECI 3 explicitly declares ISO-8859-1: EF BB BF is the legitimate Latin-1
        // text "ï»¿", not a BOM, an explicit charset declaration must win over the
        // BOM heuristic.
        var data = Build(
            (ModeEci, 4), (3, 8),
            (ModeByte, 4), (4, 8), (0xEF, 8), (0xBB, 8), (0xBF, 8), ('A', 8),
            (ModeTerminator, 4));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("ï»¿A");
    }

    [Test]
    public async Task NoEci_BomBytes_TreatedAsUtf8AndStripped()
    {
        var data = Build(
            (ModeByte, 4), (4, 8), (0xEF, 8), (0xBB, 8), (0xBF, 8), ('A', 8),
            (ModeTerminator, 4));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("A");
    }

    [Test]
    public async Task EciTwoByteDesignator_Parses()
    {
        // 2-byte designator form: 10xxxxxx xxxxxxxx; value 26 = UTF-8
        var data = Build(
            (ModeEci, 4), (0x80, 8), (26, 8),
            (ModeByte, 4), (1, 8), ('X', 8),
            (ModeTerminator, 4));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("X");
    }

    [Test]
    public async Task EmptyData_DecodesToEmpty()
    {
        var status = Decode([], out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo(string.Empty);
    }

    [Test]
    public async Task ImplicitTerminator_FewerThanFourBitsRemaining_Succeeds()
    {
        // Single numeric digit, stream ends without an explicit terminator
        var data = Build((ModeNumeric, 4), (1, 10), (7, 4), (0, 6));

        var status = Decode(data, out var text);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.Success);
        await Assert.That(text).IsEquivalentTo("7");
    }

    // Unsupported content (recognized but rejected, never misdecoded)

    [Test]
    [Arguments(ModeKanji)]
    [Arguments(ModeStructuredAppend)]
    [Arguments(ModeFnc1First)]
    [Arguments(ModeFnc1Second)]
    public async Task UnsupportedModes_ReturnUnsupportedContent(int mode)
    {
        var data = Build((mode, 4), (0, 12));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.UnsupportedContent);
    }

    [Test]
    public async Task UnknownEciCharset_ReturnsUnsupportedContent()
    {
        // ECI 20 = Shift-JIS: recognized designator, unsupported charset
        var data = Build(
            (ModeEci, 4), (20, 8),
            (ModeByte, 4), (1, 8), ('A', 8),
            (ModeTerminator, 4));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.UnsupportedContent);
    }

    // Invalid bitstreams (malformed input must fail cleanly, never throw)

    [Test]
    [Arguments(0b0110)]
    [Arguments(0b1010)]
    [Arguments(0b1111)]
    public async Task UnassignedModeIndicators_ReturnInvalidBitstream(int mode)
    {
        var data = Build((mode, 4), (0, 12));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task Numeric_GroupValueAboveRange_ReturnsInvalidBitstream()
    {
        // 10-bit group encodes 3 digits, so values 1000-1023 are invalid
        var data = Build((ModeNumeric, 4), (3, 10), (1000, 10), (ModeTerminator, 4));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task Alphanumeric_PairValueAboveRange_ReturnsInvalidBitstream()
    {
        // 11-bit pair encodes values 0..2024
        var data = Build((ModeAlphanumeric, 4), (2, 9), (2025, 11), (ModeTerminator, 4));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task ByteSegment_CountBeyondStream_ReturnsInvalidBitstream()
    {
        // Declares 200 bytes but the stream ends immediately
        var data = Build((ModeByte, 4), (200, 8), (0, 8));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task Numeric_CountBeyondStream_ReturnsInvalidBitstream()
    {
        var data = Build((ModeNumeric, 4), (100, 10), (0, 2));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task EciDesignator_TruncatedMultiByte_ReturnsInvalidBitstream()
    {
        // 2-byte designator prefix (10xxxxxx) with no second byte
        var data = Build((ModeEci, 4), (0x80, 8));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    [Test]
    public async Task EciDesignator_InvalidPrefix_ReturnsInvalidBitstream()
    {
        // 111xxxxx is not a valid designator length prefix
        var data = Build((ModeEci, 4), (0xE0, 8), (0, 16));

        await Assert.That(Decode(data, out _)).IsEquivalentTo(QRCodeDecodeStatus.InvalidBitstream);
    }

    // Destination sizing

    [Test]
    public async Task DestinationTooSmall_ReportsStatus_NotException()
    {
        var data = Build((ModeNumeric, 4), (3, 10), (123, 10), (ModeTerminator, 4));

        Span<char> tiny = stackalloc char[2];
        var status = QRBinaryDecoder.DecodeBitStream(data, Version, tiny, out _);

        await Assert.That(status).IsEquivalentTo(QRCodeDecodeStatus.DestinationTooSmall);
    }

    // Robustness: untrusted input must never throw

    [Test]
    public void RandomGarbage_NeverThrows()
    {
        var random = new Random(20260712);
        Span<char> destination = stackalloc char[256];
        for (var round = 0; round < 2000; round++)
        {
            var data = new byte[random.Next(0, 80)];
            random.NextBytes(data);

            // Any status is fine; throwing is not.
            QRBinaryDecoder.DecodeBitStream(data, random.Next(1, 41), destination, out _);
        }
    }

    private static QRCodeDecodeStatus Decode(byte[] data, out string text)
    {
        Span<char> destination = stackalloc char[256];
        var status = QRBinaryDecoder.DecodeBitStream(data, Version, destination, out var charsWritten);
        text = destination.Slice(0, charsWritten).ToString();
        return status;
    }

    private static byte[] Build(params (int value, int bits)[] fields)
    {
        var buffer = new byte[64];
        var writer = new BitWriter(buffer);
        foreach (var (value, bits) in fields)
        {
            writer.Write(value, bits);
        }
        writer.Flush();
        return writer.GetData().ToArray();
    }
}
