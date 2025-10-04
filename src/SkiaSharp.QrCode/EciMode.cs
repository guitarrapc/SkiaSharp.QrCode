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
    // TODO: Drop Iso8859_2 support. As .NET Standard 2.0/2.1 does not support it by default.
    /// <summary>
    /// ISO-8859-2 (Latin-2) - Central European
    /// If your data is Central European, this will be the most efficient.
    /// Only limited QR reader support this encoding, you may better use UTF-8 instead.
    /// </summary>
    Iso8859_2 = 4,
    /// <summary>
    /// UTF-8 Unicode.
    /// If your input is UTF-8, emoji or other multibyte characters, you should use this.
    /// </summary>
    Utf8 = 26
}
