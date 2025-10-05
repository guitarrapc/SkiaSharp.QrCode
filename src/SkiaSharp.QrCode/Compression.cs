namespace SkiaSharp.QrCode;

/// <summary>
/// Compression mode for QR code data serialization.
/// </summary>
public enum Compression
{
    /// <summary>
    /// No compression
    /// </summary>
    Uncompressed,
    /// <summary>
    /// DEFLATE compression (RFC 1951)
    /// </summary>
    Deflate,
    /// <summary>
    /// GZIP compression (RFC 1952)
    /// </summary>
    GZip
}
