using System;
using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Encoding mode enumeration (ISO/IEC 18004 Section 7.4.1).
/// Determines how data is encoded in the QR code, affecting capacity and efficiency.
/// 
/// Mode priority in automatic detection:
/// 1. Numeric (most efficient)
/// 2. Alphanumeric
/// 3. Byte (least efficient)
/// 
/// Note: ECI is not a data encoding mode, but a character set declaration.
/// </summary>
internal enum EncodingMode
{
    /// <summary>
    /// 0-9 only (10 bits per 3 digits)
    /// </summary>
    Numeric = 1,
    /// <summary>
    /// 0-9, A-Z, space, $%*+-./:  (11 bits per 2 chars).
    /// </summary>
    Alphanumeric = 2,
    /// <summary>
    /// Any 8-bit data (8 bits per byte)
    /// Default: ISO-8859-1, can be UTF-8 with ECI.
    /// </summary>
    Byte = 4,
    /// <summary>
    /// Extended Channel Interpretation (metadata only).
    /// Mode indicator: 0111 + 8-bit assignment number
    /// Specifies character encoding for Byte mode:
    ///   - ECI 3: ISO-8859-1
    ///   - ECI 4: ISO-8859-2
    ///   - ECI 26: UTF-8
    /// Always followed by another mode (typically Byte).
    /// </summary>
    ECI = 7,
    /// <summary>
    /// Shift JIS Kanji (13 bits per character)
    /// </summary>
    Kanji = 8,
}

internal static class EncodingModeExtensions
{
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
    public static int GetCountIndicatorLength(this EncodingMode mode, int version)
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
}
