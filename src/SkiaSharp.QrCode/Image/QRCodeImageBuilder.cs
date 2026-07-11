using System.Buffers;
using System.Text;

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
/// Chain methods like <see cref="WithSize(int, int)"/>, <see cref="WithModulePixelSize(int)"/>, <see cref="WithColors(SKColor?, SKColor?, SKColor?)"/>,
/// <see cref="WithModuleShape(ModuleShape?, float)"/>, <see cref="WithGradient(GradientOptions?)"/>,
/// and <see cref="WithIcon(IconData?)"/> to customize QR code appearance.
/// </para>
/// </remarks>
/// <seealso cref="QRCodeGenerator"/>
/// <seealso cref="QRCodeRenderer"/>
public class QRCodeImageBuilder
{
    private readonly string? _content;
    private readonly QRCodeData? _qrCodeData;
    private Vector2Slim? _explicitSize;
    private SKEncodedImageFormat _format = SKEncodedImageFormat.Png;
    private int _quality = 100;
    private ECCLevel _eccLevel = ECCLevel.M;
    private EciMode _eciMode = EciMode.Default;
    private int _requestedVersion = -1;
    private int _quietZoneSize = 4;
    private int? _modulePixelSize;

    // rendering
    private SKColor? _codeColor;
    private SKColor? _backgroundColor;
    private SKColor? _clearColor;
    private ModuleShape? _moduleShape;
    private float _moduleSizePercent = 1.0f;
    private FinderPatternShape? _finderPatternShape;
    private GradientOptions? _gradientOptions;
    private IconData? _iconData;

    public QRCodeImageBuilder(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        _content = content;
    }

    public QRCodeImageBuilder(QRCodeData qrCodeData)
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

    // builder methods

    /// <summary>
    /// Configure the output image size in absolute pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used alone, this sets an exact canvas size. Module pixel size then becomes
    /// <c>imageSize / QRCodeData.Size</c>, which may be fractional and can change when QR version changes.
    /// </para>
    /// <para>
    /// Used with <see cref="WithModulePixelSize(int)"/>, this sets the canvas size while module pixels
    /// define the QR content size (<c>QRCodeData.Size * modulePixelSize</c>).
    /// The canvas must be at least as large as the content on both sides; extra space is padded and
    /// the QR content is centered. Padding uses <c>clearColor</c> from <see cref="WithColors"/>.
    /// </para>
    /// </remarks>
    /// <param name="width">Width in pixels (must be positive).</param>
    /// <param name="height">Height in pixels (must be positive).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public QRCodeImageBuilder WithSize(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");

        _explicitSize = new Vector2Slim(width, height);
        return this;
    }

    /// <summary>
    /// Configure QR content size from pixels-per-module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sets each QR module to an exact integer pixel size. Content side length is
    /// <c>QRCodeData.Size * modulePixelSize</c>.
    /// </para>
    /// <para>
    /// Used alone, the output image matches the content size.
    /// Used with <see cref="WithSize(int, int)"/>, the content is centered on the larger canvas
    /// and padded with <c>clearColor</c>. If the canvas is smaller than the content, rendering throws.
    /// </para>
    /// </remarks>
    /// <param name="modulePixelSize">Pixel size per QR module (must be positive).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public QRCodeImageBuilder WithModulePixelSize(int modulePixelSize)
    {
        if (modulePixelSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(modulePixelSize), "Module pixel size must be positive");

        _modulePixelSize = modulePixelSize;
        return this;
    }

    /// <summary>
    /// Configure the output image format and quality.
    /// </summary>
    /// <param name="format">The image format to use.</param>
    /// <param name="quality">The image quality (0-100).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public QRCodeImageBuilder WithFormat(SKEncodedImageFormat format, int quality = 100)
    {
        if (quality is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 0 and 100");

        _format = format;
        _quality = quality;
        return this;
    }

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
    /// Configure the quiet zone size (white border) around the QR code.
    /// </summary>
    /// <param name="size">Quiet zone size in modules (0-10). Recommended is 4.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public QRCodeImageBuilder WithQuietZone(int size = 4)
    {
        if (size is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(size), "Quiet zone size must be between 0 and 10");
        _quietZoneSize = size;
        return this;
    }

    /// <summary>
    /// Configure the colors used in the QR code image.
    /// </summary>
    /// <param name="codeColor">Color of QR code modules. If null, uses black.</param>
    /// <param name="backgroundColor">Background color. If null, uses white.</param>
    /// <param name="clearColor">Canvas clear color. If null, uses transparent.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public QRCodeImageBuilder WithColors(SKColor? codeColor = null, SKColor? backgroundColor = null, SKColor? clearColor = null)
    {
        _codeColor = codeColor;
        _backgroundColor = backgroundColor;
        _clearColor = clearColor;
        return this;
    }

    /// <summary>
    /// Configure the shape of the QR code modules.
    /// </summary>
    /// <remarks>
    /// Note: Custom module shapes affect finder patterns unless a custom finder pattern shape is explicitly set via <see cref="WithFinderPatternShape"/>.
    /// </remarks>
    /// <param name="moduleShape">Shape to use for modules. If null, uses rectangles.</param>
    /// <param name="sizePercent">Module size as a percentage of cell size (0.5-1.0). Values below 0.8 may affect readability. Default is 1.0 (no gaps).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public QRCodeImageBuilder WithModuleShape(ModuleShape? moduleShape, float sizePercent = 1.0f)
    {
        if (sizePercent is < 0.5f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(sizePercent), "Module size percent must be between 0.5 and 1.0.");

        _moduleShape = moduleShape;
        _moduleSizePercent = sizePercent;
        return this;
    }

    /// <summary>
    /// Configure gradient options for the QR code modules.
    /// </summary>
    /// <param name="gradientOptions">Gradient configuration. If null, uses solid color.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public QRCodeImageBuilder WithGradient(GradientOptions? gradientOptions)
    {
        _gradientOptions = gradientOptions;
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

    /// <summary>
    /// Generate QR code and save to stream
    /// </summary>
    /// <param name="output">The output stream (must be writable).</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public void SaveTo(Stream output)
    {
        if (output is null)
            throw new ArgumentNullException(nameof(output));
        if (!output.CanWrite)
            throw new ArgumentException("Output stream must be writable", nameof(output));

        using var image = GenerateImage();
        using var data = image.Encode(_format, _quality);

        // write to stream
        data.SaveTo(output);
    }

    /// <summary>
    /// Generate QR code and write to an IBufferWriter.
    /// This is more efficient than SaveTo(Stream) as it avoids intermediate buffering.
    /// </summary>
    /// <param name="writer">The buffer writer</param>
    /// <exception cref="ArgumentNullException"></exception>
    public void SaveTo(IBufferWriter<byte> writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        using var image = GenerateImage();
        using var data = image.Encode(_format, _quality);

        // write to IBufferWriter
        var span = data.AsSpan();
        var buffer = writer.GetSpan(span.Length);
        span.CopyTo(buffer);
        writer.Advance(span.Length);
    }

    /// <summary>
    /// Generate QR code and save as SVG document to stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The QR code is drawn as vector shapes via <see cref="SKSvgCanvas"/>, so the output scales
    /// without quality loss. All builder options apply: colors, module shapes, gradients, finder
    /// pattern shapes, and icons (icon images are embedded as base64 data URIs).
    /// </para>
    /// <para>
    /// The root element carries a <c>viewBox</c>, so the document scales its content when
    /// embedded at a different size (img element, CSS). For plain rectangular modules,
    /// <c>shape-rendering="crispEdges"</c> is added to avoid antialiasing seams between modules;
    /// custom shapes keep antialiasing for smooth curves.
    /// </para>
    /// <para>
    /// <see cref="WithFormat(SKEncodedImageFormat, int)"/> is ignored — SVG is a vector format,
    /// not an <see cref="SKEncodedImageFormat"/>. Size options (<see cref="WithSize(int, int)"/>,
    /// <see cref="WithModulePixelSize(int)"/>) define the SVG viewport in units.
    /// The stream is left open after writing.
    /// </para>
    /// </remarks>
    /// <param name="output">The output stream (must be writable).</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public void SaveToSvg(Stream output)
    {
        if (output is null)
            throw new ArgumentNullException(nameof(output));
        if (!output.CanWrite)
            throw new ArgumentException("Output stream must be writable", nameof(output));

        using var document = RenderSvgDocument(out var insertAt, out var rootAttributes);
        var buffer = document.GetBuffer();
        var length = (int)document.Length;

        if (insertAt < 0)
        {
            output.Write(buffer, 0, length);
            return;
        }

        var attributeBytes = Encoding.UTF8.GetBytes(rootAttributes);
        output.Write(buffer, 0, insertAt);
        output.Write(attributeBytes, 0, attributeBytes.Length);
        output.Write(buffer, insertAt, length - insertAt);
    }

    /// <summary>
    /// Generate QR code and write as SVG document to an IBufferWriter.
    /// </summary>
    /// <remarks>
    /// See <see cref="SaveToSvg(Stream)"/> for rendering behavior.
    /// </remarks>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public void SaveToSvg(IBufferWriter<byte> writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        using var document = RenderSvgDocument(out var insertAt, out var rootAttributes);
        var buffer = document.GetBuffer();
        var length = (int)document.Length;

        if (insertAt < 0)
        {
            var raw = writer.GetSpan(length);
            buffer.AsSpan(0, length).CopyTo(raw);
            writer.Advance(length);
            return;
        }

        var attributeBytes = Encoding.UTF8.GetBytes(rootAttributes);
        var total = length + attributeBytes.Length;
        var span = writer.GetSpan(total);
        buffer.AsSpan(0, insertAt).CopyTo(span);
        attributeBytes.AsSpan().CopyTo(span.Slice(insertAt));
        buffer.AsSpan(insertAt, length - insertAt).CopyTo(span.Slice(insertAt + attributeBytes.Length));
        writer.Advance(total);
    }

    /// <summary>
    /// Generate QR code and return as SVG document string.
    /// </summary>
    /// <remarks>
    /// See <see cref="SaveToSvg(Stream)"/> for rendering behavior.
    /// </remarks>
    /// <returns>SVG document.</returns>
    public string ToSvgString()
    {
        using var stream = new MemoryStream();
        SaveToSvg(stream);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// Renders the SVG document into a memory buffer and prepares root element attributes
    /// (<c>viewBox</c>, optional <c>shape-rendering</c>) to insert at <paramref name="insertAt"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SKSvgCanvas"/> writes <c>width</c>/<c>height</c> on the root element but no
    /// <c>viewBox</c>. Without a viewBox, an SVG embedded at a different size (img element, CSS)
    /// keeps its content at the original coordinates instead of scaling — the main reason to use
    /// SVG in the first place. The attributes are inserted right after <c>&lt;svg </c>; if the
    /// marker is not found (unexpected upstream format change), <paramref name="insertAt"/> is -1
    /// and the document should be written unpatched.
    /// </remarks>
    private MemoryStream RenderSvgDocument(out int insertAt, out string rootAttributes)
    {
        var qrCodeData = ResolveQrCodeData();
        var (info, contentRect) = CreateLayout(qrCodeData);

        var document = new MemoryStream();
        // SKSvgCanvas flushes the SVG document when the canvas is disposed,
        // so dispose before the buffer is consumed.
        using (var canvas = SKSvgCanvas.Create(SKRect.Create(0, 0, info.Width, info.Height), document))
        {
            RenderContent(canvas, qrCodeData, info, contentRect);
        }

        var viewBox = $"viewBox=\"0 0 {info.Width} {info.Height}\" ";
        rootAttributes = UseCrispEdges() ? viewBox + "shape-rendering=\"crispEdges\" " : viewBox;

        ReadOnlySpan<byte> marker = "<svg "u8;
        var index = document.GetBuffer().AsSpan(0, (int)document.Length).IndexOf(marker);
        insertAt = index < 0 ? -1 : index + marker.Length;
        return document;
    }

    /// <summary>
    /// Antialiasing between adjacent vector shapes produces visible hairline seams when the SVG
    /// is scaled to non-integer sizes. For plain rectangular modules crispEdges removes the seams;
    /// for custom shapes (circles, rounded rects, custom finder patterns or icons) antialiasing
    /// is kept for smooth curves. Built-in icon shapes only draw rectangles, bitmaps, and text,
    /// none of which degrade under crispEdges.
    /// </summary>
    private bool UseCrispEdges()
    {
        return (_moduleShape is null || _moduleShape is RectangleModuleShape)
            && _moduleSizePercent == 1.0f
            && _finderPatternShape is null
            && (_iconData?.Icon is null or ImageIconShape or ImageTextIconShape);
    }

    /// <summary>
    /// Generate QR code and return as byte array.
    /// </summary>
    /// <returns>Encoded image as byte array.</returns>
    public byte[] ToByteArray()
    {
        using var image = GenerateImage();
        using var data = image.Encode(_format, _quality);
        return data.ToArray();
    }

    /// <summary>
    /// Generate QR code and return as SKImage.
    /// </summary>
    /// <returns>SKImage instance (caller must dispose).</returns>
    public SKImage ToImage()
    {
        return GenerateImage();
    }

    /// <summary>
    /// Generate QR code and return as SKBitmap.
    /// </summary>
    /// <returns>SKBitmap instance (caller must dispose).</returns>
    public SKBitmap ToBitmap()
    {
        using var image = GenerateImage();
        return SKBitmap.FromImage(image);
    }

    /// <summary>
    /// Generate the QRCodeData from builder input.
    /// </summary>
    private QRCodeData ResolveQrCodeData()
    {
        return _qrCodeData ?? QRCodeGenerator.CreateQrCode(_content.AsSpan(), _eccLevel, eciMode: _eciMode, requestedVersion: _requestedVersion, quietZoneSize: _quietZoneSize);
    }

    /// <summary>
    /// Generate the SKImage from QR code data.
    /// </summary>
    private SKImage GenerateImage()
    {
        // Generate QR Data
        var qrCodeData = ResolveQrCodeData();

        var (info, contentRect) = CreateLayout(qrCodeData);

        var clearColor = _clearColor ?? SKColors.Transparent;
        var contentCoversCanvas = ContentCoversCanvas(contentRect, info);
        var backgroundIsOpaque = (_backgroundColor ?? SKColors.White).Alpha == byte.MaxValue;
        var clearIsOpaque = clearColor.Alpha == byte.MaxValue;

        // When the base layer (QR background fill, or the cleared canvas) is opaque
        // everywhere, anything drawn over it stays opaque, so the whole image is
        // opaque no matter what modules/icons/gradients are painted on top.
        // An opaque surface lets encoders skip the alpha channel and the unpremul
        // pass — PNG output becomes RGB: smaller and faster to encode.
        var isOpaque = contentCoversCanvas
            ? backgroundIsOpaque || clearIsOpaque
            : clearIsOpaque;
        if (isOpaque)
        {
            info = info.WithAlphaType(SKAlphaType.Opaque);
        }

        using var surface = SKSurface.Create(info);
        RenderContent(surface.Canvas, qrCodeData, info, contentRect);

        return surface.Snapshot();
    }

    /// <summary>
    /// Draws the configured QR code onto the canvas. Shared by the raster
    /// (<see cref="GenerateImage"/>) and SVG (<see cref="SaveToSvg(Stream)"/>) paths.
    /// </summary>
    private void RenderContent(SKCanvas canvas, QRCodeData qrCodeData, SKImageInfo info, SKRect contentRect)
    {
        var clearColor = _clearColor ?? SKColors.Transparent;
        var contentCoversCanvas = ContentCoversCanvas(contentRect, info);
        var backgroundIsOpaque = (_backgroundColor ?? SKColors.White).Alpha == byte.MaxValue;

        // Clear the canvas with clearColor, then draw QR into contentRect; extra
        // canvas area (pad) keeps clearColor. The clear is skipped when it cannot
        // remain visible: a fresh canvas is already fully transparent, and an
        // opaque QR background covering the whole canvas overwrites it anyway.
        if (clearColor.Alpha != 0 && !(contentCoversCanvas && backgroundIsOpaque))
        {
            canvas.Clear(clearColor);
        }

        QRCodeRenderer.Render(canvas, contentRect, qrCodeData, _codeColor, _backgroundColor, _iconData, _moduleShape, _moduleSizePercent, _gradientOptions, _finderPatternShape);
    }

    private static bool ContentCoversCanvas(SKRect contentRect, SKImageInfo info)
    {
        return contentRect.Left <= 0 && contentRect.Top <= 0
            && contentRect.Right >= info.Width && contentRect.Bottom >= info.Height;
    }

    private (SKImageInfo info, SKRect contentRect) CreateLayout(QRCodeData qrCodeData)
    {
        if (_modulePixelSize is null)
        {
            var size = _explicitSize ?? new Vector2Slim(512, 512);
            return (new SKImageInfo(size.X, size.Y), SKRect.Create(0, 0, size.X, size.Y));
        }

        int contentSide;
        try
        {
            contentSide = checked(qrCodeData.Size * _modulePixelSize.Value);
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException("Calculated image size overflowed. Reduce module pixel size or QR version.", ex);
        }

        if (_explicitSize is null)
            return (new SKImageInfo(contentSide, contentSide), SKRect.Create(0, 0, contentSide, contentSide));

        var canvasWidth = _explicitSize.Value.X;
        var canvasHeight = _explicitSize.Value.Y;
        if (canvasWidth < contentSide || canvasHeight < contentSide)
        {
            throw new InvalidOperationException(
                $"Canvas size {canvasWidth}x{canvasHeight} is smaller than QR content size {contentSide}x{contentSide} " +
                $"(QR matrix size {qrCodeData.Size} * module pixel size {_modulePixelSize.Value}).");
        }

        // Use integer offsets so content stays on whole pixels (odd padding may be 1px asymmetric).
        var left = (canvasWidth - contentSide) / 2;
        var top = (canvasHeight - contentSide) / 2;
        return (
            new SKImageInfo(canvasWidth, canvasHeight),
            SKRect.Create(left, top, contentSide, contentSide));
    }
}
