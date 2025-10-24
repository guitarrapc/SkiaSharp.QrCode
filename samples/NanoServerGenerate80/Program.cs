using SkiaSharp.QrCode.Image;
using System.Reflection;

if (args.Length < 2)
{
    Console.WriteLine($"Usage: dotnet {Assembly.GetExecutingAssembly().EntryPoint!.Module.Name} your_message output_path");
    return;
}
var content = args[0];
var path = args[1];

// generate qr code
var qrCodeData = QRCodeImageBuilder.GetPngBytes(content);
File.WriteAllBytes(path, qrCodeData);

Console.WriteLine($"Successfully output QRCode in {path}");
