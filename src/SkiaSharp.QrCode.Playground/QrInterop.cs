using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode.Playground;

/// <summary>
/// Browser-callable QR generation API. Invoked by the host script after <c>runMain()</c> completes.
/// <para>
/// CRITICAL: Every <c>[JSExport]</c> method MUST catch all exceptions internally.
/// An unhandled exception propagating through the interop boundary causes the Mono WASM
/// runtime to abort (exit code 1). Once aborted, the runtime cannot be restarted without
/// a full page reload, and all subsequent calls fail with
/// "Assert failed: .NET runtime already exited with 1".
/// </para>
/// </summary>
public static partial class QrInterop
{
    private static SKBitmap? s_defaultLogo;
    private static byte[]? s_customLogoBytes;
    private static SKBitmap? s_customLogoBitmap;
    private static string s_lastMeta = "{}";

    /// <summary>User-facing build version. Exposed to the page script after WASM starts.</summary>
    [JSExport]
    public static string GetProductVersion()
    {
        try
        {
            var assembly = typeof(QrInterop).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(info))
                return assembly.GetName().Version?.ToString(3) ?? "unknown";

            // Strip SourceLink metadata (e.g. "1.2.3+abcdef0")
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Stats of the most recent successful <see cref="Generate"/> call as a JSON string:
    /// <c>{"qrVersion":N,"matrixSize":N,"totalMs":N,"bytes":N}</c>.
    /// </summary>
    [JSExport]
    public static string GetLastMeta() => s_lastMeta;

    /// <summary>
    /// Generates a QR code PNG from JSON options (see <see cref="QrRequest"/>).
    /// Returns PNG bytes on success (first byte 0x89), or UTF-8 JSON
    /// <c>{"error":"..."}</c> on failure (first byte 0x7B) — the JS side
    /// distinguishes the two by the first byte.
    /// </summary>
    /// <param name="optionsJson">Serialized <see cref="QrRequest"/> (camelCase).</param>
    /// <param name="customLogo">Uploaded logo image bytes; empty unless logo mode is "custom".</param>
    [JSExport]
    public static byte[] Generate(string optionsJson, byte[] customLogo)
    {
        try
        {
            var request = JsonSerializer.Deserialize(optionsJson, PlaygroundJsonContext.Default.QrRequest)
                ?? throw new InvalidOperationException("Options JSON deserialized to null.");
            return GenerateCore(request, customLogo);
        }
        catch (Exception ex)
        {
            return SerializeError(ex);
        }
    }

    private static byte[] GenerateCore(QrRequest request, byte[] customLogo)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Content is empty.");

        var stopwatch = Stopwatch.StartNew();
        var data = QRCodeGenerator.CreateQrCode(
            request.Content.AsSpan(),
            ParseEcc(request.Ecc),
            requestedVersion: request.Version,
            quietZoneSize: Math.Clamp(request.QuietZone, 0, 10));

        var bytes = CreateBuilder(request, data, customLogo).ToByteArray();
        stopwatch.Stop();

        s_lastMeta = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"qrVersion\":{data.Version},\"matrixSize\":{data.Size},\"totalMs\":{stopwatch.Elapsed.TotalMilliseconds:F1},\"bytes\":{bytes.Length}}}");
        return bytes;
    }

    /// <summary>Builds the image builder from request options; shared by preview and benchmark rendering.</summary>
    private static QRCodeImageBuilder CreateBuilder(QrRequest request, QRCodeData data, byte[] customLogo)
    {
        var size = Math.Clamp(request.Size, 64, 2048);
        return new QRCodeImageBuilder(data)
            .WithSize(size, size)
            .WithColors(
                ParseColor(request.Foreground, SKColors.Black),
                ParseColor(request.Background, SKColors.White))
            .WithModuleShape(CreateModuleShape(request), Math.Clamp(request.ModuleSizePercent, 0.5f, 1.0f))
            .WithFinderPatternShape(CreateFinderShape(request.FinderShape))
            .WithGradient(CreateGradient(request.Gradient))
            .WithIcon(CreateIcon(request.Logo, customLogo));
    }

    /// <summary>
    /// Runs one benchmark batch and returns stats as a JSON string:
    /// <c>{"count":N,"elapsedMs":N,"qrVersion":N,"matrixSize":N,"bytesTotal":N}</c>,
    /// or <c>{"error":"..."}</c> on failure. The page script chains batches so the UI
    /// stays responsive and can show progress / cancel.
    /// <para>
    /// Content is made unique per iteration by appending <c>" #&lt;index+1&gt;"</c>.
    /// Mode <c>encode</c> exercises the allocation-free
    /// <see cref="QRCodeGenerator.CreateQrCode(ReadOnlySpan{char}, ECCLevel, Span{byte}, bool, EciMode, int, int)"/>
    /// overload only; mode <c>render</c> runs the full pipeline (encode + Skia render + PNG encode)
    /// with the current visual options.
    /// </para>
    /// </summary>
    /// <param name="optionsJson">Serialized <see cref="QrRequest"/> (camelCase).</param>
    /// <param name="mode">"encode" or "render".</param>
    /// <param name="startIndex">Global index of the first iteration in this batch.</param>
    /// <param name="count">Iterations to run in this batch.</param>
    /// <param name="customLogo">Uploaded logo bytes for render mode; empty otherwise.</param>
    [JSExport]
    public static string BenchmarkBatch(string optionsJson, string mode, int startIndex, int count, byte[] customLogo)
    {
        try
        {
            var request = JsonSerializer.Deserialize(optionsJson, PlaygroundJsonContext.Default.QrRequest)
                ?? throw new InvalidOperationException("Options JSON deserialized to null.");
            if (string.IsNullOrWhiteSpace(request.Content))
                throw new ArgumentException("Content is empty.");
            if (count is <= 0 or > 1_000_000)
                throw new ArgumentOutOfRangeException(nameof(count), "Batch count must be between 1 and 1,000,000.");

            return mode switch
            {
                "encode" => BenchmarkEncode(request, startIndex, count),
                "render" => BenchmarkRender(request, startIndex, count, customLogo),
                _ => throw new ArgumentException($"Unknown benchmark mode '{mode}'. Use 'encode' or 'render'."),
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new ErrorPayload(ex.GetBaseException().Message), PlaygroundJsonContext.Default.ErrorPayload);
        }
    }

    /// <summary>
    /// Tight loop over the zero-allocation span API. The per-iteration text is composed in a
    /// pooled char buffer (no string allocation) and the module matrix is written into a
    /// pooled byte buffer sized for QR version 40, so the loop itself allocates nothing.
    /// </summary>
    private static string BenchmarkEncode(QrRequest request, int startIndex, int count)
    {
        var ecc = ParseEcc(request.Ecc);
        var quietZone = Math.Clamp(request.QuietZone, 0, 10);
        var prefixLength = request.Content.Length;

        // " #" + int.MaxValue digits
        var textBuffer = ArrayPool<char>.Shared.Rent(prefixLength + 2 + 11);
        // Version 40 with quiet zone is the largest possible matrix.
        var maxSide = 177 + 2 * quietZone;
        var moduleBuffer = ArrayPool<byte>.Shared.Rent(maxSide * maxSide);
        try
        {
            request.Content.AsSpan().CopyTo(textBuffer);
            textBuffer[prefixLength] = ' ';
            textBuffer[prefixLength + 1] = '#';

            long bytesTotal = 0;
            var written = 0;
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < count; i++)
            {
                (startIndex + i + 1).TryFormat(textBuffer.AsSpan(prefixLength + 2), out var digits);
                var text = textBuffer.AsSpan(0, prefixLength + 2 + digits);
                written = QRCodeGenerator.CreateQrCode(text, ecc, moduleBuffer, requestedVersion: request.Version, quietZoneSize: quietZone);
                bytesTotal += written;
            }
            stopwatch.Stop();

            var matrixSize = (int)Math.Sqrt(written);
            var qrVersion = (matrixSize - 2 * quietZone - 17) / 4;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"count\":{count},\"elapsedMs\":{stopwatch.Elapsed.TotalMilliseconds:F2},\"qrVersion\":{qrVersion},\"matrixSize\":{matrixSize},\"bytesTotal\":{bytesTotal}}}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(moduleBuffer, clearArray: false);
            ArrayPool<char>.Shared.Return(textBuffer, clearArray: false);
        }
    }

    /// <summary>Full pipeline per iteration: encode, Skia render with current options, PNG encode.</summary>
    private static string BenchmarkRender(QrRequest request, int startIndex, int count, byte[] customLogo)
    {
        var ecc = ParseEcc(request.Ecc);
        var quietZone = Math.Clamp(request.QuietZone, 0, 10);

        long bytesTotal = 0;
        var qrVersion = 0;
        var matrixSize = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"{request.Content} #{startIndex + i + 1}");
            var data = QRCodeGenerator.CreateQrCode(text.AsSpan(), ecc, requestedVersion: request.Version, quietZoneSize: quietZone);
            qrVersion = data.Version;
            matrixSize = data.Size;
            bytesTotal += CreateBuilder(request, data, customLogo).ToByteArray().Length;
        }
        stopwatch.Stop();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"count\":{count},\"elapsedMs\":{stopwatch.Elapsed.TotalMilliseconds:F2},\"qrVersion\":{qrVersion},\"matrixSize\":{matrixSize},\"bytesTotal\":{bytesTotal}}}");
    }

    private static ECCLevel ParseEcc(string ecc) => ecc.ToUpperInvariant() switch
    {
        "L" => ECCLevel.L,
        "M" => ECCLevel.M,
        "Q" => ECCLevel.Q,
        "H" => ECCLevel.H,
        _ => throw new ArgumentException($"Unknown ECC level '{ecc}'. Use L, M, Q or H."),
    };

    private static SKColor ParseColor(string value, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return SKColors.Transparent;
        if (SKColor.TryParse(value, out var color))
            return color;
        throw new ArgumentException($"Could not parse color '{value}'. Use #RRGGBB or 'transparent'.");
    }

    private static ModuleShape? CreateModuleShape(QrRequest request) => request.ModuleShape switch
    {
        "circle" => CircleModuleShape.Default,
        "rounded" => new RoundedRectangleModuleShape(Math.Clamp(request.ModuleCornerRadius, 0f, 1f)),
        _ => null, // rectangle (default)
    };

    private static FinderPatternShape? CreateFinderShape(string finderShape) => finderShape switch
    {
        "rectangle" => RectangleFinderPatternShape.Default,
        "circle" => CircleFinderPatternShape.Default,
        "rounded" => RoundedRectangleFinderPatternShape.Default,
        "roundedCircle" => RoundedRectangleCircleFinderPatternShape.Default,
        _ => null, // auto: standard pattern, or module shape when one is set
    };

    private static GradientOptions? CreateGradient(GradientDto? gradient)
    {
        if (gradient is null || !gradient.Enabled || gradient.Colors.Length < 2)
            return null;

        var direction = Enum.TryParse<GradientDirection>(gradient.Direction, ignoreCase: true, out var parsed)
            ? parsed
            : GradientDirection.TopLeftToBottomRight;
        if (direction == GradientDirection.None)
            return null;

        var colors = new SKColor[gradient.Colors.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = ParseColor(gradient.Colors[i], SKColors.Black);
        }
        return new GradientOptions(colors, direction);
    }

    private static IconData? CreateIcon(LogoDto? logo, byte[] customLogo)
    {
        if (logo is null)
            return null;

        var bitmap = logo.Mode switch
        {
            "default" => s_defaultLogo ??= CreateDefaultLogo(),
            "custom" => DecodeCustomLogo(customLogo),
            _ => null, // none
        };
        if (bitmap is null)
            return null;

        return IconData.FromImage(
            bitmap,
            iconSizePercent: Math.Clamp(logo.SizePercent, 1, 40),
            iconBorderWidth: Math.Clamp(logo.BorderWidth, 0, 64));
    }

    /// <summary>
    /// Decodes the uploaded logo, caching the bitmap keyed by content so slider-driven
    /// realtime regeneration does not re-decode the same image on every call.
    /// </summary>
    private static SKBitmap? DecodeCustomLogo(byte[] bytes)
    {
        if (bytes.Length == 0)
            return null;
        if (s_customLogoBitmap is not null && s_customLogoBytes is not null && bytes.AsSpan().SequenceEqual(s_customLogoBytes))
            return s_customLogoBitmap;

        var bitmap = SKBitmap.Decode(bytes)
            ?? throw new ArgumentException("Could not decode the uploaded logo image. Use PNG, JPEG or WebP.");
        s_customLogoBitmap?.Dispose();
        s_customLogoBytes = bytes;
        s_customLogoBitmap = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Draws the built-in logo: an Instagram-style camera glyph on a warm-to-purple
    /// gradient rounded square. Rendered with SkiaSharp itself, so no binary asset is shipped.
    /// </summary>
    private static SKBitmap CreateDefaultLogo()
    {
        const int S = 256;
        var bitmap = new SKBitmap(new SKImageInfo(S, S, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Gradient rounded square (bottom-left warm to top-right purple).
        using var background = new SKPaint { IsAntialias = true };
        background.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, S),
            new SKPoint(S, 0),
            [SKColor.Parse("#FA7E1E"), SKColor.Parse("#D62976"), SKColor.Parse("#962FBF")],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(SKRect.Create(0, 0, S, S), S * 0.22f, S * 0.22f, background);

        // White camera outline: body, lens, flash dot.
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = S * 0.055f,
            Color = SKColors.White,
            StrokeCap = SKStrokeCap.Round,
        };
        var inset = S * 0.19f;
        canvas.DrawRoundRect(SKRect.Create(inset, inset, S - 2 * inset, S - 2 * inset), S * 0.12f, S * 0.12f, stroke);
        canvas.DrawCircle(S / 2f, S / 2f, S * 0.15f, stroke);
        using var dot = new SKPaint { IsAntialias = true, Color = SKColors.White };
        canvas.DrawCircle(S * 0.685f, S * 0.315f, S * 0.033f, dot);

        return bitmap;
    }

    private static byte[] SerializeError(Exception ex)
    {
        var message = ex.GetBaseException().Message;
        return JsonSerializer.SerializeToUtf8Bytes(new ErrorPayload(message), PlaygroundJsonContext.Default.ErrorPayload);
    }
}

/// <summary>QR generation options passed from the page script as camelCase JSON.</summary>
public sealed record QrRequest
{
    public string Content { get; init; } = "";
    /// <summary>Error correction level: L, M, Q or H.</summary>
    public string Ecc { get; init; } = "M";
    /// <summary>Output image size in pixels (square).</summary>
    public int Size { get; init; } = 512;
    /// <summary>Quiet zone in modules (0-10).</summary>
    public int QuietZone { get; init; } = 4;
    /// <summary>QR version 1-40, or -1 for automatic selection.</summary>
    public int Version { get; init; } = -1;
    /// <summary>Module shape: rectangle, circle or rounded.</summary>
    public string ModuleShape { get; init; } = "rectangle";
    /// <summary>Module size as a fraction of the cell (0.5-1.0).</summary>
    public float ModuleSizePercent { get; init; } = 1.0f;
    /// <summary>Corner radius fraction for rounded modules (0.0-1.0).</summary>
    public float ModuleCornerRadius { get; init; } = 0.3f;
    /// <summary>Finder pattern shape: auto, rectangle, circle, rounded or roundedCircle.</summary>
    public string FinderShape { get; init; } = "auto";
    /// <summary>Module color as #RRGGBB.</summary>
    public string Foreground { get; init; } = "#000000";
    /// <summary>Background color as #RRGGBB or 'transparent'.</summary>
    public string Background { get; init; } = "#FFFFFF";
    public GradientDto? Gradient { get; init; }
    public LogoDto? Logo { get; init; }
}

public sealed record GradientDto
{
    public bool Enabled { get; init; }
    /// <summary>Gradient stops as #RRGGBB (2 or more).</summary>
    public string[] Colors { get; init; } = [];
    /// <summary>A <see cref="GradientDirection"/> member name.</summary>
    public string Direction { get; init; } = "TopLeftToBottomRight";
}

public sealed record LogoDto
{
    /// <summary>Logo mode: none, default (built-in camera glyph) or custom (uploaded image).</summary>
    public string Mode { get; init; } = "none";
    /// <summary>Logo size as a percentage of the QR side length (1-40).</summary>
    public int SizePercent { get; init; } = 16;
    /// <summary>Border padding around the logo in pixels (0-64).</summary>
    public int BorderWidth { get; init; } = 6;
}

public sealed record ErrorPayload(string Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QrRequest))]
[JsonSerializable(typeof(ErrorPayload))]
internal sealed partial class PlaygroundJsonContext : JsonSerializerContext;
