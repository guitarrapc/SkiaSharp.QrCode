namespace SkiaSharp.QrCode.Internals.ImageDecoders;

/// <summary>
/// Converts SkiaSharp bitmaps to 8-bit grayscale luminance buffers.
/// </summary>
/// <remarks>
/// Fast paths cover the color types QR sources actually use (Gray8, Bgra8888,
/// Rgba8888, Rgb888x); anything else is redrawn once into Bgra8888. Transparent
/// pixels are composited against white, QR quiet zones are white by definition,
/// and transparent-background PNGs are a common input.
/// </remarks>
internal static partial class LuminanceConverter
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
                for (var y = 0; y < height; y++)
                {
                    pixels.Slice(y * rowBytes, width).CopyTo(luminance.Slice(y * width, width));
                }
                return true;

            case SKColorType.Bgra8888:
                ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset: 2, greenOffset: 1, blueOffset: 0, alphaOffset: 3, premultiplied);
                return true;

            case SKColorType.Rgba8888:
                ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset: 0, greenOffset: 1, blueOffset: 2, alphaOffset: 3, premultiplied);
                return true;

            case SKColorType.Rgb888x:
                ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset: 0, greenOffset: 1, blueOffset: 2, alphaOffset: -1, premultiplied: false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Pixel buffer to BT.601 luminance. Three tiers: an AVX2 kernel at 32 pixels per
    /// iteration (LuminanceConverter.Simd.cs), a NEON kernel at 16 pixels per
    /// iteration (LuminanceConverter.Simd.Arm.cs), and this per-pixel loop everywhere
    /// else. All three produce identical bytes; see LuminanceConverterParityTest.
    /// </summary>
    private static void ConvertRgba(ReadOnlySpan<byte> pixels, Span<byte> luminance, int width, int height, int rowBytes, int redOffset, int greenOffset, int blueOffset, int alphaOffset, bool premultiplied)
    {
#if NET8_0_OR_GREATER
        // The only layouts reaching here are BGRA (2,1,0) and RGBA / RGB888x (0,1,2),
        // which is what the vector kernels cover.
        if (IsVectorLayout(redOffset, greenOffset, blueOffset))
        {
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                ConvertRgbaAvx2(pixels, luminance, width, height, rowBytes, bgra: redOffset == 2, hasAlpha: alphaOffset >= 0, premultiplied);
                return;
            }

            // The NEON kernel covers a whole row from 16-pixel blocks plus one
            // overlapping final block, so it has no scalar remainder to fall back on
            // and narrower rows stay on the per-pixel loop instead.
            if (IsAdvSimdTierAvailable && width >= AdvSimdBlockPixels)
            {
                ConvertRgbaAdvSimd(pixels, luminance, width, height, rowBytes, bgra: redOffset == 2, hasAlpha: alphaOffset >= 0, premultiplied);
                return;
            }
        }
#endif
        ConvertRgbaScalar(pixels, luminance, width, height, rowBytes, redOffset, greenOffset, blueOffset, alphaOffset, premultiplied);
    }

#if NET8_0_OR_GREATER
    /// <summary>BGRA (2,1,0) or RGBA / RGB888x (0,1,2); nothing else reaches a vector tier.</summary>
    private static bool IsVectorLayout(int redOffset, int greenOffset, int blueOffset)
        => greenOffset == 1 && (redOffset == 2 ? blueOffset == 0 : redOffset == 0 && blueOffset == 2);

    /// <summary>
    /// UDOT carries the whole luminance sum and LD4 the composite path, so both are
    /// required rather than plain AdvSimd.
    /// </summary>
    private static bool IsAdvSimdTierAvailable
        => System.Runtime.Intrinsics.Arm.Dp.IsSupported && System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported;
#endif

    /// <summary>Whether the AVX2 tier runs on this machine (parity tests skip it otherwise).</summary>
    internal static bool IsAvx2TierSupported =>
#if NET8_0_OR_GREATER
        System.Runtime.Intrinsics.X86.Avx2.IsSupported;
#else
        false;
#endif

    /// <summary>Whether the NEON tier runs on this machine (parity tests skip it otherwise).</summary>
    internal static bool IsAdvSimdTierSupported =>
#if NET8_0_OR_GREATER
        IsAdvSimdTierAvailable;
#else
        false;
#endif

    /// <summary>Tier-selecting entry for parity tests; behavior-identical to <see cref="ConvertRgba"/>.</summary>
    internal static void ConvertRgbaForTest(ReadOnlySpan<byte> pixels, Span<byte> luminance, int width, int height, int rowBytes, int redOffset, int greenOffset, int blueOffset, int alphaOffset, bool premultiplied, bool forceScalar)
    {
        if (forceScalar)
        {
            ConvertRgbaScalar(pixels, luminance, width, height, rowBytes, redOffset, greenOffset, blueOffset, alphaOffset, premultiplied);
            return;
        }
        ConvertRgba(pixels, luminance, width, height, rowBytes, redOffset, greenOffset, blueOffset, alphaOffset, premultiplied);
    }

    private static void ConvertRgbaScalar(ReadOnlySpan<byte> pixels, Span<byte> luminance, int width, int height, int rowBytes, int redOffset, int greenOffset, int blueOffset, int alphaOffset, bool premultiplied)
    {
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * rowBytes, width * 4);
            var dest = luminance.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var p = x * 4;
                int r = row[p + redOffset];
                int g = row[p + greenOffset];
                int b = row[p + blueOffset];

                if (alphaOffset >= 0)
                {
                    int a = row[p + alphaOffset];
                    if (a != 255)
                    {
                        // Composite against white (the quiet zone color)
                        if (premultiplied)
                        {
                            // Premultiplied channels already carry c·a/255
                            var white = 255 - a;
                            r += white;
                            g += white;
                            b += white;
                        }
                        else
                        {
                            r = (r * a + 255 * (255 - a)) / 255;
                            g = (g * a + 255 * (255 - a)) / 255;
                            b = (b * a + 255 * (255 - a)) / 255;
                        }
                    }
                }

                // ITU-R BT.601 integer luma
                dest[x] = (byte)((77 * r + 150 * g + 29 * b) >> 8);
            }
        }
    }
}
