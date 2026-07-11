using System.Buffers;
using System.Text;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Internals.BinaryDecoders;

/// <summary>
/// Decodes the QR data bitstream (mode segments) back into text.
/// </summary>
/// <remarks>
/// Inverse of <see cref="QRBinaryEncoder"/>. Supports the segments the encoder can
/// produce — Numeric, Alphanumeric, Byte (ISO-8859-1 / UTF-8) and ECI headers — plus
/// multi-segment streams from other encoders. Kanji mode, FNC1 and Structured Append
/// are recognized but reported as <see cref="QRCodeDecodeStatus.UnsupportedContent"/>.
/// <para>
/// Byte segments without an ECI header have no declared charset (ISO/IEC 18004
/// defaults to ISO-8859-1, but UTF-8 payloads are common in the wild). The decoder
/// uses UTF-8 when the payload validates as UTF-8 (or carries a BOM) and falls back
/// to ISO-8859-1 otherwise — ASCII decodes identically either way.
/// </para>
/// </remarks>
internal static class QRBinaryDecoder
{
    // Mode indicators (ISO/IEC 18004 Table 2). EncodingMode covers the modes the
    // encoder writes; the remaining indicators are listed here for classification.
    private const int ModeTerminator = 0b0000;
    private const int ModeNumeric = 0b0001;
    private const int ModeAlphanumeric = 0b0010;
    private const int ModeStructuredAppend = 0b0011;
    private const int ModeByte = 0b0100;
    private const int ModeFnc1First = 0b0101;
    private const int ModeEci = 0b0111;
    private const int ModeKanji = 0b1000;
    private const int ModeFnc1Second = 0b1001;

    // ECI assignment numbers this decoder can map to a charset.
    private const int EciIso8859_1a = 1;  // ISO-8859-1 (historical assignment)
    private const int EciIso8859_1b = 3;  // ISO-8859-1
    private const int EciUtf8 = 26;       // UTF-8
    private const int EciAscii = 27;      // US-ASCII (subset of ISO-8859-1)

    // Value → character table for alphanumeric mode (ISO/IEC 18004 Table 5),
    // the inverse of QRCodeConstants.GetAlphanumericValue.
    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private enum ByteCharset
    {
        Unspecified, // no ECI seen: UTF-8 when the payload validates as UTF-8, else ISO-8859-1
        Iso8859_1,
        Utf8,
    }

    /// <summary>
    /// Decodes the corrected data codewords into characters.
    /// </summary>
    /// <param name="data">Corrected data codewords (without ECC).</param>
    /// <param name="version">QR code version (1-40), determines character count indicator widths.</param>
    /// <param name="destination">Destination for decoded characters.</param>
    /// <param name="charsWritten">Number of characters written to <paramref name="destination"/>.</param>
    public static QRCodeDecodeStatus DecodeBitStream(ReadOnlySpan<byte> data, int version, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        var reader = new BitReader(data);
        var totalBits = data.Length * 8;
        var charset = ByteCharset.Unspecified;

        byte[]? rentedBytes = null;
        try
        {
            while (true)
            {
                // Fewer than 4 bits left acts as an implicit terminator (ISO/IEC 18004 7.4.9)
                if (totalBits - reader.BitPosition < 4)
                    break;

                var mode = reader.Reads(4);
                if (mode == ModeTerminator)
                    break;

                switch (mode)
                {
                    case ModeNumeric:
                    {
                        var status = DecodeNumeric(ref reader, totalBits, version, destination, ref charsWritten);
                        if (status != QRCodeDecodeStatus.Success)
                            return status;
                        break;
                    }
                    case ModeAlphanumeric:
                    {
                        var status = DecodeAlphanumeric(ref reader, totalBits, version, destination, ref charsWritten);
                        if (status != QRCodeDecodeStatus.Success)
                            return status;
                        break;
                    }
                    case ModeByte:
                    {
                        // Segment payload bytes are staged in a rented buffer (reused
                        // across segments) so the charset conversion sees them whole.
                        rentedBytes ??= ArrayPool<byte>.Shared.Rent(data.Length);
                        var status = DecodeByte(ref reader, totalBits, version, charset, rentedBytes, destination, ref charsWritten);
                        if (status != QRCodeDecodeStatus.Success)
                            return status;
                        break;
                    }
                    case ModeEci:
                    {
                        var status = ReadEciDesignator(ref reader, totalBits, out var eciValue);
                        if (status != QRCodeDecodeStatus.Success)
                            return status;
                        switch (eciValue)
                        {
                            case EciIso8859_1a:
                            case EciIso8859_1b:
                            case EciAscii:
                                charset = ByteCharset.Iso8859_1;
                                break;
                            case EciUtf8:
                                charset = ByteCharset.Utf8;
                                break;
                            default:
                                return QRCodeDecodeStatus.UnsupportedContent;
                        }
                        break;
                    }
                    case ModeKanji:
                    case ModeStructuredAppend:
                    case ModeFnc1First:
                    case ModeFnc1Second:
                        return QRCodeDecodeStatus.UnsupportedContent;
                    default:
                        return QRCodeDecodeStatus.InvalidBitstream;
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

    private static QRCodeDecodeStatus DecodeNumeric(ref BitReader reader, int totalBits, int version, Span<char> destination, ref int charsWritten)
    {
        var countBits = EncodingMode.Numeric.GetCountIndicatorLength(version);
        if (totalBits - reader.BitPosition < countBits)
            return QRCodeDecodeStatus.InvalidBitstream;
        var count = reader.Reads(countBits);

        if (destination.Length - charsWritten < count)
            return QRCodeDecodeStatus.DestinationTooSmall;

        // Groups of 3 digits (10 bits), then 2 digits (7 bits) or 1 digit (4 bits)
        while (count >= 3)
        {
            if (totalBits - reader.BitPosition < 10)
                return QRCodeDecodeStatus.InvalidBitstream;
            var value = reader.Reads(10);
            if (value > 999)
                return QRCodeDecodeStatus.InvalidBitstream;
            destination[charsWritten++] = (char)('0' + value / 100);
            destination[charsWritten++] = (char)('0' + value / 10 % 10);
            destination[charsWritten++] = (char)('0' + value % 10);
            count -= 3;
        }
        if (count == 2)
        {
            if (totalBits - reader.BitPosition < 7)
                return QRCodeDecodeStatus.InvalidBitstream;
            var value = reader.Reads(7);
            if (value > 99)
                return QRCodeDecodeStatus.InvalidBitstream;
            destination[charsWritten++] = (char)('0' + value / 10);
            destination[charsWritten++] = (char)('0' + value % 10);
        }
        else if (count == 1)
        {
            if (totalBits - reader.BitPosition < 4)
                return QRCodeDecodeStatus.InvalidBitstream;
            var value = reader.Reads(4);
            if (value > 9)
                return QRCodeDecodeStatus.InvalidBitstream;
            destination[charsWritten++] = (char)('0' + value);
        }

        return QRCodeDecodeStatus.Success;
    }

    private static QRCodeDecodeStatus DecodeAlphanumeric(ref BitReader reader, int totalBits, int version, Span<char> destination, ref int charsWritten)
    {
        var countBits = EncodingMode.Alphanumeric.GetCountIndicatorLength(version);
        if (totalBits - reader.BitPosition < countBits)
            return QRCodeDecodeStatus.InvalidBitstream;
        var count = reader.Reads(countBits);

        if (destination.Length - charsWritten < count)
            return QRCodeDecodeStatus.DestinationTooSmall;

        // Pairs of characters (11 bits), then a single character (6 bits)
        while (count >= 2)
        {
            if (totalBits - reader.BitPosition < 11)
                return QRCodeDecodeStatus.InvalidBitstream;
            var value = reader.Reads(11);
            if (value >= 45 * 45)
                return QRCodeDecodeStatus.InvalidBitstream;
            destination[charsWritten++] = AlphanumericChars[value / 45];
            destination[charsWritten++] = AlphanumericChars[value % 45];
            count -= 2;
        }
        if (count == 1)
        {
            if (totalBits - reader.BitPosition < 6)
                return QRCodeDecodeStatus.InvalidBitstream;
            var value = reader.Reads(6);
            if (value >= 45)
                return QRCodeDecodeStatus.InvalidBitstream;
            destination[charsWritten++] = AlphanumericChars[value];
        }

        return QRCodeDecodeStatus.Success;
    }

    private static QRCodeDecodeStatus DecodeByte(ref BitReader reader, int totalBits, int version, ByteCharset charset, byte[] byteBuffer, Span<char> destination, ref int charsWritten)
    {
        var countBits = EncodingMode.Byte.GetCountIndicatorLength(version);
        if (totalBits - reader.BitPosition < countBits)
            return QRCodeDecodeStatus.InvalidBitstream;
        var count = reader.Reads(countBits);

        if (totalBits - reader.BitPosition < count * 8)
            return QRCodeDecodeStatus.InvalidBitstream;
        // byteBuffer is rented at data.Length, which always bounds the byte count
        for (var i = 0; i < count; i++)
        {
            byteBuffer[i] = (byte)reader.Reads(8);
        }

        var bytes = byteBuffer.AsSpan(0, count);

        // Resolve the effective charset. A UTF-8 BOM (the encoder can emit one with
        // utf8BOM: true) is consumed, not decoded — but an explicit ECI ISO-8859-1
        // declaration wins over the BOM heuristic: there, EF BB BF is the legitimate
        // Latin-1 text "ï»¿".
        var useUtf8 = charset switch
        {
            ByteCharset.Utf8 => true,
            ByteCharset.Iso8859_1 => false,
            _ => IsValidUtf8(bytes),
        };
        if (charset != ByteCharset.Iso8859_1
            && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            useUtf8 = true;
            bytes = bytes.Slice(3);
        }

        if (!useUtf8)
        {
            // ISO-8859-1 → UTF-16 is a pure widening cast
            if (destination.Length - charsWritten < bytes.Length)
                return QRCodeDecodeStatus.DestinationTooSmall;
            for (var i = 0; i < bytes.Length; i++)
            {
                destination[charsWritten + i] = (char)bytes[i];
            }
            charsWritten += bytes.Length;
            return QRCodeDecodeStatus.Success;
        }

        return DecodeUtf8(byteBuffer, bytes.Length == count ? 0 : 3, bytes.Length, destination, ref charsWritten);
    }

    private static QRCodeDecodeStatus DecodeUtf8(byte[] byteBuffer, int offset, int byteCount, Span<char> destination, ref int charsWritten)
    {
        if (byteCount == 0)
            return QRCodeDecodeStatus.Success;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        var bytes = byteBuffer.AsSpan(offset, byteCount);
        if (destination.Length - charsWritten < Encoding.UTF8.GetCharCount(bytes))
            return QRCodeDecodeStatus.DestinationTooSmall;
        charsWritten += Encoding.UTF8.GetChars(bytes, destination.Slice(charsWritten));
        return QRCodeDecodeStatus.Success;
#else
        // netstandard2.0 has no span-based Encoding APIs; byteBuffer is already an
        // array, so only the char side needs a temporary rented array.
        var charCount = Encoding.UTF8.GetCharCount(byteBuffer, offset, byteCount);
        if (destination.Length - charsWritten < charCount)
            return QRCodeDecodeStatus.DestinationTooSmall;

        var rentedChars = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            var written = Encoding.UTF8.GetChars(byteBuffer, offset, byteCount, rentedChars, 0);
            rentedChars.AsSpan(0, written).CopyTo(destination.Slice(charsWritten));
            charsWritten += written;
            return QRCodeDecodeStatus.Success;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rentedChars, clearArray: false);
        }
#endif
    }

    private static QRCodeDecodeStatus ReadEciDesignator(ref BitReader reader, int totalBits, out int eciValue)
    {
        // ECI assignment number is 1-3 bytes, length signaled by the leading bits
        // (ISO/IEC 18004 7.4.2): 0xxxxxxx = 8 bits, 10xxxxxx = 16, 110xxxxx = 24.
        eciValue = 0;
        if (totalBits - reader.BitPosition < 8)
            return QRCodeDecodeStatus.InvalidBitstream;

        var first = reader.Reads(8);
        if ((first & 0x80) == 0)
        {
            eciValue = first;
        }
        else if ((first & 0xC0) == 0x80)
        {
            if (totalBits - reader.BitPosition < 8)
                return QRCodeDecodeStatus.InvalidBitstream;
            eciValue = ((first & 0x3F) << 8) | reader.Reads(8);
        }
        else if ((first & 0xE0) == 0xC0)
        {
            if (totalBits - reader.BitPosition < 16)
                return QRCodeDecodeStatus.InvalidBitstream;
            eciValue = ((first & 0x1F) << 16) | reader.Reads(16);
        }
        else
        {
            return QRCodeDecodeStatus.InvalidBitstream;
        }

        return QRCodeDecodeStatus.Success;
    }

    /// <summary>
    /// Strict UTF-8 validation (RFC 3629): rejects overlongs, surrogates and
    /// values above U+10FFFF, so ISO-8859-1 payloads with high bytes fall through
    /// to the Latin-1 path instead of being mangled.
    /// </summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b < 0x80)
            {
                i++;
                continue;
            }

            int continuations;
            int codepoint;
            if ((b & 0xE0) == 0xC0)
            {
                continuations = 1;
                codepoint = b & 0x1F;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                continuations = 2;
                codepoint = b & 0x0F;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                continuations = 3;
                codepoint = b & 0x07;
            }
            else
            {
                return false;
            }

            if (i + continuations >= bytes.Length)
                return false;

            for (var j = 1; j <= continuations; j++)
            {
                var c = bytes[i + j];
                if ((c & 0xC0) != 0x80)
                    return false;
                codepoint = (codepoint << 6) | (c & 0x3F);
            }

            // Overlong encodings, UTF-16 surrogates and out-of-range values
            if (continuations == 1 && codepoint < 0x80)
                return false;
            if (continuations == 2 && (codepoint < 0x800 || (codepoint >= 0xD800 && codepoint <= 0xDFFF)))
                return false;
            if (continuations == 3 && (codepoint < 0x10000 || codepoint > 0x10FFFF))
                return false;

            i += continuations + 1;
        }

        return true;
    }
}
