using SkiaSharp.QrCode.Internals.BinaryEncoders;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Internals;

internal ref struct QRBinaryEncoder
{
    private BitWriter _writer;

    public QRBinaryEncoder(Span<byte> buffer)
    {
        _writer = new BitWriter(buffer);
    }

    /// <summary>
    /// Writes mode indicator (4 bits) and optional ECI header (12 bits).
    /// </summary>
    /// <param name="encoding">Encoding mode.</param>
    /// <param name="eci">ECI mode for character encoding.</param>
    public void WriteMode(EncodingMode encoding, EciMode eci)
    {
        // ECI mode requires special header before mode indicator
        if (eci != EciMode.Default)
        {
            // ECI mode indicator: 0111 (4 bits)
            _writer.Write((int)EncodingMode.ECI, 4);
            // ECI assignment number (8 bits for 00000000-000000FF)
            _writer.Write((int)eci, 8);
        }

        // Standard mode indicator (4 bits)
        _writer.Write((int)encoding, 4);
    }

    /// <summary>
    /// Writes character count indicator (8-16 bits depending on version and mode)
    /// </summary>
    /// <param name="count">Number of input characters</param>
    /// <param name="bitsLength"></param>
    public void WriteCharacterCount(int count, int bitsLength)
    {
        _writer.Write(count, bitsLength);
    }

    /// <summary>
    /// Writes numeric data (groups of 3 digits as 10 bits, 2 digits as 7 bits, 1 digit as 4 bits)
    /// </summary>
    /// <param name="digits"></param>
    public void WriteNumericData(ReadOnlySpan<byte> digits)
    {
        var i = 0;
        var length = digits.Length;

        // Process 3 digits at a time (10 bits)
        while (i + 2 < length)
        {
            var value = (digits[i] - '0') * 100 + (digits[i + 1] - '0') * 10 + (digits[i + 2] - '0');
            _writer.Write(value, 10);
            i += 3;
        }

        // Process remaining 2 digits (7 bits)
        if (i + 1 < length)
        {
            var value = (digits[i] - '0') * 10 + (digits[i + 1] - '0');
            _writer.Write(value, 7);
            i += 2;
        }

        // Process remaining 1 digit (4 bits)
        if (i < length)
        {
            var value = digits[i] - '0';
            _writer.Write(value, 4);
        }
    }

    /// <summary>
    /// Encodes alphanumeric data (0-9, A-Z, space, $, %, *, +, -, ., /, :)
    /// </summary>
    /// <param name="chars"></param>
    /// <remarks>
    /// Encoding: Groups of 2 chars → 11 bits, 1 char → 6 bits.
    /// </remarks>
    public void WriteAlphanumericData(ReadOnlySpan<byte> chars)
    {
        var length = chars.Length;
        var i = 0;

        // Pairs of chars = 11bits
        // Single chars = 6bits

        // Process 2 characters at a time
        while (i + 1 < length)
        {
            var value = GetAlphanumericValue((char)chars[i]) * 45
                + GetAlphanumericValue((char)chars[i + 1]);
            _writer.Write(value, 11);
            i += 2;
        }

        // Process remaining 1 character
        if (i < length)
        {
            var value = GetAlphanumericValue((char)chars[i]);
            _writer.Write(value, 6);
        }
    }

    /// <summary>
    /// Writes byte data (each byte should be 8-bit)
    /// </summary>
    /// <param name="data"></param>
    public void WriteByteData(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _writer.Write(b, 8);
        }
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
        var currentBits = _writer.BitPosition;
        var remaining = targetBitCount - currentBits;

        // 1. Terminator (up to 4 bits)
        if (remaining > 0)
        {
            var terminatorBits = Math.Min(remaining, 4);
            _writer.Write(0, terminatorBits);
        }

        // 2. Byte boundary alignment (0-7 bits)
        var alignmentBits = (8 - (_writer.BitPosition % 8)) % 8;
        if (alignmentBits > 0)
        {
            _writer.Write(0, alignmentBits);
        }

        // 3. Alternating pad bytes (0xEC, 0x11, 0xEC, 0x11, ...) until target length
        var useEC = true;
        while (_writer.BitPosition < targetBitCount)
        {
            _writer.Write(useEC ? 0xEC : 0x11, 8);
            useEC = !useEC;
        }
    }

    /// <summary>
    /// Gets the encoded data.
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> GetEncodedData() => _writer.GetData();
}
