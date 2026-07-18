using System.Buffers;

namespace SkiaSharp.QrCode.Image;

/// <summary>
/// High-level builder for creating Micro QR code images with fluent configuration and static methods.
/// </summary>
/// <remarks>
/// <para>
/// This builder mirrors <see cref="QRCodeImageBuilder"/> for the Micro QR symbology
/// (ISO/IEC 18004, versions M1-M4). Version and error correction use the Micro
/// QR-typed <see cref="MicroQRVersion"/> / <see cref="MicroQREccLevel"/>, and the
/// default quiet zone is the 2 modules the specification requires (Standard QR uses 4).
/// </para>
/// <para>
/// Micro QR has a single finder pattern and no high error-correction headroom,
/// so the Standard QR styling options that depend on those (icon overlays and
/// custom finder pattern shapes) are intentionally not offered.
/// </para>
/// </remarks>
/// <seealso cref="MicroQRCodeGenerator"/>
/// <seealso cref="QRCodeRenderer"/>
/// <seealso cref="QRCodeImageBuilder"/>
public class MicroQRCodeImageBuilder : QRCodeImageBuilderBase<MicroQRCodeImageBuilder>
{
    private readonly string? _content;
    private readonly MicroQRCodeData? _data;
    private MicroQREccLevel _eccLevel = MicroQREccLevel.M;
    private MicroQRVersion? _requestedVersion;

    public MicroQRCodeImageBuilder(string content) : base(defaultQuietZoneSize: 2)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        _content = content;
    }

    public MicroQRCodeImageBuilder(MicroQRCodeData microQrCodeData) : base(defaultQuietZoneSize: 2)
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
    public static byte[] GetPngBytes(string content, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        return GetImageBytes(content, SKEncodedImageFormat.Png, eccLevel, size, 100);
    }

    /// <summary>
    /// Generate a Micro QR code as PNG byte array with default settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    /// <returns>PNG encoded byte array.</returns>
    public static byte[] GetPngBytes(MicroQRCodeData microQrCodeData, int size = 512)
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
    public static byte[] GetImageBytes(string content, SKEncodedImageFormat format, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512, int quality = 100)
    {
        return new MicroQRCodeImageBuilder(content)
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
    public static byte[] GetImageBytes(MicroQRCodeData microQrCodeData, SKEncodedImageFormat format, int size = 512, int quality = 100)
    {
        return new MicroQRCodeImageBuilder(microQrCodeData)
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
    public static void SavePng(string content, Stream output, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        new MicroQRCodeImageBuilder(content)
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
    public static void SavePng(MicroQRCodeData microQrCodeData, Stream output, int size = 512)
    {
        new MicroQRCodeImageBuilder(microQrCodeData)
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
    public static byte[] GetSvgBytes(string content, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        using var stream = new MemoryStream();
        new MicroQRCodeImageBuilder(content)
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
    public static byte[] GetSvgBytes(MicroQRCodeData microQrCodeData, int size = 512)
    {
        using var stream = new MemoryStream();
        new MicroQRCodeImageBuilder(microQrCodeData)
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
    public static void SaveSvg(string content, Stream output, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        new MicroQRCodeImageBuilder(content)
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
    public static void SaveSvg(MicroQRCodeData microQrCodeData, Stream output, int size = 512)
    {
        new MicroQRCodeImageBuilder(microQrCodeData)
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
    public static string GetSvgString(string content, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        return new MicroQRCodeImageBuilder(content)
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
    public static string GetSvgString(MicroQRCodeData microQrCodeData, int size = 512)
    {
        return new MicroQRCodeImageBuilder(microQrCodeData)
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
    public static void WriteSvg(string content, IBufferWriter<byte> writer, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        new MicroQRCodeImageBuilder(content)
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
    public static void WriteSvg(MicroQRCodeData microQrCodeData, IBufferWriter<byte> writer, int size = 512)
    {
        new MicroQRCodeImageBuilder(microQrCodeData)
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
    public static void WritePng(string content, IBufferWriter<byte> writer, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512)
    {
        WriteImage(content, writer, SKEncodedImageFormat.Png, eccLevel, size, quality: 100);
    }

    /// <summary>
    /// Generate a Micro QR code and write to an IBufferWriter with default PNG settings.
    /// </summary>
    /// <param name="microQrCodeData">The Micro QR code data to render.</param>
    /// <param name="writer">The buffer writer to write to.</param>
    /// <param name="size">Image size in pixels. Default is 512x512.</param>
    public static void WritePng(MicroQRCodeData microQrCodeData, IBufferWriter<byte> writer, int size = 512)
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
    public static void WriteImage(string content, IBufferWriter<byte> writer, SKEncodedImageFormat format, MicroQREccLevel eccLevel = MicroQREccLevel.M, int size = 512, int quality = 100)
    {
        new MicroQRCodeImageBuilder(content)
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
    public static void WriteImage(MicroQRCodeData microQrCodeData, IBufferWriter<byte> writer, SKEncodedImageFormat format, int size = 512, int quality = 100)
    {
        new MicroQRCodeImageBuilder(microQrCodeData)
            .WithSize(size, size)
            .WithFormat(format, quality)
            .SaveTo(writer);
    }

    // Micro QR-specific builder methods

    /// <summary>
    /// Configure the error correction level for the Micro QR code.
    /// </summary>
    /// <remarks>
    /// Legal combinations are version-dependent: M1 supports
    /// <see cref="MicroQREccLevel.ErrorDetectionOnly"/> only, M2/M3 support L and M,
    /// M4 supports L, M, and Q. Illegal combinations throw when the symbol is generated.
    /// </remarks>
    /// <param name="eccLevel">Error correction level.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public MicroQRCodeImageBuilder WithErrorCorrection(MicroQREccLevel eccLevel)
    {
        if (_data is not null)
            throw new InvalidOperationException("WithErrorCorrection cannot be used when MicroQRCodeData is provided directly.");

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
    public MicroQRCodeImageBuilder WithVersion(MicroQRVersion version)
    {
        if (_data is not null)
            throw new InvalidOperationException("WithVersion cannot be used when MicroQRCodeData is provided directly.");
        if ((uint)((int)version - 1) > 3)
            throw new ArgumentOutOfRangeException(nameof(version), $"Invalid Micro QR version: {version}");

        _requestedVersion = version;
        return this;
    }

    // symbology hooks

    private protected override object ResolveSymbol(out int matrixSize)
    {
        var data = _data ?? MicroQRCodeGenerator.CreateMicroQRCode(_content.AsSpan(), _eccLevel, _requestedVersion, _quietZoneSize);
        matrixSize = data.Size;
        return data;
    }

    private protected override void RenderSymbol(SKCanvas canvas, object symbol, SKRect contentRect)
    {
        QRCodeRenderer.Render(canvas, contentRect, (MicroQRCodeData)symbol, _codeColor, _backgroundColor, _moduleShape, _moduleSizePercent, _gradientOptions);
    }

    /// <summary>Micro QR has no finder styling or icon overlays — no extra antialiasing conditions.</summary>
    private protected override bool UseCrispEdgesCore() => true;
}
