using SkiaSharp;
using SkiaSharp.QrCode;
using SkiaSharp.QrCode.Models;
using System.IO;

var content = "testtesttest";
var path = "bin/output/hoge.png";
var iconPath = "samples/test.png";

// prepare
var fullPath = Path.GetFullPath(path);
var dir = Path.GetDirectoryName(fullPath);
Directory.CreateDirectory(dir);

// Generate QrCode
using var generator = new QRCodeGenerator();
var qr = generator.CreateQrCode(content, ECCLevel.L, quietZoneSize: 1);

// Render to canvas
var info = new SKImageInfo(512, 512);
using var surface = SKSurface.Create(info);
var canvas = surface.Canvas;
canvas.Render(qr, info.Width, info.Height);

// gen color
// yellow https://rgb.to/yellow
//canvas.Render(qr, info.Width, info.Height, SKColor.Empty, SKColor.FromHsl(60,100,50));
// red https://rgb.to/red
//canvas.Render(qr, info.Width, info.Height, SKColor.Empty, SKColor.FromHsl(0, 100, 50));

// Render icon inside QrCode
var logo = File.ReadAllBytes(iconPath);
var icon = new IconData
{
    Icon = SKBitmap.Decode(logo),
    IconSizePercent = 10,
};
canvas.Render(qr, info.Width, info.Height, SKColor.Empty, SKColor.Parse("000000"), icon);

// Output to Stream -> File
using (var image = surface.Snapshot())
using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
using (var stream = File.OpenWrite(fullPath))
{
    data.SaveTo(stream);
}
