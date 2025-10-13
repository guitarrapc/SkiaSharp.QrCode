using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Image;

public class QrCode
{
    private readonly string _content;
    private readonly SKImageInfo _qrInfo;
    private readonly SKEncodedImageFormat _outputFormat = SKEncodedImageFormat.Png;
    private readonly int _quality = 100;

    public QrCode(string content, Vector2Slim qrSize)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));
        if (qrSize.X <= 0 || qrSize.Y <= 0)
            throw new ArgumentException("QR size must be positive", nameof(qrSize));

        (_content, _qrInfo) = (content, new SKImageInfo(qrSize.X, qrSize.Y));
    }

    public QrCode(string content, Vector2Slim qrSize, SKEncodedImageFormat outputFormat) : this(content, qrSize)
        => _outputFormat = outputFormat;

    public QrCode(string content, Vector2Slim qrSize, SKEncodedImageFormat outputFormat, int quality) : this(content, qrSize, outputFormat)
    {
        if (quality is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 0 and 100");
        _quality = quality;
    }

    /// <summary>
    /// Generate QR Code and output to stream
    /// </summary>
    /// <param name="outputImage"></param>
    public void GenerateImage(Stream outputImage, bool resetStreamPosition = true, ECCLevel eccLevel = ECCLevel.L)
    {
        if (outputImage is null)
            throw new ArgumentNullException(nameof(outputImage));
        if (!outputImage.CanWrite)
            throw new ArgumentException("Output stream must be writable", nameof(outputImage));

        ResetStreamIfNeeded(outputImage, resetStreamPosition);

        using var qrImage = CreateQrImage(eccLevel);
        Save(qrImage, outputImage);
    }

    /// <summary>
    /// Generate QR Code and combine with base image, then output to stream
    /// </summary>
    /// <param name="outputImage"></param>
    /// <param name="baseImage"></param>
    /// <param name="baseQrSize"></param>
    /// <param name="qrPosition"></param>
    public void GenerateImage(Stream outputImage, Stream baseImage, Vector2Slim baseQrSize, Vector2Slim qrPosition, bool resetStreamPosition = true, ECCLevel eccLevel = ECCLevel.L)
    {
        if (outputImage is null)
            throw new ArgumentNullException(nameof(outputImage));
        if (baseImage is null)
            throw new ArgumentNullException(nameof(baseImage));
        if (!outputImage.CanWrite)
            throw new ArgumentException("Output stream must be writable", nameof(outputImage));

        ResetStreamIfNeeded(outputImage, resetStreamPosition);
        ResetStreamIfNeeded(baseImage, resetStreamPosition);

        using var qrImage = CreateQrImage(eccLevel);
        using var baseBitmap = SKBitmap.Decode(baseImage) ?? throw new InvalidOperationException("Failed to decode base image");
        SaveCombinedImage(qrImage, baseBitmap, baseQrSize, qrPosition, outputImage);
    }

    /// <summary>
    /// Generate QR Code and combine with base image, then output to stream
    /// </summary>
    /// <param name="outputImage"></param>
    /// <param name="baseImage"></param>
    /// <param name="baseQrSize"></param>
    /// <param name="qrPosition"></param>
    public void GenerateImage(Stream outputImage, byte[] baseImage, Vector2Slim baseQrSize, Vector2Slim qrPosition, bool resetStreamPosition = true, ECCLevel eccLevel = ECCLevel.L)
    {
        if (outputImage is null)
            throw new ArgumentNullException(nameof(outputImage));
        if (baseImage is null || baseImage.Length == 0)
            throw new ArgumentNullException(nameof(baseImage));
        if (!outputImage.CanWrite)
            throw new ArgumentException("Output stream must be writable", nameof(outputImage));

        ResetStreamIfNeeded(outputImage, resetStreamPosition);

        using var qrImage = CreateQrImage(eccLevel);
        using var baseBitmap = SKBitmap.Decode(baseImage) ?? throw new InvalidOperationException("Failed to decode base image");
        SaveCombinedImage(qrImage, baseBitmap, baseQrSize, qrPosition, outputImage);
    }

    /// <summary>
    /// Create QR image
    /// </summary>
    /// <param name="eccLevel"></param>
    /// <returns></returns>
    private SKImage CreateQrImage(ECCLevel eccLevel)
    {
        var qrCodeData = QRCodeGenerator.CreateQrCode(_content, eccLevel);

        using var qrSurface = SKSurface.Create(_qrInfo);
        var qrCanvas = qrSurface.Canvas;
        qrCanvas.Render(qrCodeData, _qrInfo.Width, _qrInfo.Height);

        // Do not use 'using' - caller must dispose the returned image
        var qrImage = qrSurface.Snapshot();
        return qrImage;
    }

    /// <summary>
    /// Save QR image to stream
    /// </summary>
    /// <param name="qrImage"></param>
    /// <param name="outputImage"></param>
    private void Save(SKImage qrImage, Stream outputImage)
    {
        using var data = qrImage.Encode(_outputFormat, _quality);
        data.SaveTo(outputImage);
    }

    /// <summary>
    /// Save combined image to stream
    /// </summary>
    /// <param name="qrImage"></param>
    /// <param name="baseBitmap"></param>
    /// <param name="baseImageSize"></param>
    /// <param name="qrPosition"></param>
    /// <param name="output"></param>
    private void SaveCombinedImage(SKImage qrImage, SKBitmap baseBitmap, Vector2Slim baseImageSize, Vector2Slim qrPosition, Stream output)
    {
        var baseInfo = new SKImageInfo(baseImageSize.X, baseImageSize.Y);
        using var baseSurface = SKSurface.Create(baseInfo);
        var baseCanvas = baseSurface.Canvas;

        // combine with base image
        baseCanvas.DrawBitmap(baseBitmap, 0, 0);
        baseCanvas.DrawImage(qrImage, qrPosition.X, qrPosition.Y);

        using var image = baseSurface.Snapshot();
        using var data = image.Encode(_outputFormat, _quality);
        data.SaveTo(output);
    }

    /// <summary>
    /// Reset stream position if needed
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="reset"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResetStreamIfNeeded(Stream stream, bool reset)
    {
        if (reset && stream.CanSeek)
            stream.Seek(0, SeekOrigin.Begin);
    }
}
