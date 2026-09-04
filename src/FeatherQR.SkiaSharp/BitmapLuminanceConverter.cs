using SkiaSharp;
using FeatherQR.Internals.ImageDecoders;

namespace FeatherQR.SkiaSharp;

/// <summary>
/// Converts SkiaSharp bitmaps to 8-bit grayscale luminance buffers.
/// </summary>
/// <remarks>
/// Reads the pixel layout out of the bitmap and hands the bytes to the core kernels
/// (<see cref="LuminanceConverter"/>). Fast paths cover the color types QR sources
/// actually use (Gray8, Bgra8888, Rgba8888, Rgb888x); anything else is redrawn once
/// into Bgra8888. Transparent pixels are composited against white, QR quiet zones
/// are white by definition, and transparent-background PNGs are a common input.
/// </remarks>
internal static class BitmapLuminanceConverter
{
    /// <summary>
    /// Converts bitmap pixels to luminance (width × height bytes, row-major).
    /// </summary>
    /// <param name="bitmap">Source bitmap.</param>
    /// <param name="luminance">Destination buffer, at least Width × Height bytes.</param>
    public static void Convert(SKBitmap bitmap, Span<byte> luminance)
    {
        using (var pixmap = bitmap.PeekPixels())
        {
            if (pixmap is not null && TryConvertPixmap(pixmap, luminance))
                return;
        }

        // Unsupported layout: redraw once into a known format (rare path)
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var converted = new SKBitmap(info);
        using (var canvas = new SKCanvas(converted))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default);
        }
        using var convertedPixmap = converted.PeekPixels();
        if (convertedPixmap is null || !TryConvertPixmap(convertedPixmap, luminance))
            throw new NotSupportedException($"Unsupported bitmap color type: {bitmap.ColorType}");
    }

    private static bool TryConvertPixmap(SKPixmap pixmap, Span<byte> luminance)
    {
        var width = pixmap.Width;
        var height = pixmap.Height;
        var rowBytes = pixmap.RowBytes;
        var pixels = pixmap.GetPixelSpan();
        var premultiplied = pixmap.AlphaType == SKAlphaType.Premul;

        switch (pixmap.ColorType)
        {
            case SKColorType.Gray8:
                LuminanceConverter.ConvertGray8(pixels, luminance, width, height, rowBytes);
                return true;

            case SKColorType.Bgra8888:
                LuminanceConverter.ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset: 2, greenOffset: 1, blueOffset: 0, alphaOffset: 3, premultiplied);
                return true;

            case SKColorType.Rgba8888:
                LuminanceConverter.ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset: 0, greenOffset: 1, blueOffset: 2, alphaOffset: 3, premultiplied);
                return true;

            case SKColorType.Rgb888x:
                LuminanceConverter.ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset: 0, greenOffset: 1, blueOffset: 2, alphaOffset: -1, premultiplied: false);
                return true;

            default:
                return false;
        }
    }
}
