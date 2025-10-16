#if NET5_0_OR_GREATER
#define SIMD_SUPPORTED
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
using System.Runtime.CompilerServices;
using System.Text;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Text analyzer for automatic encoding and ECI mode detection in single pass.
/// </summary>
internal static class TextAnalyzer
{
    private static readonly Encoding Iso88591Encoding = Encoding.GetEncoding("ISO-8859-1");

    /// <summary>
    /// Analyzes the input text to determine the most efficient encoding mode (Numeric, Alphanumeric, Byte)
    /// If SIMD is supported, uses SIMD instructions for faster analysis.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="requestedEciMode"></param>
    /// <returns></returns>
    public static (EncodingMode encoding, EciMode eciMode, int length) Analyze(ReadOnlySpan<char> text, EciMode requestedEciMode)
    {
        // TODO: Should change to Byte instead of Numeric.
        //
        // ISO/IEC 18004 does not define behavior for empty data.
        // However from other practical libraries, when data was empty EncodingMode should use Byte mode.
        // Empty data means no actual data to encode, so the difference in only in mode indicator bits (4 bits for Numeric/Alphanumeric vs 4+4 bits for Byte).
        //
        // Why not Numeric or Alphanumeric?
        // - If we use Numeric mode, empty data == no number => ❌ contradiction 
        // - If we use Alphanumeric mode, empty data == no character => ❌ contradiction
        // - If we use Byte mode, empty data == 0 length byte => ✔️ valid
        if (text.IsEmpty)
        {
            var actualEciMode = requestedEciMode == EciMode.Default ? EciMode.Default : requestedEciMode;
            return (EncodingMode.Numeric, actualEciMode, 0);
        }

#if SIMD_SUPPORTED
        // SIMD path for x86/x64 AVX2 support (16 chars at once)
        if (Avx2.IsSupported)
        {
            return AnalyzeAvx2(text, requestedEciMode);
        }

        // SIMD path for x86/x64 SSE2 support (8 chars at once)
        if (Sse2.IsSupported && text.Length >= 8)
        {
            return AnalyzeSse2(text, requestedEciMode);
        }
#endif

        // Scalr fallback for .NET Standard or short text
        return AnalyzeScalar(text, requestedEciMode);
    }

#if SIMD_SUPPORTED

    /// <summary>
    /// AVX2 optimized analysis (process 16 chars at once)
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (EncodingMode encoding, EciMode eciMode, int length) AnalyzeAvx2(ReadOnlySpan<char> text, EciMode requestedEciMode)
    {
        var hasNonNumeric = false;
        var hasNonAlphanumeric = false;
        var hasNonAscii = false;
        var hasNonIso88591 = false;

        var i = 0;
        var length = text.Length;

        // AVX2 proccessing 16 chars at once
        for (; i <= length - 16; i += 16)
        {
            // load 16 chars as Vector256<ushort>
            var chars = Vector256.Create(
                text[i], text[i + 1], text[i + 2], text[i + 3],
                text[i + 4], text[i + 5], text[i + 6], text[i + 7],
                text[i + 8], text[i + 9], text[i + 10], text[i + 11],
                text[i + 12], text[i + 13], text[i + 14], text[i + 15]
            );

            var charsInt16 = chars.AsInt16();

            // ASCII check (0-127)
            if (!hasNonAscii)
            {
                var ascii127 = Vector256.Create((short)127);
                var asciiMask = Avx2.CompareGreaterThan(charsInt16, ascii127);
                if (!Avx2.TestZ(asciiMask, asciiMask))
                {
                    hasNonAscii = true;
                }
            }

            // ISO-8859-1 check (0-255)
            if (!hasNonIso88591 && hasNonAscii)
            {
                var iso255 = Vector256.Create((short)255);
                var isoMask = Avx2.CompareGreaterThan(charsInt16, iso255);
                if (!Avx2.TestZ(isoMask, isoMask))
                {
                    hasNonIso88591 = true;
                }
            }

            // Numeric check (0-9)
            if (!hasNonNumeric)
            {
                var char0 = Vector256.Create((short)'0');
                var char9 = Vector256.Create((short)'9');
                
                var lessThan0 = Avx2.CompareGreaterThan(char0, charsInt16);
                var greaterThan9 = Avx2.CompareGreaterThan(charsInt16, char9);
                var nonNumericMask = Avx2.Or(lessThan0, greaterThan9);

                if (!Avx2.TestZ(nonNumericMask, nonNumericMask))
                {
                    hasNonNumeric = true;
                }
            }

            // Alphanumeric check (require scalar processing due to complexity)
            if (!hasNonAlphanumeric && hasNonNumeric)
            {
                if (!IsAllAlphanumericAvx2(charsInt16))
                {
                    hasNonAlphanumeric = true;
                }
            }

            // Early exit if all types are found
            if (hasNonNumeric && hasNonAlphanumeric && hasNonIso88591)
                break;
        }

        // Process remaining chars with scalar fallback
        for (; i < length; i++)
        {
            var c = text[i];

            if (!hasNonAscii && c > 127)
                hasNonAscii = true;

            if (!hasNonIso88591 && c > 255)
                hasNonIso88591 = true;

            if ((!hasNonNumeric && !QRCodeConstants.IsNumeric(c)))
                hasNonNumeric = true;

            if (!hasNonAlphanumeric && !QRCodeConstants.IsAlphanumeric(c))
                hasNonAlphanumeric = true;

            if (hasNonNumeric && hasNonAlphanumeric && hasNonIso88591)
                break;
        }

        var encoding = DetermineEncoding(hasNonNumeric, hasNonAlphanumeric);

        // If there are user constraints (e.g. requestedVersion), calculate actual data length.
        var actualEciMode = requestedEciMode == EciMode.Default
            ? DetermineEciMode(hasNonAscii, hasNonIso88591)
            : requestedEciMode;

        var dataLength = CalculateLength(text, encoding, actualEciMode);

        return (encoding, actualEciMode, dataLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAllAlphanumericAvx2(Vector256<short> chars)
    {
        // Digits: 0-9
        var char0 = Vector256.Create((short)'0');
        var char9 = Vector256.Create((short)'9');
        var lessThan0 = Avx2.CompareGreaterThan(char0, chars);
        var greaterThan9 = Avx2.CompareGreaterThan(chars, char9);
        var notInDigitRange = Avx2.Or(lessThan0, greaterThan9);
        var isDigit = Avx2.AndNot(notInDigitRange, Vector256.Create((short)-1));

        // Uppercase letters: A-Z
        var charA = Vector256.Create((short)'A');
        var charZ = Vector256.Create((short)'Z');
        var lessThanA = Avx2.CompareGreaterThan(charA, chars);
        var greaterThanZ = Avx2.CompareGreaterThan(chars, charZ);
        var notInUpperRange = Avx2.Or(lessThanA, greaterThanZ);
        var isUpper = Avx2.AndNot(notInUpperRange, Vector256.Create((short)-1));

        // Special characters: space, $, %, *, +, -, ., /, :
        var space = Avx2.CompareEqual(chars, Vector256.Create((short)' ')); // 0x20
        var dollar = Avx2.CompareEqual(chars, Vector256.Create((short)'$')); // 0x24
        var percent = Avx2.CompareEqual(chars, Vector256.Create((short)'%')); // 0x25
        var asterisk = Avx2.CompareEqual(chars, Vector256.Create((short)'*')); // 0x2A
        var plus = Avx2.CompareEqual(chars, Vector256.Create((short)'+')); // 0x2B
        var minus = Avx2.CompareEqual(chars, Vector256.Create((short)'-')); // 0x2D
        var period = Avx2.CompareEqual(chars, Vector256.Create((short)'.')); // 0x2E
        var slash = Avx2.CompareEqual(chars, Vector256.Create((short)'/')); // 0x2F
        var colon = Avx2.CompareEqual(chars, Vector256.Create((short)':')); // 0x3A

        var isSpecial = Avx2.Or(
            Avx2.Or(Avx2.Or(space, dollar), Avx2.Or(percent, asterisk)),
            Avx2.Or(Avx2.Or(plus, minus), Avx2.Or(Avx2.Or(period, slash), colon))
        );

        // combine all checks
        var isValid = Avx2.Or(Avx2.Or(isDigit, isUpper), isSpecial);

        // Check all bits are flaged.
        var allOnes = Vector256.Create((short)-1);
        var mask = Avx2.CompareEqual(isValid, allOnes);

        // All 32bytes must be 0xFF, so MoveMask must return 0xFFFFFFFF
        return Avx2.MoveMask(mask.AsByte()) == -1; // 0xFFFFFFFF
    }

    /// <summary>
    /// SSE2 optimized analysis (process 8 chars at once)
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (EncodingMode encoding, EciMode eciMode, int length) AnalyzeSse2(ReadOnlySpan<char> text, EciMode requestedEciMode)
    {
        var hasNonNumeric = false;
        var hasNonAlphanumeric = false;
        var hasNonAscii = false;
        var hasNonIso88591 = false;

        var i = 0;
        var length = text.Length;

        // AVX2 proccessing 16 chars at once
        for (; i <= length - 8; i += 8)
        {
            // load 16 chars as Vector128<ushort>
            var chars = Vector128.Create(
                text[i], text[i + 1], text[i + 2], text[i + 3],
                text[i + 4], text[i + 5], text[i + 6], text[i + 7]
            );

            var charsInt16 = chars.AsInt16();

            // ASCII check (0-127)
            if (!hasNonAscii)
            {
                var ascii127 = Vector128.Create((short)127);
                var asciiMask = Sse2.CompareGreaterThan(charsInt16, ascii127);
                if (Sse2.MoveMask(asciiMask.AsByte()) != 0)
                {
                    hasNonAscii = true;
                }
            }

            // ISO-8859-1 check (0-255)
            if (!hasNonIso88591 && hasNonAscii)
            {
                var iso255 = Vector128.Create((short)255);
                var isoMask = Sse2.CompareGreaterThan(charsInt16, iso255);
                if (Sse2.MoveMask(isoMask.AsByte()) != 0)
                {
                    hasNonIso88591 = true;
                }
            }

            // Numeric check (0-9)
            if (!hasNonNumeric)
            {
                var char0 = Vector128.Create((short)'0');
                var char9 = Vector128.Create((short)'9');

                var lessThan0 = Sse2.CompareLessThan(charsInt16, char0);
                var greaterThan9 = Sse2.CompareGreaterThan(charsInt16, char9);
                var nonNumericMask = Sse2.Or(lessThan0, greaterThan9);

                if (Sse2.MoveMask(nonNumericMask.AsByte()) != 0)
                {
                    hasNonNumeric = true;
                }
            }

            // Alphanumeric check (require scalar processing due to complexity)
            if (!hasNonAlphanumeric && hasNonNumeric)
            {
                if (!IsAllAlphanumericSse2(charsInt16))
                {
                    hasNonAlphanumeric = true;
                }
            }

            // Early exit if all types are found
            if (hasNonNumeric && hasNonAlphanumeric && hasNonIso88591)
                break;
        }

        // Process remaining chars with scalar fallback
        for (; i < length; i++)
        {
            var c = text[i];

            if (!hasNonAscii && c > 127)
                hasNonAscii = true;

            if (!hasNonIso88591 && c > 255)
                hasNonIso88591 = true;

            if ((!hasNonNumeric && !QRCodeConstants.IsNumeric(c)))
                hasNonNumeric = true;

            if (!hasNonAlphanumeric && !QRCodeConstants.IsAlphanumeric(c))
                hasNonAlphanumeric = true;

            if (hasNonNumeric && hasNonAlphanumeric && hasNonIso88591)
                break;
        }

        var encoding = DetermineEncoding(hasNonNumeric, hasNonAlphanumeric);

        // If there are user constraints (e.g. requestedVersion), calculate actual data length.
        var actualEciMode = requestedEciMode == EciMode.Default
            ? DetermineEciMode(hasNonAscii, hasNonIso88591)
            : requestedEciMode;

        var dataLength = CalculateLength(text, encoding, actualEciMode);

        return (encoding, actualEciMode, dataLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAllAlphanumericSse2(Vector128<short> chars)
    {
        // Digits: 0-9
        var char0 = Vector128.Create((short)'0');
        var char9 = Vector128.Create((short)'9');
        var lessThan0 = Sse2.CompareGreaterThan(char0, chars);
        var greaterThan9 = Sse2.CompareGreaterThan(chars, char9);
        var notInDigitRange = Sse2.Or(lessThan0, greaterThan9);
        var isDigit = Sse2.AndNot(notInDigitRange, Vector128.Create((short)-1));

        // Uppercase letters: A-Z
        var charA = Vector128.Create((short)'A');
        var charZ = Vector128.Create((short)'Z');
        var lessThanA = Sse2.CompareGreaterThan(charA, chars);
        var greaterThanZ = Sse2.CompareGreaterThan(chars, charZ);
        var notInUpperRange = Sse2.Or(lessThanA, greaterThanZ);
        var isUpper = Sse2.AndNot(notInUpperRange, Vector128.Create((short)-1));

        // Special characters: space, $, %, *, +, -, ., /, :
        var space = Sse2.CompareEqual(chars, Vector128.Create((short)' ')); // 0x20
        var dollar = Sse2.CompareEqual(chars, Vector128.Create((short)'$')); // 0x24
        var percent = Sse2.CompareEqual(chars, Vector128.Create((short)'%')); // 0x25
        var asterisk = Sse2.CompareEqual(chars, Vector128.Create((short)'*')); // 0x2A
        var plus = Sse2.CompareEqual(chars, Vector128.Create((short)'+')); // 0x2B
        var minus = Sse2.CompareEqual(chars, Vector128.Create((short)'-')); // 0x2D
        var period = Sse2.CompareEqual(chars, Vector128.Create((short)'.')); // 0x2E
        var slash = Sse2.CompareEqual(chars, Vector128.Create((short)'/')); // 0x2F
        var colon = Sse2.CompareEqual(chars, Vector128.Create((short)':')); // 0x3A

        var isSpecial = Sse2.Or(
            Sse2.Or(Sse2.Or(space, dollar), Sse2.Or(percent, asterisk)),
            Sse2.Or(Sse2.Or(plus, minus), Sse2.Or(Sse2.Or(period, slash), colon))
        );

        // combine all checks
        var isValid = Sse2.Or(Sse2.Or(isDigit, isUpper), isSpecial);

        // Check all bits are flaged.
        var allOnes = Vector128.Create((short)-1);
        var allValid = Sse2.CompareEqual(isValid, allOnes);

        // All 16bytes must be 0xFF, so MoveMask must return 0xFFFF
        return Sse2.MoveMask(allValid.AsByte()) == 0xFFFF;
    }
#endif

    /// <summary>
    /// Scalar fallback analysis (process 1 char at once)
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (EncodingMode encoding, EciMode eciMode, int length) AnalyzeScalar(ReadOnlySpan<char> text, EciMode requestedEciMode)
    {
        var hasNonNumeric = false;
        var hasNonAlphanumeric = false;
        var hasNonAscii = false;
        var hasNonIso88591 = false;

        foreach (var c in text)
        {
            if (!hasNonAscii && c > 127)
                hasNonAscii = true;

            if (!hasNonIso88591 && c > 255)
                hasNonIso88591 = true;

            if (!hasNonNumeric && !QRCodeConstants.IsNumeric(c))
                hasNonNumeric = true;

            if (!hasNonAlphanumeric && !QRCodeConstants.IsAlphanumeric(c))
                hasNonAlphanumeric = true;

            // Early exit if all types are found
            if (hasNonNumeric && hasNonAlphanumeric && hasNonIso88591)
                break;
        }

        var encoding = DetermineEncoding(hasNonNumeric, hasNonAlphanumeric);

        // If there are user constraints (e.g. requestedVersion), calculate actual data length.
        var actualEciMode = requestedEciMode == EciMode.Default
            ? DetermineEciMode(hasNonAscii, hasNonIso88591)
            : requestedEciMode;

        var dataLength = CalculateLength(text, encoding, actualEciMode);

        return (encoding, actualEciMode, dataLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static EncodingMode DetermineEncoding(bool hasNonNumeric, bool hasNonAlphanumeric)
    {
        if (!hasNonNumeric)
            return EncodingMode.Numeric;
        if (!hasNonAlphanumeric)
            return EncodingMode.Alphanumeric;
        return EncodingMode.Byte;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static EciMode DetermineEciMode(bool hasNonAscii, bool hasNonIso88591)
    {
        if (!hasNonAscii)
            return EciMode.Default;
        if (!hasNonIso88591)
            return EciMode.Iso8859_1;
        return EciMode.Utf8;
    }

    private static int CalculateLength(ReadOnlySpan<char> text, EncodingMode encoding, EciMode eciMode)
    {
        return encoding switch
        {
            EncodingMode.Numeric => text.Length,
            EncodingMode.Alphanumeric => text.Length,
            EncodingMode.Byte => CalculateByteCount(text, eciMode),
            _ => text.Length
        };
    }

    private static int CalculateByteCount(ReadOnlySpan<char> text, EciMode eciMode)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        ReadOnlySpan<char> input = text;
#else
        string input = text.ToString();
#endif
        // ISO-8859-x encoding based on ECI mode
        return eciMode switch
        {
            EciMode.Default => QRCodeConstants.IsValidISO88591(input)
                ? Iso88591Encoding.GetByteCount(input)
                : Encoding.UTF8.GetByteCount(input),
            EciMode.Iso8859_1 => Iso88591Encoding.GetByteCount(input),
            EciMode.Utf8 => Encoding.UTF8.GetByteCount(input),
            _ => throw new ArgumentOutOfRangeException(nameof(eciMode), "Unsupported ECI mode for Byte encoding"),
        };
    }
}
