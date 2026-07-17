using SkiaSharp;
using System.Text;

/// <summary>
/// End-to-end QR decoding through the public API (QRCodeDecoder).
/// Payloads mirror QrCodeEndToEnd so encode and decode costs are directly comparable,
/// plus an image-pipeline scenario (detection + sampling).
/// Matrices are quiet-zone-free (the decoder's in-place fast path).
///
/// Scenarios:
///   Short_M : version ~1-2 module matrix -> text
///   Url_M   : typical URL payload, version ~4-5
///   Long_L  : ~2900 chars, version 40-L (largest data volume)
///   Long_H  : ~1200 chars, version 40-H (81 blocks, max ECC share)
///   Image_Url_M : rendered bitmap luminance -> text (binarize + finder detection + sampling)
/// </summary>
public class QrCodeDecodeEndToEnd
{
    private byte[] _shortModules = default!;
    private int _shortSize;
    private byte[] _urlModules = default!;
    private int _urlSize;
    private byte[] _longLModules = default!;
    private int _longLSize;
    private byte[] _longHModules = default!;
    private int _longHSize;
    private char[] _chars = default!;

    private byte[] _urlLuminance = default!;
    private int _urlImageSize;

    [GlobalSetup]
    public void GlobalSetup()
    {
        (_shortModules, _shortSize) = BuildModules("HELLO WORLD 2026", ECCLevel.M);
        (_urlModules, _urlSize) = BuildModules("https://github.com/guitarrapc/SkiaSharp.QrCode/blob/main/README.md?foo=sample&bar=dummy", ECCLevel.M);
        (_longLModules, _longLSize) = BuildModules(BuildDeterministicText(2900), ECCLevel.L);
        (_longHModules, _longHSize) = BuildModules(BuildDeterministicText(1200), ECCLevel.H);
        _chars = new char[QRCodeDecoder.GetMaxDecodedLength(40)];

        (_urlLuminance, _urlImageSize) = RenderLuminance("https://github.com/guitarrapc/SkiaSharp.QrCode/blob/main/README.md?foo=sample&bar=dummy", ECCLevel.M, pixelsPerModule: 8);
    }

    // Span destination (zero-allocation) path

    [Benchmark(Baseline = true, Description = "Short_M_Decode (Span)")]
    public int Short_M_DecodeSpan()
    {
        QRCodeDecoder.TryDecode(_shortModules, _shortSize, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "Url_M_Decode (Span)")]
    public int Url_M_DecodeSpan()
    {
        QRCodeDecoder.TryDecode(_urlModules, _urlSize, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "Long_L_Decode (Span)")]
    public int Long_L_DecodeSpan()
    {
        QRCodeDecoder.TryDecode(_longLModules, _longLSize, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "Long_H_Decode (Span)")]
    public int Long_H_DecodeSpan()
    {
        QRCodeDecoder.TryDecode(_longHModules, _longHSize, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "Image_Url_M_Decode (Span)")]
    public int Image_Url_M_DecodeSpan()
    {
        QRCodeDecoder.TryDecodeImage(_urlLuminance, _urlImageSize, _urlImageSize, _chars, out var written, out _);
        return written;
    }

    // String path (allocates the result string only)

    [Benchmark]
    public string Short_M_Decode()
    {
        QRCodeDecoder.TryDecode(_shortModules, _shortSize, out var text, out _);
        return text;
    }

    [Benchmark]
    public string Url_M_Decode()
    {
        QRCodeDecoder.TryDecode(_urlModules, _urlSize, out var text, out _);
        return text;
    }

    private static (byte[] modules, int size) BuildModules(string content, ECCLevel eccLevel)
    {
        var calculated = QRCodeGenerator.GetRequiredBufferSize(content, eccLevel, quietZoneSize: 0);
        var buffer = new byte[calculated.BufferSize];
        QRCodeGenerator.CreateQrCode(content, eccLevel, buffer, quietZoneSize: 0);
        return (buffer, calculated.QrSize);
    }

    private static (byte[] luminance, int size) RenderLuminance(string content, ECCLevel eccLevel, int pixelsPerModule)
    {
        var qr = QRCodeGenerator.CreateQrCode(content, eccLevel);
        var sizePx = qr.Size * pixelsPerModule;
        using var bitmap = new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Gray8));
        using (var canvas = new SKCanvas(bitmap))
        {
            QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, sizePx, sizePx), qr, SKColors.Black, SKColors.White);
            canvas.Flush();
        }

        var luminance = new byte[sizePx * sizePx];
        using var pixmap = bitmap.PeekPixels();
        var pixels = pixmap.GetPixelSpan();
        for (var y = 0; y < sizePx; y++)
        {
            pixels.Slice(y * pixmap.RowBytes, sizePx).CopyTo(luminance.AsSpan(y * sizePx, sizePx));
        }
        return (luminance, sizePx);
    }

    private static string BuildDeterministicText(int length)
    {
        var sb = new StringBuilder(length);
        var rng = new Random(42);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:/?&=-_";
        for (var i = 0; i < length; i++)
        {
            sb.Append(alphabet[rng.Next(alphabet.Length)]);
        }
        return sb.ToString();
    }
}
