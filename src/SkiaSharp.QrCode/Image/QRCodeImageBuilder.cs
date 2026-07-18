using System.Buffers;

namespace SkiaSharp.QrCode.Image;

/// <summary>
/// High-level builder for creating QR code images with fluent configuration and static methods.
/// </summary>
/// <remarks>
/// <para>
/// This builder provides both simple static methods for quick QR code generation
/// and a fluent API for advanced customization.
/// </para>
/// <para>
/// <b>Quick Generation (Static Methods):</b><br/>
/// Use static methods like <see cref="GetPngBytes(string, ECCLevel, int)"/> for one-liner QR code creation with default settings.
/// </para>
/// <para>
/// <b>Advanced Configuration (Fluent API):</b><br/>
/// Chain the shared options (<see cref="QRCodeImageBuilderBase{TSelf}.WithSize(int, int)"/>,
/// <see cref="QRCodeImageBuilderBase{TSelf}.WithModulePixelSize(int)"/>,
/// <see cref="QRCodeImageBuilderBase{TSelf}.WithColors(SKColor?, SKColor?, SKColor?)"/>,
/// <see cref="QRCodeImageBuilderBase{TSelf}.WithModuleShape(ModuleShape?, float)"/>,
/// <see cref="QRCodeImageBuilderBase{TSelf}.WithGradient(GradientOptions?)"/>)
/// with the Standard QR-specific options (<see cref="WithErrorCorrection(ECCLevel)"/>,
/// <see cref="WithVersion(int)"/>, <see cref="WithIcon(IconData?)"/>,
/// <see cref="WithFinderPatternShape(FinderPatternShape?)"/>) to customize appearance.
/// </para>
/// </remarks>
/// <seealso cref="QRCodeGenerator"/>
/// <seealso cref="QRCodeRenderer"/>
/// <seealso cref="MicroQRCodeImageBuilder"/>
public class QRCodeImageBuilder : QRCodeImageBuilderBase<QRCodeImageBuilder>
{
    private readonly string? _content;
    private readonly QRCodeData? _qrCodeData;
    private ECCLevel _eccLevel = ECCLevel.M;
    private EciMode _eciMode = EciMode.Default;
    private int _requestedVersion = -1;

    // rendering (Standard QR-only options; Micro QR has a single finder pattern
    // and no ECC headroom for overlays)
    private FinderPatternShape? _finderPatternShape;
    private IconData? _iconData;

    public QRCodeImageBuilder(string content) : base(defaultQuietZoneSize: 4)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        _content = content;
    }

    public QRCodeImageBuilder(QRCodeData qrCodeData) : base(defaultQuietZoneSize: 4)
    {
        if (qrCodeData is null)
            throw new ArgumentNullException(nameof(qrCodeData));

        _qrCodeData = qrCodeData;
    }

    // static methods for quick generation

    /// <summary>
    /// Generate a QR code as PNG byte array with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <returns>PNG encoded byte array.</returns>
    public static byte[] GetPngBytes(string content, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        return GetImageBytes(content, SKEncodedImageFormat.Png, eccLevel, size, 100);
    }

    /// <summary>
    /// Generate a QR code as PNG byte array with default settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <returns>PNG encoded byte array.</returns>
    public static byte[] GetPngBytes(QRCodeData qrCodeData, int size = 512)
    {
        return GetImageBytes(qrCodeData, SKEncodedImageFormat.Png, size, 100);
    }

    /// <summary>
    /// Generate a QR code as image byte array with specified format.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    /// <returns>Encoded byte array.</returns>
    public static byte[] GetImageBytes(string content, SKEncodedImageFormat format, ECCLevel eccLevel = ECCLevel.M, int size = 512, int quality = 100)
    {
        return new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .WithFormat(format, quality)
            .ToByteArray();
    }

    /// <summary>
    /// Generate a QR code as image byte array with specified format.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    /// <returns>Encoded byte array.</returns>
    public static byte[] GetImageBytes(QRCodeData qrCodeData, SKEncodedImageFormat format, int size = 512, int quality = 100)
    {
        return new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .WithFormat(format, quality)
            .ToByteArray();
    }

    /// <summary>
    /// Generate a QR code and save to stream with default PNG settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void SavePng(string content, Stream output, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveTo(output);
    }

    /// <summary>
    /// Generate a QR code and save to stream with default PNG settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void SavePng(QRCodeData qrCodeData, Stream output, int size = 512)
    {
        new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .SaveTo(output);
    }

    /// <summary>
    /// Generate a QR code as SVG (UTF-8 encoded) byte array with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>UTF-8 encoded SVG document.</returns>
    public static byte[] GetSvgBytes(string content, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        using var stream = new MemoryStream();
        new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveToSvg(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Generate a QR code as SVG (UTF-8 encoded) byte array with default settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>UTF-8 encoded SVG document.</returns>
    public static byte[] GetSvgBytes(QRCodeData qrCodeData, int size = 512)
    {
        using var stream = new MemoryStream();
        new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .SaveToSvg(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Generate a QR code and save as SVG to stream with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void SaveSvg(string content, Stream output, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveToSvg(output);
    }

    /// <summary>
    /// Generate a QR code and save as SVG to stream with default settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void SaveSvg(QRCodeData qrCodeData, Stream output, int size = 512)
    {
        new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .SaveToSvg(output);
    }

    /// <summary>
    /// Generate a QR code as SVG document string with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>SVG document.</returns>
    public static string GetSvgString(string content, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        return new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .ToSvgString();
    }

    /// <summary>
    /// Generate a QR code as SVG document string with default settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>SVG document.</returns>
    public static string GetSvgString(QRCodeData qrCodeData, int size = 512)
    {
        return new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .ToSvgString();
    }

    /// <summary>
    /// Generate a QR code and write as SVG (UTF-8 encoded) to an IBufferWriter with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void WriteSvg(string content, IBufferWriter<byte> writer, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveToSvg(writer);
    }

    /// <summary>
    /// Generate a QR code and write as SVG (UTF-8 encoded) to an IBufferWriter with default settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void WriteSvg(QRCodeData qrCodeData, IBufferWriter<byte> writer, int size = 512)
    {
        new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .SaveToSvg(writer);
    }

    /// <summary>
    /// Generate a QR code and write to an IBufferWriter with default PNG settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void WritePng(string content, IBufferWriter<byte> writer, ECCLevel eccLevel = ECCLevel.M, int size = 512)
    {
        WriteImage(content, writer, SKEncodedImageFormat.Png, eccLevel, size, quality: 100);
    }

    /// <summary>
    /// Generate a QR code and write to an IBufferWriter with default PNG settings.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void WritePng(QRCodeData qrCodeData, IBufferWriter<byte> writer, int size = 512)
    {
        WriteImage(qrCodeData, writer, SKEncodedImageFormat.Png, size, quality: 100);
    }

    /// <summary>
    /// Generate a QR code and write to an IBufferWriter with specified format.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="eccLevel">Error correction level. Default is M (15%).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    public static void WriteImage(string content, IBufferWriter<byte> writer, SKEncodedImageFormat format, ECCLevel eccLevel = ECCLevel.M, int size = 512, int quality = 100)
    {
        new QRCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .WithFormat(format, quality)
            .SaveTo(writer);
    }

    /// <summary>
    /// Generate a QR code and write to an IBufferWriter with specified format.
    /// </summary>
    /// <param name="qrCodeData">The QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    public static void WriteImage(QRCodeData qrCodeData, IBufferWriter<byte> writer, SKEncodedImageFormat format, int size = 512, int quality = 100)
    {
        new QRCodeImageBuilder(qrCodeData)
            .WithSize(size, size)
            .WithFormat(format, quality)
            .SaveTo(writer);
    }

    // Standard QR-specific builder methods

    /// <summary>
    /// Configure the error correction level for the QR code.
    /// </summary>
    /// <param name="eccLevel">Error correction level (L=7%, M=15%, Q=25%, H=30%). Level H is recommended when using icons or custom module shapes.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public QRCodeImageBuilder WithErrorCorrection(ECCLevel eccLevel)
    {
        _eccLevel = eccLevel;
        return this;
    }

    /// <summary>
    /// Configure the ECI (Extended Channel Interpretation) mode for character encoding.
    /// </summary>
    /// <param name="eciMode">The ECI mode to use.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public QRCodeImageBuilder WithEciMode(EciMode eciMode)
    {
        _eciMode = eciMode;
        return this;
    }

    /// <summary>
    /// Configure the QR code version to generate.
    /// </summary>
    /// <param name="version">Version to use (1-40), or -1 for automatic selection based on content length.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public QRCodeImageBuilder WithVersion(int version)
    {
        if (_qrCodeData is not null)
            throw new InvalidOperationException("WithVersion cannot be used when QRCodeData is provided directly.");
        if (version is not -1 and (< 1 or > 40))
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be between 1 and 40, or -1 for automatic selection.");

        _requestedVersion = version;
        return this;
    }

    /// <summary>
    /// Configure an icon to overlay on the center of the QR code.
    /// </summary>
    /// <param name="iconData">Icon configuration. If null, no icon is displayed.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public QRCodeImageBuilder WithIcon(IconData? iconData)
    {
        _iconData = iconData;
        return this;
    }

    /// <summary>
    /// Configure the shape of the finder patterns.
    /// </summary>
    /// <param name="finderPatternShape">Shape to use for finder patterns. If null, uses standard pattern or same as module shape.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public QRCodeImageBuilder WithFinderPatternShape(FinderPatternShape? finderPatternShape)
    {
        _finderPatternShape = finderPatternShape;
        return this;
    }

    // symbology hooks

    private protected override object ResolveSymbol(out int matrixSize)
    {
        var qrCodeData = _qrCodeData ?? QRCodeGenerator.CreateQrCode(_content.AsSpan(), _eccLevel, eciMode: _eciMode, requestedVersion: _requestedVersion, quietZoneSize: _quietZoneSize);
        matrixSize = qrCodeData.Size;
        return qrCodeData;
    }

    private protected override void RenderSymbol(SKCanvas canvas, object symbol, SKRect contentRect)
    {
        QRCodeRenderer.Render(canvas, contentRect, (QRCodeData)symbol, _codeColor, _backgroundColor, _iconData, _moduleShape, _moduleSizePercent, _gradientOptions, _finderPatternShape);
    }

    /// <summary>
    /// Custom finder shapes require antialiasing; built-in icon shapes only draw
    /// rectangles, bitmaps, and text, none of which degrade under crispEdges.
    /// </summary>
    private protected override bool UseCrispEdgesCore()
    {
        return _finderPatternShape is null
            && (_iconData?.Icon is null or ImageIconShape or ImageTextIconShape);
    }
}
