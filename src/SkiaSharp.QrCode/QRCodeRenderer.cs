using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode;

/// <summary>
/// Provides low-level rendering capabilities for QR codes to SkiaSharp canvases.
/// Offers fine-grained control over appearance, including colors, shapes, gradients, and icon overlays.
/// </summary>
public static class QRCodeRenderer
{
    /// <summary>
    /// Render the specified data into the given area of the target canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render the QR code on.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="data">The QR code data to render.</param>
    /// <param name="codeColor">The color of the QR code modules. If null, black is used.</param>
    /// <param name="backgroundColor">The background color. If null, white is used.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    /// <param name="moduleShape">The shape to use for drawing modules. If null, rectangles are used.</param>
    /// <param name="moduleSizePercent">The size of each module as a percentage of the cell size (0.0 to 1.0). Default is 1.0 (no gap).</param>
    /// <param name="gradientOptions">Optional gradient options for the QR code modules.</param>
    /// <param name="finderPatternShape">The shape to use for drawing finder patterns. If null, finder patterns are drawn using the same shape as regular modules. Set to a custom shape to differentiate finder patterns from data modules.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static void Render(
        SKCanvas canvas,
        SKRect area,
        QRCodeData data,
        SKColor? codeColor,
        SKColor? backgroundColor,
        IconData? iconData = null,
        ModuleShape? moduleShape = null,
        float moduleSizePercent = 1.0f,
        GradientOptions? gradientOptions = null,
        FinderPatternShape? finderPatternShape = null)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));
        if (moduleSizePercent is < 0f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(moduleSizePercent), "Module size percent must be between 0.0 and 1.0.");

        var bgColor = backgroundColor ?? SKColors.White;
        var fgColor = codeColor ?? SKColors.Black;
        var shape = moduleShape ?? RectangleModuleShape.Default;

        // Draw the background at once
        using (var lightPaint = new SKPaint() { Color = bgColor, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(area, lightPaint);
        }

        // Create paint with gradient or solid color
        using var darkPaint = new SKPaint() { Style = SKPaintStyle.Fill, IsAntialias = true };

        // Apply gradient if specified
        if (gradientOptions is not null && gradientOptions.Direction != GradientDirection.None)
        {
            var (start, end) = GetLinearGradientPoints(area, gradientOptions.Direction);
            darkPaint.Shader = SKShader.CreateLinearGradient(start, end, gradientOptions.Colors, gradientOptions.ColorPositions, SKShaderTileMode.Clamp);
        }
        else
        {
            darkPaint.Color = codeColor ?? SKColors.Black;
        }

        // Draw the modules
        var size = data.Size;
        var coreSize = data.GetCoreSize();
        var cellHeight = area.Height / size;
        var cellWidth = area.Width / size;
        var quietZoneOffset = size - coreSize;

        // Calculate module size with gaps
        var moduleWidth = cellWidth * moduleSizePercent;
        var moduleHeight = cellHeight * moduleSizePercent;
        var xOffset = (cellWidth - moduleWidth) / 2;
        var yOffset = (cellHeight - moduleHeight) / 2;

        // Draw regular modules (exclude finder patterns)
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                // Convert to core coordinates
                var coreRow = row - quietZoneOffset / 2;
                var coreCol = col - quietZoneOffset / 2;

                // Skip if outside core area
                if (coreRow < 0 || coreRow >= coreSize || coreCol < 0 || coreCol >= coreSize)
                    continue;

                // Skip finder pattern if custom shape is used
                if (finderPatternShape is not null && data.IsFinderPattern(coreRow, coreCol))
                    continue;

                if (data[row, col])
                {
                    var x = area.Left + col * cellWidth + xOffset;
                    var y = area.Top + row * cellHeight + yOffset;
                    var rect = SKRect.Create(x, y, moduleWidth, moduleHeight);
                    shape.Draw(canvas, rect, darkPaint);
                }
            }
        }

        // Draw finder patterns
        if (finderPatternShape is not null)
        {
            // total 3 finder patterns
            for (var i = 0; i < 3; i++)
            {
                var finderRect = GetFinderPatternRect(data, i, area);
                finderPatternShape.Draw(canvas, finderRect, darkPaint);
            }
        }

        // Draw the icon if provided
        if (iconData?.Icon is not null)
        {
            var iconSize = iconData.IconSizePercent / 100f;
            var iconWidth = area.Width * iconSize;
            var iconHeight = area.Height * iconSize;

            var centerX = area.Left + area.Width / 2;
            var centerY = area.Top + area.Height / 2;

            var iconRect = SKRect.Create(
                centerX - iconWidth / 2,
                centerY - iconHeight / 2,
                iconWidth,
                iconHeight);

            var borderWidth = iconData.IconBorderWidth;
            var borderRect = iconData.IconBorderWidth > 0 ? SKRect.Create(
                    centerX - iconWidth / 2 - borderWidth,
                    centerY - iconHeight / 2 - borderWidth,
                    iconWidth + ((float)borderWidth * 2),
                    iconHeight + ((float)borderWidth * 2)) : iconRect;

            iconData.Icon.Draw(canvas, iconRect, borderRect, bgColor);
        }
    }

    /// <summary>
    /// Gets the rectangle area for a specified finder pattern in the rendered QR code area.
    /// </summary>
    /// <remarks>
    /// The finder patterns are the large squares typically located at three corners of a QR code.
    /// This method calculates their positions based on the QR code's size and quiet zone, ensuring accurate placement
    /// within the specified rendering area.
    /// </remarks>
    /// <param name="data">The QR code data containing module and layout information. Cannot be null.</param>
    /// <param name="patternIndex">Finder pattern index (0=top-left, 1=top-right, 2=bottom-left).</param>
    /// <param name="renderArea">The area within which the QR code is rendered. The finder pattern rectangle is calculated relative to this area.</param>
    /// <returns>An SKRect representing the position and size of the specified finder pattern within the rendering area.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="patternIndex"/> is less than 0 or greater than 2.</exception>
    public static SKRect GetFinderPatternRect(QRCodeData data, int patternIndex, SKRect renderArea)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));
        if (patternIndex is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(patternIndex), "Pattern index must be 0 (top-left), 1 (top-right), or 2 (bottom-left).");

        var size = data.Size;
        var coreSize = data.GetCoreSize();
        var cellWidth = renderArea.Width / size;
        var cellHeight = renderArea.Height / size;
        var finderWidth = cellWidth * 7;
        var finderHeight = cellHeight * 7;

        var quietZoneOffset = size - coreSize;

        return patternIndex switch
        {
            0 => SKRect.Create(
                renderArea.Left + ((float)quietZoneOffset / 2) * cellWidth,
                renderArea.Top + ((float)quietZoneOffset / 2) * cellHeight,
                finderWidth,
                finderHeight),
            1 => SKRect.Create(
                renderArea.Left + (coreSize - 7 + (float)quietZoneOffset / 2) * cellWidth,
                renderArea.Top + ((float)quietZoneOffset / 2) * cellHeight,
                finderWidth,
                finderHeight),
            2 => SKRect.Create(
                renderArea.Left + ((float)quietZoneOffset / 2) * cellWidth,
                renderArea.Top + (coreSize - 7 + (float)quietZoneOffset / 2) * cellHeight,
                finderWidth,
                finderHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(patternIndex), "Pattern index must be 0 (top-left), 1 (top-right), or 2 (bottom-left)."),
        };
    }

    private static (SKPoint start, SKPoint end) GetLinearGradientPoints(SKRect area, GradientDirection direction)
    {
        return direction switch
        {
            GradientDirection.LeftToRight => (new SKPoint(area.Left, area.MidY), new SKPoint(area.Right, area.MidY)),
            GradientDirection.RightToLeft => (new SKPoint(area.Right, area.MidY), new SKPoint(area.Left, area.MidY)),
            GradientDirection.TopToBottom => (new SKPoint(area.MidX, area.Top), new SKPoint(area.MidX, area.Bottom)),
            GradientDirection.BottomToTop => (new SKPoint(area.MidX, area.Bottom), new SKPoint(area.MidX, area.Top)),
            GradientDirection.TopLeftToBottomRight => (new SKPoint(area.Left, area.Top), new SKPoint(area.Right, area.Bottom)),
            GradientDirection.TopRightToBottomLeft => (new SKPoint(area.Right, area.Top), new SKPoint(area.Left, area.Bottom)),
            GradientDirection.BottomLeftToTopRight => (new SKPoint(area.Left, area.Bottom), new SKPoint(area.Right, area.Top)),
            GradientDirection.BottomRightToTopLeft => (new SKPoint(area.Right, area.Bottom), new SKPoint(area.Left, area.Top)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), $"Direction {direction} is not a valid linear gradient direction."),
        };
    }
}
