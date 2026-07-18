using SkiaSharp;

namespace QRInteropFixtures;

/// <summary>
/// Renders a core module matrix to a clean black-on-white PNG (fixed quiet zone and
/// pixels-per-module) for the image-decoding fixture path.
/// </summary>
public static class PngRenderer
{
    public static byte[] Render(byte[] modules, int size, int quietZoneModules, int pixelsPerModule)
    {
        var totalPixels = (size + quietZoneModules * 2) * pixelsPerModule;

        using var bitmap = new SKBitmap(totalPixels, totalPixels, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                if (modules[row * size + col] == 0)
                    continue;

                var x = (quietZoneModules + col) * pixelsPerModule;
                var y = (quietZoneModules + row) * pixelsPerModule;
                canvas.DrawRect(x, y, pixelsPerModule, pixelsPerModule, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
