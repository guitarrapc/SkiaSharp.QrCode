namespace SkiaSharp.QrCode.Image;

/// <summary>
/// Shared canvas layout math for the image builders: resolves the output image
/// info and the content rectangle from explicit size and/or module pixel size.
/// </summary>
internal static class QrImageLayout
{
    internal static (SKImageInfo info, SKRect contentRect) CreateLayout(int matrixSize, Vector2Slim? explicitSize, int? modulePixelSize)
    {
        if (modulePixelSize is null)
        {
            var size = explicitSize ?? new Vector2Slim(512, 512);
            return (new SKImageInfo(size.X, size.Y), SKRect.Create(0, 0, size.X, size.Y));
        }

        int contentSide;
        try
        {
            contentSide = checked(matrixSize * modulePixelSize.Value);
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException("Calculated image size overflowed. Reduce module pixel size or QR version.", ex);
        }

        if (explicitSize is null)
            return (new SKImageInfo(contentSide, contentSide), SKRect.Create(0, 0, contentSide, contentSide));

        var canvasWidth = explicitSize.Value.X;
        var canvasHeight = explicitSize.Value.Y;
        if (canvasWidth < contentSide || canvasHeight < contentSide)
        {
            throw new InvalidOperationException(
                $"Canvas size {canvasWidth}x{canvasHeight} is smaller than QR content size {contentSide}x{contentSide} " +
                $"(QR matrix size {matrixSize} * module pixel size {modulePixelSize.Value}).");
        }

        // Use integer offsets so content stays on whole pixels (odd padding may be 1px asymmetric).
        var left = (canvasWidth - contentSide) / 2;
        var top = (canvasHeight - contentSide) / 2;
        return (
            new SKImageInfo(canvasWidth, canvasHeight),
            SKRect.Create(left, top, contentSide, contentSide));
    }

    internal static bool ContentCoversCanvas(SKRect contentRect, SKImageInfo info)
    {
        return contentRect.Left <= 0 && contentRect.Top <= 0
            && contentRect.Right >= info.Width && contentRect.Bottom >= info.Height;
    }
}
