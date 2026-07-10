using SkiaSharp.QrCode.Image;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The renderer collapses horizontal runs of dark modules into single rects for the
/// default rectangle shape at 100% module size. These tests pin that the merged
/// fast path is pixel-identical to per-module drawing (forced through a rect shape
/// of a different type, which bypasses the fast-path type check).
/// </summary>
public class QRCodeRendererRunMergeParityTest
{
    // Draws exactly like RectangleModuleShape, but as a different type the renderer
    // routes it through the per-module path instead of the run-merged fast path.
    private sealed class PerModuleRectangleShape : ModuleShape
    {
        public override bool RequiresAntialiasing => false;
        public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint) => canvas.DrawRect(rect, paint);
    }

    [Theory]
    [InlineData("HELLO WORLD 2026", 0, 290)] // v1-2, no quiet zone, exact multiple
    [InlineData("HELLO WORLD 2026", 4, 290)] // v1-2, standard quiet zone
    [InlineData("HELLO WORLD 2026", 4, 411)] // fractional cell size
    [InlineData("https://github.com/guitarrapc/SkiaSharp.QrCode/blob/main/README.md?foo=sample&bar=dummy", 4, 512)] // v~5
    public void MergedRuns_MatchPerModuleRendering(string content, int quietZone, int imageSize)
    {
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, quietZoneSize: quietZone);

        var merged = RenderPixels(qr, imageSize, moduleShape: null);
        var perModule = RenderPixels(qr, imageSize, moduleShape: new PerModuleRectangleShape());

        Assert.Equal(perModule, merged);
    }

    [Fact]
    public void MergedRuns_WithGradient_MatchPerModuleRendering()
    {
        var qr = QRCodeGenerator.CreateQrCode("gradient-run-merge-parity", ECCLevel.M);
        var gradient = new GradientOptions([SKColors.Purple, SKColors.Orange], GradientDirection.TopLeftToBottomRight);

        var merged = RenderPixels(qr, 512, moduleShape: null, gradientOptions: gradient);
        var perModule = RenderPixels(qr, 512, moduleShape: new PerModuleRectangleShape(), gradientOptions: gradient);

        Assert.Equal(perModule, merged);
    }

    [Fact]
    public void MergedRuns_WithFinderPatternShape_MatchPerModuleRendering()
    {
        // Runs must break at finder pattern modules so the custom finder shape
        // is not painted over.
        var qr = QRCodeGenerator.CreateQrCode("finder-run-merge-parity", ECCLevel.M);

        var merged = RenderPixels(qr, 512, moduleShape: null, finderPatternShape: RectangleFinderPatternShape.Default);
        var perModule = RenderPixels(qr, 512, moduleShape: new PerModuleRectangleShape(), finderPatternShape: RectangleFinderPatternShape.Default);

        Assert.Equal(perModule, merged);
    }

    private static byte[] RenderPixels(
        QRCodeData qr,
        int imageSize,
        ModuleShape? moduleShape,
        GradientOptions? gradientOptions = null,
        FinderPatternShape? finderPatternShape = null)
    {
        var area = SKRect.Create(0, 0, imageSize, imageSize);
        using var bitmap = new SKBitmap(imageSize, imageSize);
        using var canvas = new SKCanvas(bitmap);

        QRCodeRenderer.Render(
            canvas,
            area,
            qr,
            SKColors.Black,
            SKColors.White,
            moduleShape: moduleShape,
            gradientOptions: gradientOptions,
            finderPatternShape: finderPatternShape);

        return bitmap.Bytes;
    }
}
