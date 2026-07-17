namespace SkiaSharp.QrCode;

/// <summary>
/// Result of <see cref="MicroQrCodeGenerator.GetRequiredBufferSize"/>: buffer size,
/// matrix side length and selected version for a pending Micro QR encode.
/// </summary>
public readonly struct MicroQrCodeCalculatedSize
{
    internal MicroQrCodeCalculatedSize(int bufferSize, int qrSize, MicroQrVersion version)
    {
        BufferSize = bufferSize;
        QrSize = qrSize;
        Version = version;
    }

    /// <summary>Required destination buffer size in bytes (one byte per module, quiet zone included).</summary>
    public int BufferSize { get; }

    /// <summary>Matrix side length in modules, quiet zone included.</summary>
    public int QrSize { get; }

    /// <summary>The Micro QR version that will be produced.</summary>
    public MicroQrVersion Version { get; }
}
