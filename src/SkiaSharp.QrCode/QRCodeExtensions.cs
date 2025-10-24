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
    public static void Render(
        this SKCanvas canvas,
        QRCodeData data,
        int width,
        int height,
        SKColor? clearColor = null,
        SKColor? codeColor = null,
        SKColor? backgroundColor = null,
        IconData? iconData = null,
        ModuleShape? moduleShape = null)
    {
        var area = SKRect.Create(0, 0, width, height);
        canvas.Render(data, area, clearColor, codeColor, backgroundColor, iconData, moduleShape);
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
    public static void Render(
        this SKCanvas canvas,
        QRCodeData data,
        SKRect area,
        SKColor? clearColor = null,
        SKColor? codeColor = null,
        SKColor? backgroundColor = null,
        IconData? iconData = null,
        ModuleShape? moduleShape = null)
    {
        canvas.Clear(clearColor ?? SKColors.Transparent);
        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor, iconData, moduleShape);
    }
}
