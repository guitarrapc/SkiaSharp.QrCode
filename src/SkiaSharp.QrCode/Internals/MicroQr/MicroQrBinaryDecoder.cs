using System.Buffers;
using SkiaSharp.QrCode.Internals.BinaryDecoders;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Internals.MicroQr;

/// <summary>
/// Decodes the Micro QR data bitstream (mode segments) back into text.
/// </summary>
/// <remarks>
/// Inverse of <see cref="MicroQrBinaryEncoder"/>. Micro QR framing differs from
/// Standard QR (ISO/IEC 18004 Table 2/3):
/// <list type="bullet">
/// <item>Mode indicator is version − 1 bits wide (M1 has none; Numeric is implied).
/// Values: Numeric = 0, Alphanumeric = 1, Byte = 2, Kanji = 3 (reported as
/// <see cref="QRCodeDecodeStatus.UnsupportedContent"/>); M4 values 4-7 are undefined.</item>
/// <item>Character count indicator is 3-6 bits (Numeric = version + 2, others = version + 1).</item>
/// <item>The terminator (2·version + 1 zero bits) is exactly a Numeric mode
/// indicator followed by an all-zero count — a zero-count Numeric segment ends the
/// stream. It may be truncated when the data fills the capacity.</item>
/// <item>No ECI: byte segments always use the UTF-8-validates heuristic.</item>
/// <item>The bit capacity is not a whole number of bytes for M1/M3: the final data
/// codeword carries 4 bits in its high nibble, so decoding is bounded by
/// <c>dataBitCount</c>, not by the codeword byte length.</item>
/// </list>
/// Segment payload decoding is shared with the other symbology decoders via
/// <see cref="SegmentDecoders"/>.
/// </remarks>
internal static class MicroQrBinaryDecoder
{
    // Mode indicator values (ISO/IEC 18004 Table 2, Micro QR column).
    private const int ModeNumeric = 0;
    private const int ModeAlphanumeric = 1;
    private const int ModeByte = 2;
    private const int ModeKanji = 3;

    /// <summary>
    /// Decodes the corrected data codewords into characters.
    /// </summary>
    /// <param name="data">Corrected data codewords (without ECC); for M1/M3 the final codeword carries data in its high nibble only.</param>
    /// <param name="dataBitCount">Data capacity in bits for the version/ECC combination (bounds the stream; 4 less than <c>data.Length * 8</c> for M1/M3).</param>
    /// <param name="version">Micro QR version (M1-M4), determines mode/count indicator widths.</param>
    /// <param name="destination">Destination for decoded characters.</param>
    /// <param name="charsWritten">Number of characters written to <paramref name="destination"/>.</param>
    public static QRCodeDecodeStatus DecodeBitStream(ReadOnlySpan<byte> data, int dataBitCount, MicroQrVersion version, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        var reader = new BitReader(data);
        var totalBits = dataBitCount;
        var modeBits = MicroQrConstants.GetModeIndicatorLength(version);

        byte[]? rentedBytes = null;
        try
        {
            while (true)
            {
                // Not enough bits left for a mode indicator: the terminator was
                // shortened away at capacity (ISO/IEC 18004 7.4.9).
                if (totalBits - reader.BitPosition < modeBits)
                    break;

                // M1 has no mode indicator; Numeric is implied.
                var modeValue = modeBits == 0 ? ModeNumeric : reader.Reads(modeBits);
                if (modeValue == ModeKanji)
                    return QRCodeDecodeStatus.UnsupportedContent;
                if (modeValue > ModeKanji)
                    return QRCodeDecodeStatus.InvalidBitstream; // M4 indicators 4-7 are undefined

                var mode = modeValue switch
                {
                    ModeNumeric => EncodingMode.Numeric,
                    ModeAlphanumeric => EncodingMode.Alphanumeric,
                    _ => EncodingMode.Byte,
                };

                var countBits = MicroQrConstants.GetCountIndicatorLength(version, mode);
                if (totalBits - reader.BitPosition < countBits)
                {
                    // A Numeric indicator (all zero bits) running out of room is a
                    // truncated terminator; anything else is a truncated segment.
                    if (modeValue == ModeNumeric)
                        break;
                    return QRCodeDecodeStatus.InvalidBitstream;
                }

                var count = reader.Reads(countBits);
                if (count == 0)
                {
                    // Numeric + zero count is the terminator. Zero-count segments
                    // in other modes are empty (the encoder never emits them).
                    if (modeValue == ModeNumeric)
                        break;
                    continue;
                }

                switch (modeValue)
                {
                    case ModeNumeric:
                        {
                            var status = SegmentDecoders.DecodeNumericPayload(ref reader, totalBits, count, destination, ref charsWritten);
                            if (status != QRCodeDecodeStatus.Success)
                                return status;
                            break;
                        }
                    case ModeAlphanumeric:
                        {
                            var status = SegmentDecoders.DecodeAlphanumericPayload(ref reader, totalBits, count, destination, ref charsWritten);
                            if (status != QRCodeDecodeStatus.Success)
                                return status;
                            break;
                        }
                    default:
                        {
                            // Micro QR data codewords top out at 16 bytes (M4-L).
                            rentedBytes ??= ArrayPool<byte>.Shared.Rent(data.Length);
                            var status = SegmentDecoders.DecodeBytePayload(ref reader, totalBits, count, ByteSegmentCharset.Unspecified, rentedBytes, destination, ref charsWritten);
                            if (status != QRCodeDecodeStatus.Success)
                                return status;
                            break;
                        }
                }
            }

            return QRCodeDecodeStatus.Success;
        }
        finally
        {
            if (rentedBytes is not null)
                ArrayPool<byte>.Shared.Return(rentedBytes, clearArray: false);
        }
    }
}
