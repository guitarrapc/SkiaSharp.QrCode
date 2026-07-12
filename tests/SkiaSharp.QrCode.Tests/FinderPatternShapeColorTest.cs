using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode.Tests;

public class FinderPatternShapeColorTest
{
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

    public static IEnumerable<Func<FinderPatternShape>> GetFinderPatternShapes()
    {
        yield return () => RectangleFinderPatternShape.Default;
        yield return () => CircleFinderPatternShape.Default;
        yield return () => RoundedRectangleFinderPatternShape.Default;
        yield return () => RoundedRectangleCircleFinderPatternShape.Default;
    }
}
