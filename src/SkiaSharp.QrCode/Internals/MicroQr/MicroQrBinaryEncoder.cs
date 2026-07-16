using System.Diagnostics;
using System.Text;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Internals.MicroQr;

/// <summary>
/// Encodes text into Micro QR data codewords: mode indicator, character count,
/// data bits, terminator and padding (ISO/IEC 18004 Micro QR bit stream rules).
/// </summary>
/// <remarks>
/// Micro QR differences from Standard QR handled here:
/// <list type="bullet">
/// <item>Mode indicator is 0-3 bits wide (version − 1); M1 has none.</item>
/// <item>Character count indicator is 3-6 bits wide (version-dependent).</item>
/// <item>Terminator is 3/5/7/9 zero bits, shortened at capacity.</item>
/// <item>No ECI: non-Latin-1 text is emitted as raw UTF-8 bytes in Byte mode
/// (Micro QR has no ECI mode; readers detect UTF-8 heuristically).</item>
/// <item>M1/M3 capacities end on a half byte: the final data codeword is 4 bits
/// stored in the byte's high nibble with a forced-zero low nibble, and a final
/// 4-bit pad codeword is 0000 (never part of the 0xEC/0x11 cycle).</item>
/// </list>
/// Reed-Solomon ECC is computed by the shared <see cref="EccBinaryEncoder"/> over
/// the returned codeword bytes as-is (the half codeword participates as its
/// high-nibble byte value).
/// </remarks>
internal static class MicroQrBinaryEncoder
{
    // M4-L has the largest data section: 16 codewords. The work buffer adds slack
    // because BitWriter stores 32-bit words ahead of the logical position.
    private const int MaxDataCodewords = 16;
    private const int WorkBufferSize = MaxDataCodewords + 8;

    /// <summary>
    /// Encodes <paramref name="text"/> into data codewords (padding included) and
    /// writes them to <paramref name="destination"/>.
    /// </summary>
    /// <param name="text">Input text; must satisfy the mode's alphabet and the version's capacity (validated by the caller).</param>
    /// <param name="version">Micro QR version (M1-M4).</param>
    /// <param name="eccLevel">Error correction level (valid for the version).</param>
    /// <param name="mode">Data encoding mode (Numeric / Alphanumeric / Byte).</param>
    /// <param name="destination">Destination for the data codewords.</param>
    /// <returns>Number of codeword bytes written (= data codeword count for the version/ECC).</returns>
    public static int EncodeDataCodewords(ReadOnlySpan<char> text, MicroQrVersion version, MicroQrEccLevel eccLevel, EncodingMode mode, Span<byte> destination)
    {
        var capacityBits = MicroQrConstants.GetDataBitCapacity(version, eccLevel);
        var codewordCount = MicroQrConstants.GetDataCodewordCount(version, eccLevel);
        Debug.Assert(capacityBits > 0, "invalid version/ECC combination must be rejected by the caller");

        Span<byte> work = stackalloc byte[WorkBufferSize];
        var writer = new BitWriter(work);

        // 1. Mode indicator (version - 1 bits; M1 carries numeric implicitly).
        var modeBits = MicroQrConstants.GetModeIndicatorLength(version);
        if (modeBits > 0)
        {
            writer.Write(MicroQrConstants.GetModeIndicatorValue(mode), modeBits);
        }

        // 2. Character count indicator + 3. data bits.
        var countBits = MicroQrConstants.GetCountIndicatorLength(version, mode);
        switch (mode)
        {
            case EncodingMode.Numeric:
                writer.Write(text.Length, countBits);
                WriteNumericData(ref writer, text);
                break;
            case EncodingMode.Alphanumeric:
                writer.Write(text.Length, countBits);
                WriteAlphanumericData(ref writer, text);
                break;
            case EncodingMode.Byte:
                WriteByteSegment(ref writer, text, countBits);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), $"Encoding mode {mode} is not supported by Micro QR.");
        }

        Debug.Assert(writer.BitPosition <= capacityBits, "capacity must be validated by the caller");

        // 4. Terminator (all-zero, shortened when the data reaches capacity).
        var terminatorBits = Math.Min(MicroQrConstants.GetTerminatorLength(version), capacityBits - writer.BitPosition);
        if (terminatorBits > 0)
        {
            writer.Write(0, terminatorBits);
        }

        // 5. Zero-fill the current byte. This also zeroes the forced-zero low
        // nibble when the data already reached into the final half codeword.
        var alignmentBits = (8 - writer.BitPosition % 8) % 8;
        if (alignmentBits > 0)
        {
            writer.Write(0, alignmentBits);
        }

        // 6. Alternating full pad codewords up to the last full data codeword.
        var fullCodewordBits = capacityBits / 8 * 8;
        var padIndex = 0;
        while (writer.BitPosition < fullCodewordBits)
        {
            writer.Write(padIndex++ % 2 == 0 ? 0xEC : 0x11, 8);
        }

        // 7. M1/M3: final 4-bit pad codeword is 0000 (high nibble; low nibble is
        // the forced zero filler), i.e. one full zero byte.
        while (writer.BitPosition < codewordCount * 8)
        {
            writer.Write(0, 8);
        }

        writer.Flush();
        work.Slice(0, codewordCount).CopyTo(destination);
        return codewordCount;
    }

    /// <summary>Numeric segment: 10/7/4 bits per 3/2/1 digits (ISO/IEC 18004 7.4.3).</summary>
    private static void WriteNumericData(ref BitWriter writer, ReadOnlySpan<char> digits)
    {
        var i = 0;
        for (; i + 2 < digits.Length; i += 3)
        {
            writer.Write((digits[i] - '0') * 100 + (digits[i + 1] - '0') * 10 + (digits[i + 2] - '0'), 10);
        }
        if (i + 1 < digits.Length)
        {
            writer.Write((digits[i] - '0') * 10 + (digits[i + 1] - '0'), 7);
        }
        else if (i < digits.Length)
        {
            writer.Write(digits[i] - '0', 4);
        }
    }

    /// <summary>Alphanumeric segment: 11 bits per pair, 6 bits for a trailing odd character.</summary>
    private static void WriteAlphanumericData(ref BitWriter writer, ReadOnlySpan<char> chars)
    {
        var i = 0;
        for (; i + 1 < chars.Length; i += 2)
        {
            writer.Write(CharacterSets.GetAlphanumericValue(chars[i]) * 45 + CharacterSets.GetAlphanumericValue(chars[i + 1]), 11);
        }
        if (i < chars.Length)
        {
            writer.Write(CharacterSets.GetAlphanumericValue(chars[i]), 6);
        }
    }

    /// <summary>
    /// Byte segment: count indicator counts encoded BYTES, then 8 bits per byte.
    /// Latin-1-representable text is narrowed per char; anything else is UTF-8.
    /// </summary>
    private static void WriteByteSegment(ref BitWriter writer, ReadOnlySpan<char> text, int countBits)
    {
        if (CharacterSets.IsValidISO88591(text))
        {
            writer.Write(text.Length, countBits);
            for (var i = 0; i < text.Length; i++)
            {
                writer.Write((byte)text[i], 8);
            }
            return;
        }

        // Micro QR byte capacity tops out at 15 bytes (M4-L), so the UTF-8 buffer
        // is always tiny; the caller has already validated the encoded length.
        Debug.Assert(text.Length <= 64, "byte payloads beyond any Micro QR capacity must be rejected by the caller");
        Span<byte> utf8 = stackalloc byte[256];
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        var byteCount = Encoding.UTF8.GetBytes(text, utf8);
#else
        var encoded = Encoding.UTF8.GetBytes(text.ToString());
        encoded.CopyTo(utf8);
        var byteCount = encoded.Length;
#endif
        writer.Write(byteCount, countBits);
        for (var i = 0; i < byteCount; i++)
        {
            writer.Write(utf8[i], 8);
        }
    }
}
