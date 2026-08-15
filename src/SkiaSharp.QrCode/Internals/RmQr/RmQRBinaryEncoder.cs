using System.Buffers;
using System.Diagnostics;
using System.Text;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// rMQR data-codeword stream (ISO/IEC 23941 7.4): 3-bit mode indicator, per-version
/// character count indicator, segment payload bits, terminator (000, shortened at
/// capacity), zero bits to the byte boundary, then alternating 0xEC / 0x11 pad
/// codewords up to the data codeword count. Single segment; ECI headers are not
/// emitted (non-Latin-1 text is carried as UTF-8 bytes in Byte mode, the Micro QR
/// precedent). Allocation-free: the shared <see cref="BitWriter"/> writes straight
/// into the caller's destination and byte-mode transcoding uses a fixed
/// stack budget with a pool fallback.
/// </summary>
/// <remarks>
/// This is the readable reference-shaped implementation; a register-accumulator
/// fast path (as Micro QR's) is a benchmark-driven follow-up and must stay parity
/// tested against <c>RmQRNaiveReference.NaiveDataCodewords</c>.
/// </remarks>
internal static class RmQRBinaryEncoder
{
    // rMQR byte-mode capacity tops out at 150 bytes (R17x139-M), so any content
    // that passed version selection transcodes into this budget; the pool path
    // only exists for callers that bypass selection.
    private const int StackByteBudget = 160;

    /// <summary>
    /// Writes the data codewords for <paramref name="text"/> into
    /// <paramref name="destination"/> and returns the number of codewords written
    /// (always the version × ECC data codeword count). The caller guarantees the
    /// content fits (see <see cref="RmQRVersionSelector"/>).
    /// </summary>
    /// <param name="text">Content, already analyzed.</param>
    /// <param name="version">Target version.</param>
    /// <param name="eccLevel">Target ECC level.</param>
    /// <param name="analysis">Mode, effective charset (Default / ISO-8859-1 narrow, UTF-8 transcode) and encoded data length.</param>
    /// <param name="destination">At least the data codeword count in size.</param>
    public static int EncodeDataCodewords(ReadOnlySpan<char> text, RmQRVersion version, RmQREccLevel eccLevel, in TextAnalysisResult analysis, Span<byte> destination)
    {
        var codewordCount = RmQRConstants.GetDataCodewordCount(version, eccLevel);
        var capacityBits = codewordCount * 8;
        var mode = analysis.EncodingMode;
        if (destination.Length < codewordCount)
            throw new ArgumentException($"Destination too small: {codewordCount} data codewords required, got {destination.Length} bytes.", nameof(destination));
        Debug.Assert(RmQRVersionSelector.GetRequiredBits(version, mode, analysis.DataLength) <= capacityBits, "content must fit (version selection guarantees this)");

        var writer = new BitWriter(destination.Slice(0, codewordCount));
        writer.Write(RmQRConstants.GetModeIndicatorValue(mode), RmQRConstants.ModeIndicatorLength);
        writer.Write(analysis.DataLength, RmQRConstants.GetCountIndicatorLength(version, mode));

        switch (mode)
        {
            case EncodingMode.Numeric:
                WriteNumeric(ref writer, text);
                break;
            case EncodingMode.Alphanumeric:
                WriteAlphanumeric(ref writer, text);
                break;
            case EncodingMode.Byte:
                WriteByte(ref writer, text, analysis.EciMode);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(analysis), $"Encoding mode {mode} is not supported by rMQR.");
        }

        // Terminator: 000, shortened to whatever capacity remains (possibly zero bits).
        var terminator = Math.Min(RmQRConstants.TerminatorLength, capacityBits - writer.BitPosition);
        if (terminator > 0)
            writer.Write(0, terminator);

        // Zero bits to the byte boundary (capacity is a whole number of bytes, so
        // this never overruns), then alternating pad codewords.
        var misalignment = writer.BitPosition & 7;
        if (misalignment != 0)
            writer.Write(0, 8 - misalignment);
        writer.WritePadBytes(codewordCount - writer.BitPosition / 8);

        Debug.Assert(writer.BitPosition == capacityBits);
        return codewordCount;
    }

    /// <summary>Digits in groups of three (10 bits), a two-digit tail (7 bits) or one-digit tail (4 bits).</summary>
    private static void WriteNumeric(ref BitWriter writer, ReadOnlySpan<char> digits)
    {
        var i = 0;
        for (; i + 3 <= digits.Length; i += 3)
        {
            var value = (digits[i] - '0') * 100 + (digits[i + 1] - '0') * 10 + (digits[i + 2] - '0');
            writer.Write(value, 10);
        }

        switch (digits.Length - i)
        {
            case 2:
                writer.Write((digits[i] - '0') * 10 + (digits[i + 1] - '0'), 7);
                break;
            case 1:
                writer.Write(digits[i] - '0', 4);
                break;
        }
    }

    /// <summary>Character pairs as 45·a + b (11 bits), a single tail character as 6 bits.</summary>
    private static void WriteAlphanumeric(ref BitWriter writer, ReadOnlySpan<char> chars)
    {
        var i = 0;
        for (; i + 2 <= chars.Length; i += 2)
        {
            writer.Write(CharacterSets.GetAlphanumericValue(chars[i]) * 45 + CharacterSets.GetAlphanumericValue(chars[i + 1]), 11);
        }

        if (i < chars.Length)
            writer.Write(CharacterSets.GetAlphanumericValue(chars[i]), 6);
    }

    /// <summary>
    /// Byte mode: chars ≤ 0xFF narrow directly (Default / ISO-8859-1, validated by
    /// the analyzer); otherwise the text is transcoded to UTF-8 (no ECI header).
    /// </summary>
    private static void WriteByte(ref BitWriter writer, ReadOnlySpan<char> text, EciMode eciMode)
    {
        if (eciMode is EciMode.Default or EciMode.Iso8859_1)
        {
            for (var i = 0; i < text.Length; i++)
                writer.Write((byte)text[i], 8);
            return;
        }

        if (eciMode != EciMode.Utf8)
            throw new ArgumentOutOfRangeException(nameof(eciMode), $"Unsupported charset {eciMode} for rMQR Byte mode.");

        var maxByteCount = text.Length * 3; // UTF-16 → UTF-8 worst case is 3 bytes per char (4-byte sequences come from surrogate pairs)
        if (maxByteCount <= StackByteBudget)
        {
            Span<byte> utf8 = stackalloc byte[StackByteBudget];
            var length = GetUtf8Bytes(text, utf8);
            WriteBytes(ref writer, utf8.Slice(0, length));
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try
            {
                var length = GetUtf8Bytes(text, rented);
                WriteBytes(ref writer, rented.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void WriteBytes(ref BitWriter writer, scoped ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            writer.Write(bytes[i], 8);
    }

    private static int GetUtf8Bytes(ReadOnlySpan<char> text, Span<byte> destination)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return Encoding.UTF8.GetBytes(text, destination);
#else
        // netstandard2.0 has no span overload; this path is the documented
        // exception to the allocation-free contract on that TFM.
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        bytes.CopyTo(destination);
        return bytes.Length;
#endif
    }
}
