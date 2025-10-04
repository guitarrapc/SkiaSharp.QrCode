using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// QR code text encoder to binary string.
/// </summary>
internal class QRTextEncoder
{
    private readonly StringBuilder _builder;

    public QRTextEncoder(int estimatedCapacity)
    {
        _builder = new StringBuilder(estimatedCapacity);
    }

    /// <summary>
    /// Writes mode indicator (4 bits) and optional ECI header (12 bits).
    /// </summary>
    /// <param name="mode">Encoding mode.</param>
    /// <param name="eci">ECI mode for character encoding.</param>
    public void WriteMode(EncodingMode mode, EciMode eci)
    {
        // ECI mode requires special header before mode indicator
        if (eci != EciMode.Default)
        {
            // ECI mode indicator: 0111 (4 bits)
            _builder.Append(DecToBin((int)EncodingMode.ECI, 4));
            // ECI assignment number (8 bits for 00000000-000000FF)
            _builder.Append(DecToBin((int)eci, 8));
        }

        // Standard mode indicator (4 bits)
        _builder.Append(DecToBin((int)mode, 4));
    }

    /// <summary>
    /// Writes character count indicator (8-16 bits depending on version and mode)
    /// </summary>
    /// <param name="count">Number of input characters</param>
    /// <param name="version">QR code version (1-40)</param>
    /// <param name="mode">Encoding mode (affects bit length of count indicator)</param>
    public void WriteCharacterCount(int count, int version, EncodingMode mode)
    {
        var bitsLength = GetCountIndicatorLength(version, mode);
        _builder.Append(DecToBin(count, bitsLength));
    }

    /// <summary>
    /// Writes encoded data based on mode
    /// </summary>
    /// <param name="plainText">Input text to encode</param>
    /// <param name="mode">Encoding mode</param>
    /// <param name="eci">ECI mode for character encoding</param>
    /// <param name="utf8Bom">Whether to include UTF-8 BOM</param>
    public void WriteData(string plainText, EncodingMode mode, EciMode eci, bool utf8Bom)
    {
        var encoded = mode switch
        {
            EncodingMode.Numeric => EncodeNumeric(plainText),
            EncodingMode.Alphanumeric => EncodeAlphanumeric(plainText),
            EncodingMode.Byte => EncodeByte(plainText, eci, utf8Bom),
            EncodingMode.Kanji => throw new NotImplementedException("Kanji encoding not yet implemented, use Byte mode with UTF-8 encoding for Japanese text."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "Invalid encoding mode"),
        };
        _builder.Append(encoded);
    }

    /// <summary>
    /// Writes terminator (up to 4 bits), byte alignment (0-7 bits) and pad bytes (0-16 bits) to reach target capacity
    /// </summary>
    /// <param name="targetBitCount">Target total bit count</param>
    /// <remarks>
    /// Padding structure:
    /// 1. Terminator: 0000 (up to 4 bits)
    /// 2. Byte alignment: 0-7 bits to align to byte boundary
    /// 3. Pad bytes: Alternating 11101100 (0xEC) and 00010001 (0x11)
    /// </remarks>
    public void WritePadding(int targetBitCount)
    {
        var remaining = targetBitCount - _builder.Length;

        // 1. Terminator (up to 4 bits)
        if (remaining > 0)
        {
            _builder.Append('0', Math.Min(remaining, 4));
        }

        // 2. Byte boundary alignment (0-7 bits)
        if ((_builder.Length % 8) != 0)
        {
            _builder.Append('0', 8 - (_builder.Length % 8));
        }

        // 3. Alternating pad bytes (0xEC, 0x11, 0xEC, 0x11, ...) until target length
        while (_builder.Length < targetBitCount)
        {
            _builder.Append("1110110000010001"); // 0xEC + 0x11 = 16 bits
        }

        // 4. Trim if exceeded
        if (_builder.Length > targetBitCount)
        {
            _builder.Length = targetBitCount;
        }
    }

    /// <summary>
    /// Returns the encoded binary string
    /// </summary>
    /// <returns>Binary string representation</returns>
    public string ToBinaryString() => _builder.ToString();

    // private methods

    /// <summary>
    /// Encodes numeric text (0-9) to binary string
    /// </summary>
    /// <param name="text"></param>
    /// <remarks>
    /// Encoding: Groups of 3 digits → 10 bits, 2 digits → 7 bits, 1 digit → 4 bits.
    /// Example: "123" → DecToBin(123, 10) = "0001111011"
    /// </remarks>
    private string EncodeNumeric(string text)
    {
        var codeText = new StringBuilder(text.Length * 10 / 3 + 10);
        var length = text.Length;
        var index = 0;

        // Process 3 digits at a time
        while (index + 2 < length)
        {
            var dec = (text[index] - '0') * 100
                + (text[index + 1] - '0') * 10
                + (text[index + 2] - '0');
            codeText.Append(DecToBin(dec, 10));
            index += 3;
        }

        // Process remaining 2 digits
        if (index + 1 < length)
        {
            var dec = (text[index] - '0') * 10
                + (text[index + 1] - '0');
            codeText.Append(DecToBin(dec, 7));
            index += 2;
        }

        // Process remaining 1 digit
        if (index < length)
        {
            var dec = (text[index] - '0');
            codeText.Append(DecToBin(dec, 4));
        }

        return codeText.ToString();
    }

    /// <summary>
    /// Encodes alphanumeric text (0-9, A-Z, space, $, %, *, +, -, ., /, :) to binary string
    /// </summary>
    /// <param name="text"></param>
    /// <remarks>
    /// Encoding: Groups of 2 chars → 11 bits, 1 char → 6 bits.
    /// </remarks>
    private string EncodeAlphanumeric(string text)
    {
        var codeText = new StringBuilder(text.Length * 10 / 3 + 10);
        var length = text.Length;
        var index = 0;

        // Process 2 characters at a time
        while (index + 1 < length)
        {
            var dec = GetAlphanumericValue(text[index]) * 45
                + GetAlphanumericValue(text[index + 1]);
            codeText.Append(DecToBin(dec, 11));
            index += 2;
        }

        // Process remaining 1 character
        if (index < length)
        {
            var dec = GetAlphanumericValue(text[index]);
            codeText.Append(DecToBin(dec, 6));
        }

        return codeText.ToString();
    }

    /// <summary>
    /// Encodes byte mode text to binary string
    /// </summary>
    /// <param name="text"></param>
    /// <param name="eciMode"></param>
    /// <param name="utf8BOM"></param>
    /// <remarks>
    /// Encoding: Each byte → 8 bits (UTF-8 or ISO-8859-x based on ECI mode).
    /// </remarks>
    private string EncodeByte(string text, EciMode eciMode, bool utf8BOM)
    {
        // Determine encoding based on ECI mode and validity
        var codeBytes = eciMode switch
        {
            EciMode.Default => IsValidISO(text)
                ? Encoding.GetEncoding("ISO-8859-1").GetBytes(text)
                : utf8BOM
                    ? [.. Encoding.UTF8.GetPreamble(), ..Encoding.UTF8.GetBytes(text)]
                    : Encoding.UTF8.GetBytes(text),
            EciMode.Iso8859_1 => Encoding.GetEncoding("ISO-8859-1").GetBytes(text),
            EciMode.Utf8 => utf8BOM
                ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]
                : Encoding.UTF8.GetBytes(text),
            _ => throw new ArgumentOutOfRangeException(nameof(eciMode), "Unsupported ECI mode for Byte encoding"),
        };

        // Convert each byte to 8-bit binary string
        var codeText = new StringBuilder(codeBytes.Length * 8);
        foreach (var b in codeBytes)
        {
            codeText.Append(DecToBin(b, 8));
        }

        return codeText.ToString();
    }

    // helpers

    /// <summary>
    /// Converts decimal number to binary string with specified bit length
    /// </summary>
    /// <param name="num">Decimal number to convert.</param>
    /// <param name="bits">Number of bits in output (with leading zeros).</param>
    /// <returns>Binary string (e.g., 5 → "0101" for bits=4).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string DecToBin(int num, int bits)
    {
        var binStr = Convert.ToString(num, 2);
        return binStr.PadLeft(bits, '0');
    }

    /// <summary>
    /// Gets the bit length of character count indicator based on version and mode
    /// </summary>
    /// <param name="version">QR code version (1-40).</param>
    /// <param name="mode">Encoding mode.</param>
    /// <returns>
    /// Bit length (8-16 bits):
    /// - Version 1-9: Numeric=10, Alphanumeric=9, Byte=8
    /// - Version 10-26: Numeric=12, Alphanumeric=11, Byte=16
    /// - Version 27-40: Numeric=14, Alphanumeric=13, Byte=16
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCountIndicatorLength(int version, EncodingMode mode)
    {
        if (version < 10)
        {
            return mode switch
            {
                EncodingMode.Numeric => 10,
                EncodingMode.Alphanumeric => 9,
                EncodingMode.Byte => 8,
                EncodingMode.Kanji => 8,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), "Invalid encoding mode"),
            };
        }
        else if (version < 27)
        {
            return mode switch
            {
                EncodingMode.Numeric => 12,
                EncodingMode.Alphanumeric => 11,
                EncodingMode.Byte => 16,
                EncodingMode.Kanji => 10,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), "Invalid encoding mode"),
            };
        }
        else
        {
            return mode switch
            {
                EncodingMode.Numeric => 14,
                EncodingMode.Alphanumeric => 13,
                EncodingMode.Byte => 16,
                EncodingMode.Kanji => 12,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), "Invalid encoding mode"),
            };
        }
    }

    /// <summary>
    /// Validates if text can be encoded in ISO-8859-1 without data loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidISO(string input)
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(input);
        var result = Encoding.GetEncoding("ISO-8859-1").GetString(bytes, 0, bytes.Length);
        return string.Equals(input, result);
    }
}
