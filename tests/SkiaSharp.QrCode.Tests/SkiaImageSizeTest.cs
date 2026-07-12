namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Visual compatibility test using pixel compatison instead of binary comparison.
/// </summary>
public class SkiaImageSizeTest
{
    [Test]
    public async Task SKImageInfoBaseTest()
    {
        // no param
        var a = new SKImageInfo();
        await Assert.That(a.Size.Height).IsEquivalentTo(0);
        await Assert.That(a.Size.Width).IsEquivalentTo(0);
        await Assert.That(a.BytesSize).IsEquivalentTo(0);
        await Assert.That(a.AlphaType).IsEquivalentTo(SKAlphaType.Unknown);
        await Assert.That(a.ColorType).IsEquivalentTo(SKColorType.Unknown);

        // size
        var b = new SKImageInfo(100, 100);
        await Assert.That(b.Size.Height).IsEquivalentTo(100);
        await Assert.That(b.Size.Width).IsEquivalentTo(100);
        await Assert.That(b.BytesSize).IsEquivalentTo(40000);
        await Assert.That(b.AlphaType).IsEquivalentTo(SKAlphaType.Premul);
        // The default color type is platform-dependent (Bgra8888 on Windows/Linux,
        // Rgba8888 on macOS), so compare against SkiaSharp's platform default.
        await Assert.That(b.ColorType).IsEquivalentTo(SKImageInfo.PlatformColorType);
    }

    [Test]
    public async Task SKImageInfoColorTest()
    {
        foreach (SKColorType colorType in Enum.GetValues(typeof(SKColorType)))
        {
            var image = new SKImageInfo(100, 100, colorType);
            await Assert.That(image.Size.Height).IsEquivalentTo(100);
            await Assert.That(image.Size.Width).IsEquivalentTo(100);
            await Assert.That(image.AlphaType).IsEquivalentTo(SKAlphaType.Premul);
            await Assert.That(image.ColorType).IsEquivalentTo(colorType);

            var size = GetSizeByColorType(colorType);
            await Assert.That(image.BytesSize).IsEquivalentTo(size);
        }
    }

    [Test]
    public async Task SKImageInfoAlphaTest()
    {
        foreach (SKColorType colorType in Enum.GetValues(typeof(SKColorType)))
        {
            foreach (SKAlphaType alphaType in Enum.GetValues(typeof(SKAlphaType)))
            {
                var image = new SKImageInfo(100, 100, colorType, alphaType);
                await Assert.That(image.Size.Height).IsEquivalentTo(100);
                await Assert.That(image.Size.Width).IsEquivalentTo(100);
                await Assert.That(image.AlphaType).IsEquivalentTo(alphaType);
                await Assert.That(image.ColorType).IsEquivalentTo(colorType);

                var size = GetSizeByColorType(colorType);
                await Assert.That(image.BytesSize).IsEquivalentTo(size);
            }
        }
    }

    private static int GetSizeByColorType(SKColorType colorType)
    {
        return colorType switch
        {
            SKColorType.Unknown => 0,
            SKColorType.Alpha8 => 10000,
            // R8 Unorm (normalized 8-bit red channel only), Total: 8 bits = 1byte
            // Red: 8 bits, Total: 8 bits = 1byte
            // 100 * 100 * 1 = 10000
            SKColorType.R8Unorm => 10000,
            SKColorType.RF16 => 20000,
            SKColorType.R16Unorm => 20000,
            SKColorType.Rgb565 => 20000,
            SKColorType.Argb4444 => 20000,
            SKColorType.Rgba8888 => 40000,
            SKColorType.Rgb888x => 40000,
            SKColorType.Bgra8888 => 40000,
            // sRGB RGBA Formats with each channel is 8 bits, Total: 32 bits = 4bytes
            // Red: 8 bits, Green: 8 bits, Blue: 8 bits, Alpha: 8 bits, Total: 32 bits = 4bytes
            // 100 * 100 * 4 = 40000
            SKColorType.Srgba8888 => 40000,
            // RGBA Formats with each channel is 10 bits, Alpha: 2 bits, Total: 32 bits = 4bytes
            // Red: 10 bits, Green: 10 bits, Blue: 10 bits, Alpha: 2 bits, Total: 32 bits = 4bytes
            // 100 * 100 * 4 = 40000
            SKColorType.Rgba1010102 => 40000,
            // RGB Formats with each channel is 10 bits, Unused: 2 bits, Total: 32 bits = 4bytes
            // Red: 10 bits, Green: 10 bits, Blue: 10 bits, Unused: 2 bits, Total: 32 bits = 4bytes
            // 100 * 100 * 4 = 40000
            SKColorType.Rgb101010x => 40000,
            SKColorType.Gray8 => 10000,
            SKColorType.RgbaF16 => 80000,
            SKColorType.RgbaF16Clamped => 80000,
            SKColorType.RgbaF32 => 160000,
            SKColorType.Rg88 => 20000,
            SKColorType.AlphaF16 => 20000,
            SKColorType.RgF16 => 40000,
            SKColorType.Alpha16 => 20000,
            SKColorType.Rg1616 => 40000,
            // RGBA Formats with each channel is 16 bits, Total: 64 bits = 8bytes
            // Red: 16 bits, Green: 16 bits, Blue: 16 bits, Alpha: 16 bits, Total: 64 bits = 8bytes
            // 100 * 100 * 8 = 80000
            SKColorType.Rgba16161616 => 80000,
            SKColorType.RgbF16F16F16x => 80000,
            // RGBA Formats with each channel is 10 bits (padded to 16 bits), Total: 64 bits = 8bytes
            // Red: 10 bits (16 bits), Green: 10 bits (16 bits), Blue: 10 bits (16 bits), Alpha: 10 bits (16 bits), Total: 64 bits = 8bytes
            // 100 * 100 * 8 = 80000
            SKColorType.Rgba10x6 => 80000,
            // BGRA Formats with each channel is 10 bits, Alpha: 2 bits, Total: 32 bits = 4bytes
            // Blue: 10 bits, Green: 10 bits, Red: 10 bits, Alpha: 2 bits, Total: 32 bits = 4bytes
            // 100 * 100 * 4 = 40000
            SKColorType.Bgra1010102 => 40000,
            SKColorType.Bgra10101010XR => 80000,
            // BGR Formats with each channel is 10 bits, Unused: 2 bits, Total: 32 bits = 4bytes
            // Blue: 10 bits, Green: 10 bits, Red: 10 bits, Unused: 2 bits, Total: 32 bits = 4bytes
            // 100 * 100 * 4 = 40000
            SKColorType.Bgr101010x => 40000,
            // BGR Formats with each channel is 10 bits (eXtended Range) Color Type
            // Blue: 10 bits, Green: 10 bits, Red: 10 bits, Unused: 2 bits, Total: 32 bits = 4bytes
            // 100 * 100 * 4 = 40000
            SKColorType.Bgr101010xXR => 40000,
            _ => throw new NotImplementedException($"{nameof(colorType)} {colorType}")
        };
    }
}
