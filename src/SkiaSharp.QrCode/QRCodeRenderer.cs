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
    /// <exception cref="ArgumentNullException"></exception>
    public static void Render(
        SKCanvas canvas,
        SKRect area,
        QRCodeData data,
        SKColor? codeColor,
        SKColor? backgroundColor,
        IconData? iconData = null,
        ModuleShape? moduleShape = null,
        float moduleSizePercent = 1.0f,
        GradientOptions? gradientOptions = null)
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
            var (start, end) = GeLineartGradientPoints(area, gradientOptions.Direction);
            darkPaint.Shader = SKShader.CreateLinearGradient(
                start,
                end,
                gradientOptions.Colors,
                gradientOptions.ColorPositions,
                SKShaderTileMode.Clamp);
        }
        else
        {
            darkPaint.Color = codeColor ?? SKColors.Black;
        }

        // Draw the modules
        var size = data.Size;
        var cellHeight = area.Height / size;
        var cellWidth = area.Width / size;

        // Calculate module size with gaps
        var moduleWidth = cellWidth * moduleSizePercent;
        var moduleHeight = cellHeight * moduleSizePercent;
        var xOffset = (cellWidth - moduleWidth) / 2;
        var yOffset = (cellHeight - moduleHeight) / 2;

        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (data[row, col])
                {
                    var x = area.Left + col * cellWidth + xOffset;
                    var y = area.Top + row * cellHeight + yOffset;
                    var rect = SKRect.Create(x, y, moduleWidth, moduleHeight);
                    shape.Draw(canvas, rect, darkPaint);
                }
            }
        }

        if (iconData?.Icon != null)
        {
            var iconSize = iconData.IconSizePercent / 100f;
            var iconWidth = area.Width * iconSize;
            var iconHeight = area.Height * iconSize;

            var centerX = area.Left + area.Width / 2;
            var centerY = area.Top + area.Height / 2;

            // Draw border background color padding if specified
            if (iconData.IconBorderWidth > 0)
            {
                var borderWidth = iconData.IconBorderWidth;
                var borderRect = SKRect.Create(
                    centerX - iconWidth / 2 - borderWidth,
                    centerY - iconHeight / 2 - borderWidth,
                    iconWidth + borderWidth * 2,
                    iconHeight + borderWidth * 2);

                using var borderPaint = new SKPaint()
                {
                    Color = bgColor,
                    Style = SKPaintStyle.Fill,
                };
                canvas.DrawRect(borderRect, borderPaint);
            }

            var iconRect = SKRect.Create(
                centerX - iconWidth / 2,
                centerY - iconHeight / 2,
                iconWidth,
                iconHeight);
            canvas.DrawBitmap(iconData.Icon, iconRect);
        }
    }

    private static (SKPoint start, SKPoint end) GeLineartGradientPoints(SKRect area, GradientDirection direction)
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
