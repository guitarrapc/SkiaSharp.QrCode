using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode.Tests;

public class FinderPatternShapeColorTest
{
    [Test]
    public async Task CustomFinderPatternShape_ReceivesConfiguredBackgroundPaint()
    {
        var backgroundColor = SKColors.Yellow;
        var qr = QRCodeGenerator.CreateQrCode("finder-background-paint-test", ECCLevel.M);
        var imageSize = qr.Size * 10;
        using var bitmap = new SKBitmap(imageSize, imageSize);
        using var canvas = new SKCanvas(bitmap);
        var finderPatternShape = new BackgroundPaintFinderPatternShape();

        QRCodeRenderer.Render(
            canvas,
            SKRect.Create(0, 0, imageSize, imageSize),
            qr,
            SKColors.Black,
            backgroundColor,
            finderPatternShape: finderPatternShape);

        await Assert.That(finderPatternShape.BackgroundPaintDrawCount).IsEqualTo(3);
        await Assert.That(finderPatternShape.BackgroundColorDrawCount).IsEqualTo(0);
        await Assert.That(finderPatternShape.ReceivedBackgroundColor).IsEqualTo(backgroundColor);
    }

    [Test]
    public async Task LegacyCustomFinderPatternShape_ColorOverloadRemainsCompatible()
    {
        var backgroundColor = SKColors.Yellow;
        var qr = QRCodeGenerator.CreateQrCode("legacy-finder-shape-test", ECCLevel.M);
        using var bitmap = new SKBitmap(qr.Size * 10, qr.Size * 10);
        using var canvas = new SKCanvas(bitmap);
        var finderPatternShape = new LegacyBackgroundColorFinderPatternShape();

        QRCodeRenderer.Render(
            canvas,
            SKRect.Create(0, 0, bitmap.Width, bitmap.Height),
            qr,
            SKColors.Black,
            backgroundColor,
            finderPatternShape: finderPatternShape);

        await Assert.That(finderPatternShape.DrawCount).IsEqualTo(3);
        await Assert.That(finderPatternShape.ReceivedBackgroundColor).IsEqualTo(backgroundColor);
    }

    [Test]
    [MethodDataSource(nameof(GetFinderPatternShapes))]
    public async Task CustomFinderPatternShape_UsesBackgroundColorForInnerRing(FinderPatternShape finderPatternShape)
    {
        var backgroundColor = new SKColor(0xEA, 0xFB, 0x00, 0xFF);
        var codeColor = SKColors.Green;
        var qr = QRCodeGenerator.CreateQrCode("finder-shape-background-test", ECCLevel.M);

        var imageSize = qr.Size * 10;
        var area = SKRect.Create(0, 0, imageSize, imageSize);
        using var bitmap = new SKBitmap(imageSize, imageSize);
        using var canvas = new SKCanvas(bitmap);

        QRCodeRenderer.Render(
            canvas,
            area,
            qr,
            codeColor,
            backgroundColor,
            finderPatternShape: finderPatternShape);

        var finderRect = QRCodeRenderer.GetFinderPatternRect(qr, 0, area);
        var moduleSize = finderRect.Width / 7f;
        var ringSampleX = (int)MathF.Round(finderRect.Left + moduleSize * 1.5f);
        var ringSampleY = (int)MathF.Round(finderRect.Top + moduleSize * 3.5f);
        var centerSampleX = (int)MathF.Round(finderRect.Left + moduleSize * 3.5f);
        var centerSampleY = (int)MathF.Round(finderRect.Top + moduleSize * 3.5f);

        var ringPixel = bitmap.GetPixel(ringSampleX, ringSampleY);
        var centerPixel = bitmap.GetPixel(centerSampleX, centerSampleY);

        await Assert.That(ringPixel).IsEquivalentTo(backgroundColor);
        await Assert.That(centerPixel).IsEquivalentTo(codeColor);
    }

    [Test]
    [MethodDataSource(nameof(GetFinderPatternShapes))]
    public async Task CustomFinderPatternShape_WithAntialiasedModuleShape_UsesBackgroundColorForInnerRing(FinderPatternShape finderPatternShape)
    {
        // Regression: when the module shape requires antialiasing (e.g., circles),
        // the finder pattern ring must still use the configured backgroundColor,
        // not show as black due to missing IsAntialias on the ring paint.
        var backgroundColor = new SKColor(0xEA, 0xFB, 0x00, 0xFF);
        var codeColor = SKColors.Green;
        var qr = QRCodeGenerator.CreateQrCode("finder-shape-antialias-test", ECCLevel.M);

        var imageSize = qr.Size * 10;
        var area = SKRect.Create(0, 0, imageSize, imageSize);
        using var bitmap = new SKBitmap(imageSize, imageSize);
        using var canvas = new SKCanvas(bitmap);

        QRCodeRenderer.Render(
            canvas,
            area,
            qr,
            codeColor,
            backgroundColor,
            moduleShape: CircleModuleShape.Default,
            moduleSizePercent: 0.9f,
            finderPatternShape: finderPatternShape);

        var finderRect = QRCodeRenderer.GetFinderPatternRect(qr, 0, area);
        var moduleSize = finderRect.Width / 7f;
        var ringSampleX = (int)MathF.Round(finderRect.Left + moduleSize * 1.5f);
        var ringSampleY = (int)MathF.Round(finderRect.Top + moduleSize * 3.5f);

        var ringPixel = bitmap.GetPixel(ringSampleX, ringSampleY);

        await Assert.That(ringPixel).IsEquivalentTo(backgroundColor);
    }

    [Test]
    public async Task ToByteArray_GradientCircleFinderPattern_UsesBackgroundColorForInnerRing()
    {
        const string content = "Test 2";
        var backgroundColor = SKColors.Yellow;
        var pngBytes = new QRCodeImageBuilder(content)
            .WithSize(800, 800)
            .WithErrorCorrection(ECCLevel.H)
            .WithColors(SKColors.Black, backgroundColor, SKColors.Transparent)
            .WithGradient(new GradientOptions(
                [SKColors.Blue, SKColors.Purple, SKColors.Pink],
                GradientDirection.TopLeftToBottomRight,
                [0f, 0.5f, 1f]))
            .WithFinderPatternShape(CircleFinderPatternShape.Default)
            .WithModuleShape(CircleModuleShape.Default, 0.9f)
            .ToByteArray();

        using var bitmap = SKBitmap.Decode(pngBytes) ?? throw new InvalidOperationException("Failed to decode generated PNG.");
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.H);
        var finderRect = QRCodeRenderer.GetFinderPatternRect(
            qr,
            0,
            SKRect.Create(0, 0, bitmap.Width, bitmap.Height));
        var moduleSize = finderRect.Width / 7f;
        var ringPixel = bitmap.GetPixel(
            (int)MathF.Round(finderRect.Left + moduleSize * 1.5f),
            (int)MathF.Round(finderRect.Top + moduleSize * 3.5f));

        await Assert.That(ringPixel).IsEqualTo(backgroundColor);
    }

    [Test]
    [Arguments(false)]
    public async Task RectangleFinderPatternShape_RequiresAntialiasing_IsFalse(bool expected)
    {
        await Assert.That(RectangleFinderPatternShape.Default.RequiresAntialiasing).IsEqualTo(expected);
    }

    [Test]
    [Arguments(true)]
    public async Task CircleFinderPatternShape_RequiresAntialiasing_IsTrue(bool expected)
    {
        await Assert.That(CircleFinderPatternShape.Default.RequiresAntialiasing).IsEqualTo(expected);
    }

    [Test]
    [Arguments(true)]
    public async Task RoundedRectangleFinderPatternShape_RequiresAntialiasing_IsTrue(bool expected)
    {
        await Assert.That(RoundedRectangleFinderPatternShape.Default.RequiresAntialiasing).IsEqualTo(expected);
    }

    [Test]
    [Arguments(true)]
    public async Task RoundedRectangleCircleFinderPatternShape_RequiresAntialiasing_IsTrue(bool expected)
    {
        await Assert.That(RoundedRectangleCircleFinderPatternShape.Default.RequiresAntialiasing).IsEqualTo(expected);
    }

    public static IEnumerable<Func<FinderPatternShape>> GetFinderPatternShapes()
    {
        yield return () => RectangleFinderPatternShape.Default;
        yield return () => CircleFinderPatternShape.Default;
        yield return () => RoundedRectangleFinderPatternShape.Default;
        yield return () => RoundedRectangleCircleFinderPatternShape.Default;
    }

    private sealed class BackgroundPaintFinderPatternShape : FinderPatternShape
    {
        public override bool RequiresAntialiasing => false;

        public int BackgroundPaintDrawCount { get; private set; }

        public int BackgroundColorDrawCount { get; private set; }

        public SKColor ReceivedBackgroundColor { get; private set; }

        public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
        {
        }

        public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint, SKColor backgroundColor)
        {
            BackgroundColorDrawCount++;
        }

        public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint, SKPaint backgroundPaint)
        {
            BackgroundPaintDrawCount++;
            ReceivedBackgroundColor = backgroundPaint.Color;
        }
    }

    private sealed class LegacyBackgroundColorFinderPatternShape : FinderPatternShape
    {
        public int DrawCount { get; private set; }

        public SKColor ReceivedBackgroundColor { get; private set; }

        public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
        {
        }

        public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint, SKColor backgroundColor)
        {
            DrawCount++;
            ReceivedBackgroundColor = backgroundColor;
        }
    }
}
