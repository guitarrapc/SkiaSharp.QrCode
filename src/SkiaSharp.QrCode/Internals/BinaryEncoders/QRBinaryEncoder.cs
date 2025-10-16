using System.Buffers;
using System.Text;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

internal ref struct QRBinaryEncoder
{
    // stackalloc threshold for temporary buffers. Avoids stackoverflow for large inputs.
    // if input length exceeds this, ArrayPool<byte> is used instead.
    private const int StackAllocThreshold = 256;

    private BitWriter _writer;

    /// <summary>
    /// Gets the current bit position (actual number of bits written).
    /// </summary>
    public int BitPosition => _writer.BitPosition;

    /// <summary>
    /// Gets the number of bytes written (rounded up to nearest byte)
    /// </summary>
    public int ByteCount => _writer.ByteCount;

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
        var alignmentBits = (8 - _writer.BitPosition % 8) % 8;
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
    /// Writes encoded data byte based on mode
    /// </summary>
    /// <param name="textSpan">Input text to encode</param>
    /// <param name="encoding">Encoding mode</param>
    /// <param name="eci">ECI mode for character encoding</param>
    /// <param name="utf8Bom">Whether to include UTF-8 BOM</param>
    public void WriteData(ReadOnlySpan<char> textSpan, EncodingMode encoding, EciMode eci, bool utf8Bom)
    {
        switch (encoding)
        {
            case EncodingMode.Numeric:
                EncodeNumeric(textSpan);
                break;
            case EncodingMode.Alphanumeric:
                EncodeAlphanumeric(textSpan);
                break;
            case EncodingMode.Byte:
                EncodeByte(textSpan, eci, utf8Bom);
                break;
            case EncodingMode.Kanji:
                throw new NotImplementedException("Kanji encoding not yet implemented, use Byte mode with UTF-8 encoding for Japanese text.");
            default:
                throw new ArgumentOutOfRangeException(nameof(encoding), "Invalid encoding mode");
        }
    }

    /// <summary>
    /// Writes numeric data from string 
    /// </summary>
    /// <param name="textSpan"></param>
    private void EncodeNumeric(ReadOnlySpan<char> textSpan)
    {
        // Convert string to ASCII bytes (numeric chars are ASCII compatible)
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        // ascii is 1 char = 1 byte, so length is same
        if (textSpan.Length <= StackAllocThreshold)
        {
            Span<byte> asciiBytes = stackalloc byte[textSpan.Length];
            var bytesWritten = Encoding.ASCII.GetBytes(textSpan, asciiBytes);
            WriteNumericData(asciiBytes.Slice(0, bytesWritten));
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(textSpan.Length);
            try
            {
                var bytesWritten = Encoding.ASCII.GetBytes(textSpan, buffer);
                WriteNumericData(buffer.AsSpan(0, bytesWritten));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
#else
        // Fallback for older frameworks without Span support
        var input = textSpan.ToString();
        var asciiBytes = Encoding.ASCII.GetBytes(input);
        WriteNumericData(asciiBytes.AsSpan());
#endif
    }

    /// <summary>
    ///  Writes alphanumeric data from string
    /// </summary>
    /// <param name="textSpan"></param>
    private void EncodeAlphanumeric(ReadOnlySpan<char> textSpan)
    {
        // Convert string to ASCII bytes (numeric chars are ASCII compatible)
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        // ascii is 1 char = 1 byte, so length is same
        if (textSpan.Length <= StackAllocThreshold)
        {
            Span<byte> asciiBytes = stackalloc byte[textSpan.Length];
            var bytesWritten = Encoding.ASCII.GetBytes(textSpan, asciiBytes);
            WriteAlphanumericData(asciiBytes.Slice(0, bytesWritten));
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(textSpan.Length);
            try
            {
                var bytesWritten = Encoding.ASCII.GetBytes(textSpan, buffer);
                WriteAlphanumericData(buffer.AsSpan(0, bytesWritten));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
#else
        // Fallback for older frameworks without Span support
        var input = textSpan.ToString();
        var asciiBytes = Encoding.ASCII.GetBytes(input);
        WriteAlphanumericData(asciiBytes.AsSpan());
#endif
    }

    /// <summary>
    ///  Writes alphanumeric data from string
    /// </summary>
    /// <param name="textSpan"></param>
    private void EncodeByte(ReadOnlySpan<char> textSpan, EciMode eci, bool utf8Bom)
    {
        // Determine encoding based on ECI mode
        var maxByteCount = textSpan.Length * 4;

        if (maxByteCount <= StackAllocThreshold)
        {
            Span<byte> buffer = stackalloc byte[textSpan.Length * 4];
            var length = GetByteData(textSpan, eci, utf8Bom, buffer);
            WriteByteData(buffer.Slice(0, length));
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try
            {
                var length = GetByteData(textSpan, eci, utf8Bom, buffer);
                WriteByteData(buffer.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Writes numeric data (groups of 3 digits as 10 bits, 2 digits as 7 bits, 1 digit as 4 bits)
    /// </summary>
    /// <param name="digits"></param>
    private void WriteNumericData(scoped ReadOnlySpan<byte> digits)
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
    private void WriteAlphanumericData(scoped ReadOnlySpan<byte> chars)
    {
        var length = chars.Length;
        var i = 0;

        // Pairs of chars = 11bits
        // Single chars = 6bits

        // Process 2 characters at a time
        while (i + 1 < length)
        {
            var value = QRCodeConstants.GetAlphanumericValue((char)chars[i]) * 45
                + QRCodeConstants.GetAlphanumericValue((char)chars[i + 1]);
            _writer.Write(value, 11);
            i += 2;
        }

        // Process remaining 1 character
        if (i < length)
        {
            var value = QRCodeConstants.GetAlphanumericValue((char)chars[i]);
            _writer.Write(value, 6);
        }
    }

    /// <summary>
    /// Writes byte data (each byte should be 8-bit)
    /// </summary>
    /// <param name="data"></param>
    private void WriteByteData(scoped ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _writer.Write(b, 8);
        }
    }

    /// <summary>
    /// Gets the encoded data.
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> GetEncodedData() => _writer.GetData();

    /// <summary>
    /// Gets byte data for Byte mode encoding.
    /// </summary>
    /// <param name="textSpan">Text to encode.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <param name="utf8BOM">Whether to include UTF-8 BOM.</param>
    /// <param name="buffer">Buffer to use for encoding.</param>
    /// <returns>Byte array representing the encoded text.</returns>
    private static int GetByteData(ReadOnlySpan<char> textSpan, EciMode eciMode, bool utf8BOM, Span<byte> buffer)
    {
        return eciMode switch
        {
            EciMode.Default => QRCodeConstants.IsValidISO88591(textSpan)
                ? EncodeISO88591(textSpan, buffer)
                : EncodeUtf8(textSpan, utf8BOM, buffer),
            EciMode.Iso8859_1 => EncodeISO88591(textSpan, buffer),
            EciMode.Utf8 => EncodeUtf8(textSpan, utf8BOM, buffer),
            _ => throw new ArgumentOutOfRangeException(nameof(eciMode), "Unsupported ECI mode for Byte encoding"),
        };

        static int EncodeISO88591(ReadOnlySpan<char> textSpan, Span<byte> buffer)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            return Encoding.GetEncoding("ISO-8859-1").GetBytes(textSpan, buffer);
#else
            var input = textSpan.ToString();
            ReadOnlySpan<byte> bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(input);
            bytes.CopyTo(buffer);
            return bytes.Length;
#endif
        }

        static int EncodeUtf8(ReadOnlySpan<char> textSpan, bool utf8BOM, Span<byte> buffer)
        {
            var offset = 0;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            if (utf8BOM)
            {
                var preamble = Encoding.UTF8.GetPreamble();
                preamble.CopyTo(buffer);
                offset += preamble.Length;

                return offset + Encoding.UTF8.GetBytes(textSpan, buffer.Slice(offset));
            }
            else
            {
                return Encoding.UTF8.GetBytes(textSpan, buffer);
            }
#else
            if (utf8BOM)
            {
                var preamble = Encoding.UTF8.GetPreamble();
                preamble.CopyTo(buffer);
                offset += preamble.Length;

                var input = textSpan.ToString();
                ReadOnlySpan<byte> utf8bytes = Encoding.UTF8.GetBytes(input);
                utf8bytes.CopyTo(buffer.Slice(offset));
                return offset + utf8bytes.Length;
            }
            else
            {
                var input = textSpan.ToString();
                ReadOnlySpan<byte> utf8bytes = Encoding.UTF8.GetBytes(input);
                utf8bytes.CopyTo(buffer);
                return utf8bytes.Length;
            }
#endif
        }
    }
}
