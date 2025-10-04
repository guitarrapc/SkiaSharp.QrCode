namespace SkiaSharp.QrCode;

/// <summary>
/// ECI (Extended Channel Interpretation) mode for character encoding.
/// </summary>
public enum EciMode
{
    // ECI header makes QR mask pattern difference, due to bit length difference.
    //
    // # Data structure:
    // When ASCII text "AB" was passed... 12 bits difference for ECI header.
    //
    // EciMode.Default
    // ┌──────────┬────────────────┬──────────┐
    // │ Mode(4b) │ Count(9b)      │ Data     │
    // │ 0100     │ 000000010      │ ...      │
    // └──────────┴────────────────┴──────────┘
    // 4 + 9 + (2 * 8) = 29 bits
    //
    // EciMode.Iso8859_1
    // ┌──────────┬────────────┬──────────┬────────────────┬──────────┐
    // │ ECI(4b)  │ Value(8b)  │ Mode(4b) │ Count(9b)      │ Data     │
    // │ 0111     │ 00000100   │ 0100     │ 000000010      │ ...      │
    // └──────────┴────────────┴──────────┴────────────────┴──────────┘
    // 4 + 8 + 4 + 9 + (2 * 8) = 41 bits
    //
    // # Effects:
    // 1. Padding
    // public void WritePadding(int targetBitCount)
    // {
    //     var remaining = targetBitCount - _builder.Length; // <- - different due to ECI header
    //     ....
    // }
    //
    // 2. ECC
    // Data difference -> Reed Solomon ECC calculation difference -> Data after interleaving is different.
    //
    // 3. Mask pattern
    // Data difference -> Optimal mask pattern may be different -> Final QR code pattern is different.

    /// <summary>
    /// No ECI header (decoder-dependent interpretation).
    /// Not recommended for non-numeric/alphanumeric input.
    /// </summary>
    Default = 0,
    /// <summary>
    /// ISO-8859-1 (Latin-1) - Western European.
    /// If your data is ASCII/Latin-1, this will be the most efficient.
    /// </summary>
    Iso8859_1 = 3,
    /// <summary>
    /// UTF-8 Unicode.
    /// If your input is UTF-8, emoji or other multibyte characters, you should use this.
    /// </summary>
    Utf8 = 26
}

/// <summary>
/// Extension methods for EciMode enum.
/// </summary>
public static class EciModeExtensions
{
    /// <summary>
    /// Gets the ECI header size in bits.
    /// </summary>
    /// <param name="eciMode">ECI mode.</param>
    /// <returns>
    /// Header size in bits:
    /// - Default (no ECI): 0 bits
    /// - With ECI: 4 bits (ECI indicator) + 8 bits (assignment number) = 12 bits
    /// </returns>
    /// <remarks>
    /// ECI Header Structure (ISO/IEC 18004):
    /// ┌─────────────────┬────────────────────────┐
    /// │ ECI Indicator   │ ECI Assignment Number  │
    /// │ 0111 (4 bits)   │ Variable (8-24 bits)   │
    /// └─────────────────┴────────────────────────┘
    /// 
    /// Current implementation supports 0-127 range (8 bits).
    /// </remarks>
    internal static int GetHeaderBits(this EciMode eciMode)
    {
        if (eciMode == EciMode.Default)
        {
            return 0; // No ECI header
        }

        // ECI mode indicator (4 bits) + ECI assignment number (8 bits for 0-127)
        return 4 + 8;
    }

    /// <summary>
    /// Gets the ECI header size in bytes (rounded up).
    /// </summary>
    /// <param name="eciMode">ECI mode.</param>
    /// <returns>Header size in bytes.</returns>
    internal static int GetHeaderBytes(this EciMode eciMode)
    {
        var bits = eciMode.GetHeaderBits();
        return (bits + 7) / 8; // Round up to nearest byte
    }
}
