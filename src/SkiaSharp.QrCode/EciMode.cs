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
    /// Auto-detect encoding and add appropriate ECI header (recommended for most use cases).
    /// </summary>
    /// <remarks>
    /// <para>Automatic Encoding Detection</para>
    /// <list type="bullet">
    /// <item>ASCII-only (0x00-0x7F): No ECI header (maximum compatibility, smallest size)</item>
    /// <item>ISO-8859-1 compatible: Auto-upgrade to <see cref="Iso8859_1"/> (ECI 3 header added)</item>
    /// <item>Unicode (emojis, CJK, etc.): Auto-upgrade to <see cref="Utf8"/> (ECI 26 header added)</item>
    /// </list>
    /// 
    /// <para>Examples:</para>
    /// <code>
    /// "HELLO"     → No ECI header (ASCII-only, 29 bits for "HE")
    /// "Café"      → ECI 3 (ISO-8859-1, 41 bits for "Ca")
    /// "🎉"        → ECI 26 (UTF-8, 41 bits + UTF-8 bytes)
    /// "こんにちは"  → ECI 26 (UTF-8, auto-detected)
    /// </code>
    /// </remarks>
    Default = 0,
    /// <summary>
    /// ISO-8859-1 (Latin-1) encoding - Western European characters.
    /// Adds an ECI header: 12 bits in Standard QR, 11 bits in rMQR.
    /// </summary>
    /// <remarks>
    /// <para>Character Support:</para>
    /// <list type="bullet">
    /// <item>ASCII (0x00-0x7F): A-Z, 0-9, basic symbols</item>
    /// <item>Extended Latin (0x80-0xFF): À, Ç, Ñ, é, ü, etc.</item>
    /// </list>
    /// 
    /// <para>Use when:</para>
    /// <list type="bullet">
    /// <item>Content is Western European languages (English, French, Spanish, German, etc.)</item>
    /// <item>You need explicit ISO-8859-1 encoding declaration</item>
    /// <item>Compatibility with ISO-8859-1 readers is required</item>
    /// </list>
    /// 
    /// <para>Cannot encode:</para>
    /// <list type="bullet">
    /// <item>Emojis (🎉, 😀, etc.)</item>
    /// <item>CJK characters (日本語, 中文, 한글)</item>
    /// <item>Cyrillic beyond basic range</item>
    /// </list>
    /// </remarks>
    Iso8859_1 = 3,
    /// <summary>
    /// UTF-8 Unicode encoding - Universal character support.
    /// Adds an ECI header: 12 bits in Standard QR, 11 bits in rMQR.
    /// </summary>
    /// <remarks>
    /// <para>Character Support:</para>
    /// <list type="bullet">
    /// <item>All Unicode characters (U+0000 to U+10FFFF)</item>
    /// <item>Emojis, CJK, Arabic, Hebrew, Cyrillic, etc.</item>
    /// <item>Multi-byte encoding: 1-4 bytes per character</item>
    /// </list>
    /// 
    /// <para>Size Impact:</para>
    /// <list type="bullet">
    /// <item>ECI header: +12 bits in Standard QR, +11 bits in rMQR</item>
    /// <item>Data encoding: Variable (1-4 bytes per character)</item>
    /// <item>Standard QR example: "🎉" = 12 (header) + 4 + 9 + 32 (4-byte UTF-8) = 57 bits</item>
    /// <item>Standard QR example: "Café" = 12 (header) + 4 + 9 + 40 (5 bytes: C,a,f,é=C3A9) = 65 bits</item>
    /// </list>
    /// 
    /// <para>UTF-8 Byte Examples:</para>
    /// <code>
    /// 'A'  → 0x41           (1 byte,  ASCII)
    /// 'é'  → 0xC3 0xA9      (2 bytes, Latin Extended)
    /// '中' → 0xE4 0xB8 0xAD (3 bytes, CJK)
    /// '🎉' → 0xF0 0x9F 0x8E 0x89 (4 bytes, Emoji)
    /// </code>
    /// 
    /// <para>Use when:</para>
    /// <list type="bullet">
    /// <item>Content includes emojis or symbols</item>
    /// <item>Content includes CJK characters (Japanese, Chinese, Korean)</item>
    /// <item>Content includes non-Latin scripts (Cyrillic, Arabic, Hebrew, etc.)</item>
    /// <item>You need universal Unicode support</item>
    /// </list>
    /// 
    /// <para>Trade-offs:</para>
    /// <list type="bullet">
    /// <item>Larger data size for non-ASCII characters (multi-byte encoding)</item>
    /// <item>ECI header adds 12 bits in Standard QR or 11 bits in rMQR</item>
    /// <item>For Western European text, <see cref="Iso8859_1"/> is more efficient</item>
    /// </list>
    /// </remarks>
    Utf8 = 26
}

/// <summary>
/// Extension methods for EciMode enum.
/// </summary>
internal static class EciModeExtensions
{
    // ECI Header Structure (ISO/IEC 18004):
    // ┌─────────────────┬────────────────────────┐
    // │ ECI Indicator   │ ECI Assignment Number  │
    // │ 0111 (4 bits)   │ Variable (8-24 bits)   │
    // └─────────────────┴────────────────────────┘

    /// <summary>
    /// Gets the Standard QR ECI header size in bits.
    /// </summary>
    /// <param name="eciMode">ECI mode.</param>
    /// <returns>
    /// Header size in bits:
    /// - Default (no ECI): 0 bits
    /// - With ECI: 4 bits (ECI indicator) + 8 bits (assignment number) = 12 bits
    /// </returns>
    /// <remarks>
    /// Current implementation supports 0-127 range (8 bits).
    /// </remarks>
    internal static int GetStandardQrHeaderBits(this EciMode eciMode)
    {
        if (eciMode == EciMode.Default)
        {
            return 0; // No ECI header
        }

        // ECI mode indicator (4 bits) + ECI assignment number (8 bits for 0-127)
        return 4 + 8;
    }
}
