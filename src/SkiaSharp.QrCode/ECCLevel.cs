namespace SkiaSharp.QrCode;

/// <summary>
/// How much of a QR code can be damaged, dirty or covered and still scan.
/// Higher levels leave less room for content, so the same text may need a larger symbol.
/// </summary>
public enum ECCLevel
{
    /// <summary>
    /// 7% may be lost before recovery is not possible
    /// </summary>
    L,
    /// <summary>
    /// 15% may be lost before recovery is not possible
    /// </summary>
    M,
    /// <summary>
    /// 25% may be lost before recovery is not possible
    /// </summary>
    Q,
    /// <summary>
    /// 30% may be lost before recovery is not possible
    /// </summary>
    H
}
