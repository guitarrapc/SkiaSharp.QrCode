#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:project ../../src/SkiaSharp.QrCode/SkiaSharp.QrCode.csproj
using SkiaSharp;
using SkiaSharp.QrCode;
using SkiaSharp.QrCode.Image;

// Verification sample for WithModulePixelSize: https://github.com/guitarrapc/SkiaSharp.QrCode/issues/294
// - Fixed image size can produce fractional module pixels.
// - Fixed module pixel size keeps each module aligned to exact pixels across versions.

const string content = "HELLO-294";
const int fixedImageSize = 512;
const int modulePixelSize = 12;
var outputDirectory = Path.Combine(Environment.CurrentDirectory, "samples", "Dotfiles", "output", "module-pixel-size");
Directory.CreateDirectory(outputDirectory);

var scenarios = new (int Version, int QuietZone)[]
{
    (1, 4),
    (10, 4),
    (20, 4),
};

foreach (var (version, quietZone) in scenarios)
{
    var qrData = QRCodeGenerator.CreateQrCode(content, ECCLevel.H, requestedVersion: version, quietZoneSize: quietZone);

    using var fixedSizeBitmap = new QRCodeImageBuilder(qrData)
        .WithSize(fixedImageSize, fixedImageSize)
        .WithColors(SKColors.Black, SKColors.White)
        .WithModuleShape(CircleModuleShape.Default, 0.92f)
        .WithFinderPatternShape(RoundedRectangleCircleFinderPatternShape.Default)
        .ToBitmap();

    using var modulePixelBitmap = new QRCodeImageBuilder(qrData)
        .WithModulePixelSize(modulePixelSize)
        .WithColors(SKColors.Black, SKColors.White)
        .WithModuleShape(CircleModuleShape.Default, 0.92f)
        .WithFinderPatternShape(RoundedRectangleCircleFinderPatternShape.Default)
        .ToBitmap();

    SaveBitmap(fixedSizeBitmap, Path.Combine(outputDirectory, $"v{version}_fixed-size_{fixedImageSize}.png"));
    SaveBitmap(modulePixelBitmap, Path.Combine(outputDirectory, $"v{version}_module-pixel-{modulePixelSize}.png"));

    var fixedSizeModulePx = (float)fixedImageSize / qrData.Size;
    var modulePixelImageSide = qrData.Size * modulePixelSize;

    Console.WriteLine($"Version v{version} (matrix size: {qrData.Size} modules)");
    Console.WriteLine($"  fixed-size         : {fixedImageSize}x{fixedImageSize}, effective module px = {fixedSizeModulePx:F4}");
    Console.WriteLine($"  with-module-pixel  : {modulePixelImageSide}x{modulePixelImageSide}, module px = {modulePixelSize}");
    Console.WriteLine();
}

Console.WriteLine($"Saved comparison images to: {outputDirectory}");

static void SaveBitmap(SKBitmap bitmap, string outputPath)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
    data.SaveTo(stream);
}
