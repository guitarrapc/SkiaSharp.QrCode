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
    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);

        var area = SKRect.Create(0, 0, width, height);
        QRCodeRenderer.Render(canvas, area, data, null, null, null);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="width">The width of the rendering area.</param>
    /// <param name="height">The height of the rendering area.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int height, SKColor clearColor, SKColor codeColor)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, height);
        QRCodeRenderer.Render(canvas, area, data, codeColor, null);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="width">The width of the rendering area.</param>
    /// <param name="height">The height of the rendering area.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    /// <param name="backgroundColor">The background color of the QR Code canvas.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int height, SKColor clearColor, SKColor codeColor, SKColor backgroundColor)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, height);
        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="width">The width of the rendering area.</param>
    /// <param name="height">The height of the rendering area.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int height, SKColor clearColor, SKColor codeColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, height);
        QRCodeRenderer.Render(canvas, area, data, codeColor, null, iconData);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="width">The width of the rendering area.</param>
    /// <param name="height">The height of the rendering area.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    /// <param name="backgroundColor">The background color of the QR Code canvas.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int height, SKColor clearColor, SKColor codeColor, SKColor backgroundColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, height);
        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor, iconData);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, null);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    /// <param name="backgroundColor">The background color of the QR Code canvas.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, SKColor backgroundColor)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, null, iconData);
    }

    /// <summary>
    /// Renders a QR code on the canvas with custom colors.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="data">The QR code data.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="clearColor">The color used to clear the canvas.</param>
    /// <param name="codeColor">The color of the QR code modules.</param>
    /// <param name="backgroundColor">The background color of the QR Code canvas.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, SKColor backgroundColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor, iconData);
    }
}
