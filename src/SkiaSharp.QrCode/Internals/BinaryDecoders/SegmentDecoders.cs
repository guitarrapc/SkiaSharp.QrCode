using System.Buffers;
using System.Text;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Internals.BinaryDecoders;

/// <summary>
/// Effective charset of a Byte mode segment. Standard QR can pin it via an ECI
/// header; Micro QR has no ECI, so it is always <see cref="Unspecified"/> there.
/// </summary>
internal enum ByteSegmentCharset
{
    /// <summary>No declared charset: UTF-8 when the payload validates as UTF-8, else ISO-8859-1.</summary>
    Unspecified,
    Iso8859_1,
    Utf8,
}

/// <summary>
/// Segment payload decoders shared by the symbology bitstream decoders
/// (Standard QR, Micro QR). The mode/count indicator layout differs per
/// symbology and stays in the caller; the payload bit groups (ISO/IEC 18004
/// 7.4.3-7.4.5: numeric 10/7/4, alphanumeric 11/6, byte 8·count) and the byte
/// charset heuristics are identical.
/// </summary>
internal static class SegmentDecoders
{
    // Value → character table for alphanumeric mode (ISO/IEC 18004 Table 5),
    // the inverse of CharacterSets.GetAlphanumericValue.
    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    /// <summary>Decodes a numeric segment payload of <paramref name="count"/> digits.</summary>
    public static QRCodeDecodeStatus DecodeNumericPayload(ref BitReader reader, int totalBits, int count, Span<char> destination, ref int charsWritten)
    {
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

    /// <summary>Decodes an alphanumeric segment payload of <paramref name="count"/> characters.</summary>
    public static QRCodeDecodeStatus DecodeAlphanumericPayload(ref BitReader reader, int totalBits, int count, Span<char> destination, ref int charsWritten)
    {
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

    /// <summary>
    /// Decodes a byte segment payload of <paramref name="count"/> bytes, resolving
    /// the effective charset (UTF-8 heuristic / BOM handling / ISO-8859-1 widening).
    /// </summary>
    /// <param name="byteBuffer">Scratch buffer for the segment bytes; must hold at least <paramref name="count"/> bytes.</param>
    public static QRCodeDecodeStatus DecodeBytePayload(ref BitReader reader, int totalBits, int count, ByteSegmentCharset charset, byte[] byteBuffer, Span<char> destination, ref int charsWritten)
    {
        if (totalBits - reader.BitPosition < count * 8)
            return QRCodeDecodeStatus.InvalidBitstream;
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
            ByteSegmentCharset.Utf8 => true,
            ByteSegmentCharset.Iso8859_1 => false,
            _ => IsValidUtf8(bytes),
        };
        if (charset != ByteSegmentCharset.Iso8859_1
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
