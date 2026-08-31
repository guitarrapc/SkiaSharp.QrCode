namespace SkiaSharp.QrCode;

/// <summary>
/// Compression mode for QR code data serialization.
/// </summary>
/// <remarks>
/// No API in this library accepts or returns this type. The serialization feature it
/// described was removed before 1.0.0; the enum was left behind and shipped in 1.1.1,
/// which is the only reason it still exists. Compress the bytes from
/// <c>GetRawData()</c> yourself, as shown in docs/migration.md.
/// </remarks>
[Obsolete("The serialization feature this enum described was removed before 1.0.0, and no API accepts or returns it. Compress the bytes from GetRawData() with the compressor of your choice instead, as shown in docs/migration.md. This type will be removed in 2.0.0.")]
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
