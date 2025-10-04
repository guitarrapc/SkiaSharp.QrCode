namespace SkiaSharp.QrCode;

/// <summary>
/// ECI (Extended Channel Interpretation) mode for character encoding.
/// </summary>
public enum EciMode
{
    /// <summary>
    /// Auto-detect (ISO-8859-1 or UTF-8)
    /// </summary>
    Default = 0,
    /// <summary>
    /// Western European
    /// </summary>
    Iso8859_1 = 3,
    /// <summary>
    /// Central European
    /// </summary>
    Iso8859_2 = 4,
    /// <summary>
    /// Unicode UTF-8
    /// </summary>
    Utf8 = 26
}

