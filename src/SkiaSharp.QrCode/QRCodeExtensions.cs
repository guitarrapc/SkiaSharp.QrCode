using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode;

public static class QRCodeExtensions
{
    /// <summary>
    /// Renders a QR code on the canvas with default colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="width">The width of the rendering area.</param>
    /// <param name="height">The height of the rendering area.</param>
    /// <param name="clearColor">The color used to clear the canvas. Defaults to Transparent.</param>
    /// <param name="codeColor">The color of the QR code modules. Defaults to Black.</param>
    /// <param name="backgroundColor">The background color of the QR Code. Defaults to White.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    /// <param name="moduleShape">The shape to use for drawing modules. If null, rectangles are used.</param>
    /// <param name="moduleSizePercent">The size of each module as a percentage of the cell size (0.0 to 1.0). Default is 1.0 (no gap).</param>
    /// <param name="gradientOptions">Optional gradient options for the QR code modules.</param>
    public static void Render(
        this SKCanvas canvas,
        QRCodeData data,
        int width,
        int height,
        SKColor? clearColor = null,
        SKColor? codeColor = null,
        SKColor? backgroundColor = null,
        IconData? iconData = null,
        ModuleShape? moduleShape = null,
        float moduleSizePercent = 1.0f,
        GradientOptions? gradientOptions = null)
    {
        var area = SKRect.Create(0, 0, width, height);
        canvas.Render(data, area, clearColor, codeColor, backgroundColor, iconData, moduleShape, moduleSizePercent, gradientOptions);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="clearColor">The color used to clear the canvas. Defaults to Transparent.</param>
    /// <param name="codeColor">The color of the QR code modules. Defaults to Black.</param>
    /// <param name="backgroundColor">The background color of the QR Code. Defaults to White.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    /// <param name="moduleShape">The shape to use for drawing modules. If null, rectangles are used.</param>
    /// <param name="moduleSizePercent">The size of each module as a percentage of the cell size (0.0 to 1.0). Default is 1.0 (no gap).</param>
    /// <param name="gradientOptions">Optional gradient options for the QR code modules.</param>
    public static void Render(
        this SKCanvas canvas,
        QRCodeData data,
        SKRect area,
        SKColor? clearColor = null,
        SKColor? codeColor = null,
        SKColor? backgroundColor = null,
        IconData? iconData = null,
        ModuleShape? moduleShape = null,
        float moduleSizePercent = 1.0f,
        GradientOptions? gradientOptions = null)
    {
        canvas.Clear(clearColor ?? SKColors.Transparent);
        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor, iconData, moduleShape, moduleSizePercent, gradientOptions);
    }
}
