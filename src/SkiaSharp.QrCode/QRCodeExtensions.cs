using SkiaSharp.QrCode.Models;

namespace SkiaSharp.QrCode;

public static class QRCodeExtensions
{
    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight)
    {
        canvas.Clear(SKColors.Transparent);

        var area = SKRect.Create(0, 0, width, hight);
        QRCodeRenderer.Render(canvas, area, data, null, null, null);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor, SKColor codeColor)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, hight);
        QRCodeRenderer.Render(canvas, area, data, codeColor, null);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor, SKColor codeColor, SKColor backgroundColor)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, hight);
        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor, SKColor codeColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, hight);
        QRCodeRenderer.Render(canvas, area, data, codeColor, null, iconData);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor, SKColor codeColor, SKColor backgroundColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        var area = SKRect.Create(0, 0, width, hight);
        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor, iconData);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, null);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, SKColor backgroundColor)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, null, iconData);
    }

    public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, SKColor backgroundColor, IconData iconData)
    {
        canvas.Clear(clearColor);

        QRCodeRenderer.Render(canvas, area, data, codeColor, backgroundColor, iconData);
    }
}
