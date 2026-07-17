using System.Buffers;
using System.Text;

namespace SkiaSharp.QrCode.Image;

/// <summary>
/// High-level builder for creating Micro QR code images with fluent configuration and static methods.
/// </summary>
/// <remarks>
/// <para>
/// This builder mirrors <see cref="QRCodeImageBuilder"/> for the Micro QR symbology
/// (ISO/IEC 18004, versions M1-M4). Version and error correction use the Micro
/// QR-typed <see cref="MicroQrVersion"/> / <see cref="MicroQrEccLevel"/>, and the
/// default quiet zone is the 2 modules the specification requires (Standard QR uses 4).
/// </para>
/// <para>
/// Micro QR has a single finder pattern and no high error-correction headroom,
/// so the Standard QR styling options that depend on those (icon overlays and
/// custom finder pattern shapes) are intentionally not offered.
/// </para>
/// </remarks>
/// <seealso cref="MicroQrCodeGenerator"/>
/// <seealso cref="QRCodeRenderer"/>
public class MicroQrCodeImageBuilder
{
    private readonly string? _content;
    private readonly MicroQrCodeData? _data;
    private Vector2Slim? _explicitSize;
    private SKEncodedImageFormat _format = SKEncodedImageFormat.Png;
    private int _quality = 100;
    private MicroQrEccLevel _eccLevel = MicroQrEccLevel.M;
    private MicroQrVersion? _requestedVersion;
    private int _quietZoneSize = 2;
    private int? _modulePixelSize;

    // rendering
    private SKColor? _codeColor;
    private SKColor? _backgroundColor;
    private SKColor? _clearColor;
    private ModuleShape? _moduleShape;
    private float _moduleSizePercent = 1.0f;
    private GradientOptions? _gradientOptions;

    public MicroQrCodeImageBuilder(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        _content = content;
    }

    public MicroQrCodeImageBuilder(MicroQrCodeData microQrCodeData)
    {
        if (microQrCodeData is null)
            throw new ArgumentNullException(nameof(microQrCodeData));

        _data = microQrCodeData;
    }

    // static methods for quick generation

    /// <summary>
    /// Generate a Micro QR code as PNG byte array with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <returns>PNG encoded byte array.</returns>
    public static byte[] GetPngBytes(string content, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        return GetImageBytes(content, SKEncodedImageFormat.Png, eccLevel, size, 100);
    }

    /// <summary>
    /// Generate a Micro QR code as PNG byte array with default settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <returns>PNG encoded byte array.</returns>
    public static byte[] GetPngBytes(MicroQrCodeData microQrCodeData, int size = 512)
    {
        return GetImageBytes(microQrCodeData, SKEncodedImageFormat.Png, size, 100);
    }

    /// <summary>
    /// Generate a Micro QR code as image byte array with specified format.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    /// <returns>Encoded byte array.</returns>
    public static byte[] GetImageBytes(string content, SKEncodedImageFormat format, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512, int quality = 100)
    {
        return new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .WithFormat(format, quality)
            .ToByteArray();
    }

    /// <summary>
    /// Generate a Micro QR code as image byte array with specified format.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    /// <returns>Encoded byte array.</returns>
    public static byte[] GetImageBytes(MicroQrCodeData microQrCodeData, SKEncodedImageFormat format, int size = 512, int quality = 100)
    {
        return new MicroQrCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .WithFormat(format, quality)
            .ToByteArray();
    }

    /// <summary>
    /// Generate a Micro QR code and save to stream with default PNG settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void SavePng(string content, Stream output, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveTo(output);
    }

    /// <summary>
    /// Generate a Micro QR code and save to stream with default PNG settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void SavePng(MicroQrCodeData microQrCodeData, Stream output, int size = 512)
    {
        new MicroQrCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .SaveTo(output);
    }

    /// <summary>
    /// Generate a Micro QR code as SVG (UTF-8 encoded) byte array with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>UTF-8 encoded SVG document.</returns>
    public static byte[] GetSvgBytes(string content, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        using var stream = new MemoryStream();
        new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveToSvg(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Generate a Micro QR code as SVG (UTF-8 encoded) byte array with default settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>UTF-8 encoded SVG document.</returns>
    public static byte[] GetSvgBytes(MicroQrCodeData microQrCodeData, int size = 512)
    {
        using var stream = new MemoryStream();
        new MicroQrCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .SaveToSvg(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Generate a Micro QR code and save as SVG to stream with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void SaveSvg(string content, Stream output, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveToSvg(output);
    }

    /// <summary>
    /// Generate a Micro QR code and save as SVG to stream with default settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void SaveSvg(MicroQrCodeData microQrCodeData, Stream output, int size = 512)
    {
        new MicroQrCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .SaveToSvg(output);
    }

    /// <summary>
    /// Generate a Micro QR code as SVG document string with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>SVG document.</returns>
    public static string GetSvgString(string content, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        return new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .ToSvgString();
    }

    /// <summary>
    /// Generate a Micro QR code as SVG document string with default settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    /// <returns>SVG document.</returns>
    public static string GetSvgString(MicroQrCodeData microQrCodeData, int size = 512)
    {
        return new MicroQrCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .ToSvgString();
    }

    /// <summary>
    /// Generate a Micro QR code and write as SVG (UTF-8 encoded) to an IBufferWriter with default settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void WriteSvg(string content, IBufferWriter<byte> writer, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .SaveToSvg(writer);
    }

    /// <summary>
    /// Generate a Micro QR code and write as SVG (UTF-8 encoded) to an IBufferWriter with default settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="size">SVG viewport size in units. Default is 512x512.</param>
    public static void WriteSvg(MicroQrCodeData microQrCodeData, IBufferWriter<byte> writer, int size = 512)
    {
        new MicroQrCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .SaveToSvg(writer);
    }

    /// <summary>
    /// Generate a Micro QR code and write to an IBufferWriter with default PNG settings.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void WritePng(string content, IBufferWriter<byte> writer, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512)
    {
        WriteImage(content, writer, SKEncodedImageFormat.Png, eccLevel, size, quality: 100);
    }

    /// <summary>
    /// Generate a Micro QR code and write to an IBufferWriter with default PNG settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void WritePng(MicroQrCodeData microQrCodeData, IBufferWriter<byte> writer, int size = 512)
    {
        WriteImage(microQrCodeData, writer, SKEncodedImageFormat.Png, size, quality: 100);
    }

    /// <summary>
    /// Generate a Micro QR code and write to an IBufferWriter with specified format.
    /// </summary>
    /// <param name="content">The content to encode.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="eccLevel">Error correction level. Default is M.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    public static void WriteImage(string content, IBufferWriter<byte> writer, SKEncodedImageFormat format, MicroQrEccLevel eccLevel = MicroQrEccLevel.M, int size = 512, int quality = 100)
    {
        new MicroQrCodeImageBuilder(content)
            .WithSize(size, size)
            .WithErrorCorrection(eccLevel)
            .WithFormat(format, quality)
            .SaveTo(writer);
    }

    /// <summary>
    /// Generate a Micro QR code and write to an IBufferWriter with specified format.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="format">Image format (PNG, JPEG, WEBP, etc.).</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <param name="quality">Encoding quality (0-100). Default is 100.</param>
    public static void WriteImage(MicroQrCodeData microQrCodeData, IBufferWriter<byte> writer, SKEncodedImageFormat format, int size = 512, int quality = 100)
    {
        new MicroQrCodeImageBuilder(microQrCodeData)
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
    /// <c>imageSize / MicroQrCodeData.Size</c>, which may be fractional and can change when the version changes.
    /// </para>
    /// <para>
    /// Used with <see cref="WithModulePixelSize(int)"/>, this sets the canvas size while module pixels
    /// define the content size (<c>MicroQrCodeData.Size * modulePixelSize</c>).
    /// The canvas must be at least as large as the content on both sides; extra space is padded and
    /// the content is centered. Padding uses <c>clearColor</c> from <see cref="WithColors"/>.
    /// </para>
    /// </remarks>
    /// <param name="width">Width in pixels (must be positive).</param>
    /// <param name="height">Height in pixels (must be positive).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public MicroQrCodeImageBuilder WithSize(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");

        _explicitSize = new Vector2Slim(width, height);
        return this;
    }

    /// <summary>
    /// Configure content size from pixels-per-module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sets each module to an exact integer pixel size. Content side length is
    /// <c>MicroQrCodeData.Size * modulePixelSize</c>.
    /// </para>
    /// <para>
    /// Used alone, the output image matches the content size.
    /// Used with <see cref="WithSize(int, int)"/>, the content is centered on the larger canvas
    /// and padded with <c>clearColor</c>. If the canvas is smaller than the content, rendering throws.
    /// </para>
    /// </remarks>
    /// <param name="modulePixelSize">Pixel size per module (must be positive).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public MicroQrCodeImageBuilder WithModulePixelSize(int modulePixelSize)
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
    public MicroQrCodeImageBuilder WithFormat(SKEncodedImageFormat format, int quality = 100)
    {
        if (quality is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 0 and 100");

        _format = format;
        _quality = quality;
        return this;
    }

    /// <summary>
    /// Configure the error correction level for the Micro QR code.
    /// </summary>
    /// <remarks>
    /// Legal combinations are version-dependent: M1 supports
    /// <see cref="MicroQrEccLevel.ErrorDetectionOnly"/> only, M2/M3 support L and M,
    /// M4 supports L, M, and Q. Illegal combinations throw when the symbol is generated.
    /// </remarks>
    /// <param name="eccLevel">Error correction level.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public MicroQrCodeImageBuilder WithErrorCorrection(MicroQrEccLevel eccLevel)
    {
        if (_data is not null)
            throw new InvalidOperationException("WithErrorCorrection cannot be used when MicroQrCodeData is provided directly.");

        _eccLevel = eccLevel;
        return this;
    }

    /// <summary>
    /// Configure the Micro QR version to generate.
    /// </summary>
    /// <param name="version">Version to use (M1-M4). When not called, the smallest version that fits the content is selected.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public MicroQrCodeImageBuilder WithVersion(MicroQrVersion version)
    {
        if (_data is not null)
            throw new InvalidOperationException("WithVersion cannot be used when MicroQrCodeData is provided directly.");
        if ((uint)((int)version - 1) > 3)
            throw new ArgumentOutOfRangeException(nameof(version), $"Invalid Micro QR version: {version}");

        _requestedVersion = version;
        return this;
    }

    /// <summary>
    /// Configure the quiet zone size (light border) around the Micro QR code.
    /// </summary>
    /// <param name="size">Quiet zone size in modules (0-10). The Micro QR specification requires 2 (the default).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public MicroQrCodeImageBuilder WithQuietZone(int size = 2)
    {
        if (size is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(size), "Quiet zone size must be between 0 and 10");
        _quietZoneSize = size;
        return this;
    }

    /// <summary>
    /// Configure the colors used in the Micro QR code image.
    /// </summary>
    /// <param name="codeColor">Color of modules. If null, uses black.</param>
    /// <param name="backgroundColor">Background color. If null, uses white.</param>
    /// <param name="clearColor">Canvas clear color. If null, uses transparent.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public MicroQrCodeImageBuilder WithColors(SKColor? codeColor = null, SKColor? backgroundColor = null, SKColor? clearColor = null)
    {
        _codeColor = codeColor;
        _backgroundColor = backgroundColor;
        _clearColor = clearColor;
        return this;
    }

    /// <summary>
    /// Configure the shape of the Micro QR code modules.
    /// </summary>
    /// <remarks>
    /// Micro QR carries less error-correction headroom than Standard QR, so keep
    /// module shapes conservative and prefer <paramref name="sizePercent"/> at or near 1.0.
    /// </remarks>
    /// <param name="moduleShape">Shape to use for modules. If null, uses rectangles.</param>
    /// <param name="sizePercent">Module size as a percentage of cell size (0.5-1.0). Default is 1.0 (no gaps).</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public MicroQrCodeImageBuilder WithModuleShape(ModuleShape? moduleShape, float sizePercent = 1.0f)
    {
        if (sizePercent is < 0.5f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(sizePercent), "Module size percent must be between 0.5 and 1.0.");

        _moduleShape = moduleShape;
        _moduleSizePercent = sizePercent;
        return this;
    }

    /// <summary>
    /// Configure gradient options for the Micro QR code modules.
    /// </summary>
    /// <param name="gradientOptions">Gradient configuration. If null, uses solid color.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public MicroQrCodeImageBuilder WithGradient(GradientOptions? gradientOptions)
    {
        _gradientOptions = gradientOptions;
        return this;
    }

    /// <summary>
    /// Generate Micro QR code and save to stream
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
    /// Generate Micro QR code and write to an IBufferWriter.
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

        // Write in writer-provided segments; a single GetSpan for the whole payload
        // would force segmented writers (e.g. PipeWriter) into one contiguous buffer.
        writer.Write(data.AsSpan());
    }

    /// <summary>
    /// Generate Micro QR code and save as SVG document to stream.
    /// </summary>
    /// <remarks>
    /// See <see cref="QRCodeImageBuilder.SaveToSvg(Stream)"/> for the shared SVG
    /// rendering behavior (viewBox injection, crispEdges for plain rectangles,
    /// <see cref="WithFormat(SKEncodedImageFormat, int)"/> ignored, stream left open).
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

        RenderSvg(output);
    }

    /// <summary>
    /// Generate Micro QR code and write as SVG document to an IBufferWriter.
    /// </summary>
    /// <remarks>
    /// See <see cref="SaveToSvg(Stream)"/> for rendering behavior. Data is written in
    /// writer-provided segments, so segmented writers (e.g. PipeWriter) work without
    /// a single contiguous buffer for the whole document.
    /// </remarks>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public void SaveToSvg(IBufferWriter<byte> writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        using var stream = new BufferWriterStream(writer);
        RenderSvg(stream);
    }

    /// <summary>
    /// Generate Micro QR code and return as SVG document string.
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
    /// Generate Micro QR code and return as byte array.
    /// </summary>
    /// <returns>Encoded image as byte array.</returns>
    public byte[] ToByteArray()
    {
        using var image = GenerateImage();
        using var data = image.Encode(_format, _quality);
        return data.ToArray();
    }

    /// <summary>
    /// Generate Micro QR code and return as SKImage.
    /// </summary>
    /// <returns>SKImage instance (caller must dispose).</returns>
    public SKImage ToImage()
    {
        return GenerateImage();
    }

    /// <summary>
    /// Generate Micro QR code and return as SKBitmap.
    /// </summary>
    /// <returns>SKBitmap instance (caller must dispose).</returns>
    public SKBitmap ToBitmap()
    {
        using var image = GenerateImage();
        return SKBitmap.FromImage(image);
    }

    /// <summary>
    /// Generate the MicroQrCodeData from builder input.
    /// </summary>
    private MicroQrCodeData ResolveData()
    {
        return _data ?? MicroQrCodeGenerator.CreateMicroQrCode(_content.AsSpan(), _eccLevel, _requestedVersion, _quietZoneSize);
    }

    /// <summary>
    /// Renders the SVG document to the output stream, injecting root element attributes
    /// (<c>viewBox</c>, optional <c>shape-rendering</c>) while streaming.
    /// Same mechanism as <see cref="QRCodeImageBuilder"/> — see
    /// <see cref="SvgRootAttributeInjectorStream"/> for why the attributes are injected.
    /// </summary>
    private void RenderSvg(Stream output)
    {
        var data = ResolveData();
        var (info, contentRect) = QrImageLayout.CreateLayout(data.Size, _explicitSize, _modulePixelSize);

        var viewBox = $"viewBox=\"0 0 {info.Width} {info.Height}\" ";
        var rootAttributes = UseCrispEdges() ? viewBox + "shape-rendering=\"crispEdges\" " : viewBox;

        using var injector = new SvgRootAttributeInjectorStream(output, rootAttributes);
        // SKSvgCanvas flushes the SVG document when the canvas is disposed, so dispose
        // it before the injector (which then flushes any pending header bytes).
        // The output stream itself stays open.
        using (var canvas = SKSvgCanvas.Create(SKRect.Create(0, 0, info.Width, info.Height), injector))
        {
            RenderContent(canvas, data, info, contentRect);
        }
    }

    /// <summary>
    /// Antialiasing between adjacent vector shapes produces visible hairline seams
    /// when the SVG is scaled to non-integer sizes; crispEdges removes them for plain
    /// rectangular modules while custom shapes keep antialiasing for smooth curves.
    /// </summary>
    private bool UseCrispEdges()
    {
        return (_moduleShape is null || _moduleShape is RectangleModuleShape)
            && _moduleSizePercent == 1.0f;
    }

    /// <summary>
    /// Generate the SKImage from Micro QR code data.
    /// </summary>
    private SKImage GenerateImage()
    {
        var data = ResolveData();

        var (info, contentRect) = QrImageLayout.CreateLayout(data.Size, _explicitSize, _modulePixelSize);

        var clearColor = _clearColor ?? SKColors.Transparent;
        var contentCoversCanvas = QrImageLayout.ContentCoversCanvas(contentRect, info);
        var backgroundIsOpaque = (_backgroundColor ?? SKColors.White).Alpha == byte.MaxValue;
        var clearIsOpaque = clearColor.Alpha == byte.MaxValue;

        // When the base layer (background fill, or the cleared canvas) is opaque
        // everywhere, anything drawn over it stays opaque, so the whole image is
        // opaque no matter what modules/gradients are painted on top.
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
        RenderContent(surface.Canvas, data, info, contentRect);

        return surface.Snapshot();
    }

    /// <summary>
    /// Draws the configured Micro QR code onto the canvas. Shared by the raster
    /// (<see cref="GenerateImage"/>) and SVG (<see cref="SaveToSvg(Stream)"/>) paths.
    /// </summary>
    private void RenderContent(SKCanvas canvas, MicroQrCodeData data, SKImageInfo info, SKRect contentRect)
    {
        var clearColor = _clearColor ?? SKColors.Transparent;
        var contentCoversCanvas = QrImageLayout.ContentCoversCanvas(contentRect, info);
        var backgroundIsOpaque = (_backgroundColor ?? SKColors.White).Alpha == byte.MaxValue;

        // Clear the canvas with clearColor, then draw into contentRect; extra
        // canvas area (pad) keeps clearColor. The clear is skipped when it cannot
        // remain visible: a fresh canvas is already fully transparent, and an
        // opaque background covering the whole canvas overwrites it anyway.
        if (clearColor.Alpha != 0 && !(contentCoversCanvas && backgroundIsOpaque))
        {
            canvas.Clear(clearColor);
        }

        QRCodeRenderer.Render(canvas, contentRect, data, _codeColor, _backgroundColor, _moduleShape, _moduleSizePercent, _gradientOptions);
    }
}
