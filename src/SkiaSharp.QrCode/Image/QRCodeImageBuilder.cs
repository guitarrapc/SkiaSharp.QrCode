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
/// Chain methods like <see cref="WithSize(int, int)"/>, <see cref="WithColors(SKColor?, SKColor?, SKColor?)"/>, 
/// <see cref="WithModuleShape(ModuleShape?, float)"/>, <see cref="WithGradient(GradientOptions?)"/>, 
/// and <see cref="WithIcon(IconData?)"/> to customize QR code appearance.
/// </para>
/// </remarks>
/// <seealso cref="QRCodeGenerator"/>
/// <seealso cref="QRCodeRenderer"/>
public class QRCodeImageBuilder
{
    private readonly string _content;
    private Vector2Slim _size = new(512, 512);
    private SKEncodedImageFormat _format = SKEncodedImageFormat.Png;
    private int _quality = 100;
    private ECCLevel _eccLevel = ECCLevel.M;
    private EciMode _eciMode = EciMode.Default;
    private int _quietZoneSize = 4;

    // rendering
    private SKColor? _codeColor;
    private SKColor? _backgroundColor;
    private SKColor? _clearColor;
    private ModuleShape? _moduleShape;
    private float _moduleSizePercent = 1.0f;
    private GradientOptions? _gradientOptions;
    private IconData? _iconData;

    public QRCodeImageBuilder(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        _content = content;
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

    // builder methods

    /// <summary>
    /// Configure the size of the generated QR code image.
    /// </summary>
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

        _size = new Vector2Slim(width, height);
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
    /// Generate the SKImage from QR code data.
    /// </summary>
    private SKImage GenerateImage()
    {
        // Generate QR Data
        var qrCodeData = QRCodeGenerator.CreateQrCode(_content.AsSpan(), _eccLevel, eciMode: _eciMode, quietZoneSize: _quietZoneSize);

        // Create surface and render
        var info = new SKImageInfo(_size.X, _size.Y);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        canvas.Render(qrCodeData, info.Width, info.Height, _clearColor, _codeColor, _backgroundColor, _iconData, _moduleShape, _moduleSizePercent, _gradientOptions);

        // Encode and save
        return surface.Snapshot();
    }
}
