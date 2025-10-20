namespace SkiaSharp.QrCode;

/// <summary>
/// Calculated QR code size information.
/// </summary>
/// <param name="BufferSize">Required buffer size for the QR code matrix data (in bytes). Calculated as QrSize × QrSize.</param>
/// <param name="QrSize">QR code size in modules per side (including quiet zone if specified)</param>
/// <param name="Version">QR code version (1-40) determined by data capacity requirements</param>
public readonly record struct QRCodeCalculatedSize(int BufferSize, int QrSize, int Version)
{
    /// <summary>
    /// Validates that the calculated size values are within acceptable ranges.
    /// </summary>
    public bool IsValid => Version is >= 1 and <= 40 && QrSize > 0 && BufferSize > 0;
}
