using SkiaSharp;
using SkiaSharp.QrCode;
using SkiaSharp.QrCode.Image;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var content = "https://github.com/guitarrapc/SkiaSharp.QrCode/releases/tag/0.10.0?a=5119e6a88a83ae5db1469b66692890775c90b479&b=5119e6a88a83ae5db1469b66692890775c90b479";
var outputDir = "bin/output";
var iconPath = "samples/test.png";
var iconInstaPath = "samples/insta.png";

// prepare
Directory.CreateDirectory(outputDir);

// Static method (Simplest)
Console.WriteLine("""
    Pattern 1: Static Method (Simplest)
      - Best for: Quick QR code generation with default settings
      - API: QRCodeImageBuilder.GetPngBytes()
    """);
{
    var path = Path.Combine(outputDir, "pattern1_static_method.png");

    // One-liner: Generate PNG bytes with default settings
    var pngBytes = QRCodeImageBuilder.GetPngBytes(content);
    File.WriteAllBytes(path, pngBytes);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Static Method with Stream
Console.WriteLine("""
    Pattern 2: Static Method with Stream
      - Best for: Direct file output without byte array allocation
      - API: QRCodeImageBuilder.SavePng()
    """);
{
    var path = Path.Combine(outputDir, "pattern2_static_stream.png");

    using var stream = File.OpenWrite(path);
    QRCodeImageBuilder.SavePng(content, stream, ECCLevel.H, size: 512);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Builder Pattern (Basic)
Console.WriteLine("""
    Pattern 3: Builder Pattern (Basic)
      - Best for: Customizing size, format, error correction
      - API: new QRCodeImageBuilder().WithXxx().ToByteArray()
    """);
{
    var path = Path.Combine(outputDir, "pattern3_builder_basic.png");

    var qrBuilder = new QRCodeImageBuilder(content)
        .WithSize(1024, 1024)
        .WithErrorCorrection(ECCLevel.H)
        .WithQuietZone(2);

    var pngBytes = qrBuilder.ToByteArray();
    File.WriteAllBytes(path, pngBytes);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Builder Pattern (Advanced - Custom Colors)
Console.WriteLine("""
    Pattern 4: Builder Pattern (Advanced - Custom Colors
      - Best for: Custom colors and styling
      - API: new QRCodeImageBuilder().WithColors()
    """);
{
    var path = Path.Combine(outputDir, "pattern4_builder_colors.png");
    using var stream = File.OpenWrite(path);

    new QRCodeImageBuilder(content)
        .WithSize(800, 800)
        .WithErrorCorrection(ECCLevel.H)
        .WithColors(
            codeColor: SKColor.Parse("000080"),      // Navy
            backgroundColor: SKColor.Parse("FFE4B5"), // Moccasin
            clearColor: SKColors.Transparent)
        .SaveTo(stream);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Builder Pattern (Advanced - Module Shape)
Console.WriteLine("""
    Pattern 5: Builder Pattern (Advanced - Module Shape)
      - Best for: Custom module shapes (circles, rounded rectangles)
      - API: new QRCodeImageBuilder().WithModuleShape()
    """);
{
    var path = Path.Combine(outputDir, "pattern5_builder_shape.png");

    var qrBuilder = new QRCodeImageBuilder(content)
        .WithSize(800, 800)
        .WithErrorCorrection(ECCLevel.H)
        .WithModuleShape(CircleModuleShape.Default, sizePercent: 0.95f)
        .WithColors(codeColor: SKColors.DarkBlue);

    using var image = qrBuilder.ToImage();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Builder Pattern (Advanced - Gradient)
Console.WriteLine("""
    Pattern 6: Builder Pattern (Advanced - Gradient)
      - Best for: Gradient effects on QR code
      - API: new QRCodeImageBuilder().WithGradient()
    """);
{
    var path = Path.Combine(outputDir, "pattern6_builder_gradient.png");

    var gradient = new GradientOptions(
        [SKColors.Blue, SKColors.Purple, SKColors.Pink],
        GradientDirection.TopLeftToBottomRight,
        [0f, 0.5f, 1f]);

    var qrBuilder = new QRCodeImageBuilder(content)
        .WithSize(800, 800)
        .WithErrorCorrection(ECCLevel.H)
        .WithGradient(gradient)
        .WithModuleShape(RoundedRectangleModuleShape.Default, sizePercent: 0.9f);

    var bitmap = qrBuilder.ToBitmap();
    using (bitmap)
    using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
    using (var stream = File.OpenWrite(path))
    {
        data.SaveTo(stream);
    }

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Builder Pattern (Advanced - Embedded Icon)
Console.WriteLine("""
    Pattern 7: Builder Pattern (Advanced - Icon Overlay)
      - Best for: QR codes with logo/icon in center
      - API: new QRCodeImageBuilder().WithIcon()
    """);
{
    var path = Path.Combine(outputDir, "pattern7_builder_icon.png");

    using var logo = SKBitmap.Decode(File.ReadAllBytes(iconPath));
    var icon = new IconData
    {
        Icon = logo,
        IconSizePercent = 15,
        IconBorderWidth = 10,
    };

    var qrBuilder = new QRCodeImageBuilder(content)
        .WithSize(800, 800)
        .WithErrorCorrection(ECCLevel.H) // H recommended for icons
        .WithColors(codeColor: SKColors.DarkGreen, backgroundColor: SKColors.LightYellow)
        .WithIcon(icon);

    var pngBytes = qrBuilder.ToByteArray();
    File.WriteAllBytes(path, pngBytes);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Builder Pattern Full Customization
Console.WriteLine("""
    Pattern 8: Builder Pattern (Full Customization)
      - Best for: Maximum customization - all features combined
      - API: Chaining all With methods
    """);
{
    var path = Path.Combine(outputDir, "pattern8_builder_full.png");

    using var logo = SKBitmap.Decode(File.ReadAllBytes(iconPath));
    var icon = new IconData
    {
        Icon = logo,
        IconSizePercent = 12,
        IconBorderWidth = 8,
    };

    var gradient = new GradientOptions(
        [SKColor.Parse("FF6B35"), SKColor.Parse("F7931E"), SKColor.Parse("FDC830")],
        GradientDirection.LeftToRight);

    var qrBuilder = new QRCodeImageBuilder(content)
        .WithSize(1024, 1024)
        .WithFormat(SKEncodedImageFormat.Png, quality: 100)
        .WithErrorCorrection(ECCLevel.H)
        .WithEciMode(EciMode.Utf8)
        .WithQuietZone(3)
        .WithColors(backgroundColor: SKColors.White, clearColor: SKColors.Transparent)
        .WithModuleShape(RoundedRectangleModuleShape.Default, sizePercent: 0.92f)
        .WithGradient(gradient)
        .WithIcon(icon);

    using var stream = File.OpenWrite(path);
    qrBuilder.SaveTo(stream);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// QRCodeRenderer Pattern (Low-level)
Console.WriteLine("""
    Pattern 9: QRCodeRenderer Pattern (Low-level)
      - Best for: Advanced canvas control, custom rendering logic
      - API: QRCodeGenerator + QRCodeRenderer.Render()
    """);
{
    var path = Path.Combine(outputDir, "pattern9_renderer.png");

    // Generate QR data
    var qrData = QRCodeGenerator.CreateQrCode(content, ECCLevel.H, quietZoneSize: 4);

    // Create canvas
    var info = new SKImageInfo(800, 800);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;

    // Render with custom options
    using var logo = SKBitmap.Decode(File.ReadAllBytes(iconPath));
    var icon = new IconData { Icon = logo, IconSizePercent = 10, IconBorderWidth = 5 };
    var gradient = new GradientOptions([SKColors.DarkViolet, SKColors.DeepPink], GradientDirection.TopLeftToBottomRight);

    QRCodeRenderer.Render(
        canvas,
        SKRect.Create(0, 0, info.Width, info.Height),
        qrData,
        codeColor: null, // Use gradient instead
        backgroundColor: SKColors.White,
        iconData: icon,
        moduleShape: CircleModuleShape.Default,
        moduleSizePercent: 0.9f,
        gradientOptions: gradient);

    // Save
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// SkiaSharp Canvas Direct (Most Low-level)
Console.WriteLine("""
    Pattern 10: SkiaSharp Canvas Direct (Most Low-level)
      - Best for: Maximum control, custom post-processing
      - API: QRCodeGenerator + SKCanvas + Manual rendering
    """);
{
    var path = Path.Combine(outputDir, "pattern10_canvas_direct.png");

    // Generate QR data
    var qrData = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, quietZoneSize: 4);

    // Create canvas
    var info = new SKImageInfo(600, 600);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;

    // Clear background
    canvas.Clear(SKColors.White);

    // Render QR code
    canvas.Render(qrData, info.Width, info.Height);

    // Custom post-processing: Add text below QR code
    using var font = new SKFont
    {
        Size = 24,
        Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
    };

    using var paint = new SKPaint
    {
        Color = SKColors.Black,
        IsAntialias = true
    };

    var text = "Scan Me!";
    var textWidth = font.MeasureText(text);
    canvas.DrawText(text, (info.Width - textWidth) / 2, info.Height - 10, SKTextAlign.Left, font, paint);

    // Save
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// IBufferWriter Pattern (High Performance)
Console.WriteLine("""
    Pattern 11: IBufferWriter Pattern (High Performance)
      - Best for: ASP.NET Core, high-performance scenarios
      - API: QRCodeImageBuilder.WritePng()
    """);
{
    var path = Path.Combine(outputDir, "pattern11_buffer_writer.png");

    using var fileStream = File.Create(path);
    var bufferWriter = System.IO.Pipelines.PipeWriter.Create(fileStream);

    QRCodeImageBuilder.WritePng(content, bufferWriter, ECCLevel.M, size: 512);
    bufferWriter.Complete();

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Different Image Formats
Console.WriteLine("""
    Pattern 12: Different Image Formats
      - Best for: Supporting multiple output formats
      - API: QRCodeImageBuilder.GetImageBytes() with different formats
    """);
{
    var formats = new[]
    {
        (SKEncodedImageFormat.Png, "png", 100),
        (SKEncodedImageFormat.Jpeg, "jpg", 90),
        (SKEncodedImageFormat.Webp, "webp", 80),
    };

    foreach (var (format, ext, quality) in formats)
    {
        var path = Path.Combine(outputDir, $"pattern12_format.{ext}");
        var bytes = QRCodeImageBuilder.GetImageBytes(content, format, ECCLevel.M, 512, quality);
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  ✓ Saved {format} to: {path}");
    }
}
Console.WriteLine();

// Use Rounded Rectangle for finder patterns
Console.WriteLine("""
    Pattern 13: Rounded Rectangle for Finder Patterns
      - Best for: Stylish finder patterns with rounded corners
      - API: QRCodeImageBuilder().WithFinderPatternShape()
    """);
{
    var path = Path.Combine(outputDir, "pattern13_finderpattern_rounded_rectangle.png");

    var pngBytes = new QRCodeImageBuilder(content)
        .WithSize(512, 512)
        .WithFinderPatternShape(RectangleFinderPatternShape.Default)
        //.WithFinderPatternShape(CircleFinderPatternShape.Default)
        //.WithFinderPatternShape(RoundedRectangleFinderPatternShape.Default)
        //.WithFinderPatternShape(RoundedRectangleCircleFinderPatternShape.Default)
        .ToByteArray();

    File.WriteAllBytes(path, pngBytes);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Instagram-style Profile QR Code
Console.WriteLine("""
    Pattern 14: Instagram-style Profile QR Code
      - Best for: Demonstrating complex styling
      - API: QRCodeImageBuilder with gradient + icon + custom styling
    """);
{
    var path = Path.Combine(outputDir, "pattern14_instagram_style.png");

    // Instagram gradient colors (orange -> pink -> purple)
    var instagramGradient = new GradientOptions([
            SKColor.Parse("FCAF45"),  // Orange
            SKColor.Parse("F77737"),  // Orange-Red
            SKColor.Parse("E1306C"),  // Pink
            SKColor.Parse("C13584"),  // Purple
            SKColor.Parse("833AB4")   // Deep Purple
        ],
        GradientDirection.TopLeftToBottomRight,
        [0f, 0.25f, 0.5f, 0.75f, 1f]);

    // Load Instagram logo (if you have one)
    // For this example, we'll use the test icon
    using var logo = SKBitmap.Decode(File.ReadAllBytes(iconInstaPath));
    var icon = new IconData
    {
        Icon = logo,
        IconSizePercent = 14,
        IconBorderWidth = 5,
    };

    var qrBuilder = new QRCodeImageBuilder(content)
        .WithSize(1024, 1024)
        .WithErrorCorrection(ECCLevel.H)
        .WithQuietZone(4)
        .WithColors(
            backgroundColor: SKColors.White,
            clearColor: SKColors.White)
        .WithModuleShape(CircleModuleShape.Default, sizePercent: 0.95f)
        .WithFinderPatternShape(RoundedRectangleCircleFinderPatternShape.Default)
        .WithGradient(instagramGradient)
        .WithIcon(icon);

    var pngBytes = qrBuilder.ToByteArray();
    File.WriteAllBytes(path, pngBytes);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Instagram-style with custom frame and text
Console.WriteLine("""
    Pattern 15: Instagram-style with Custom Frame
      - Best for: Branded QR codes with text overlay
      - API: QRCodeRenderer + Custom canvas drawing
    """);
{
    var path = Path.Combine(outputDir, "pattern15_instagram_frame.png");

    // Create larger canvas for frame
    var canvasSize = 1200;
    var qrSize = 900;
    var topPadding = 80;
    var sidePadding = (canvasSize - qrSize) / 2;
    var textY = topPadding + qrSize + 50;

    var info = new SKImageInfo(canvasSize, canvasSize);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;

    // Draw Background
    {
        canvas.Clear(SKColors.White);

        var r = 100; // corner radius

        // Draw gray border (outer rounded rectangle)
        using (var borderPaint = new SKPaint
        {
            Color = SKColor.Parse("E0E0E0"), // Light gray border
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true
        })
        {
            var borderRect = new SKRoundRect(SKRect.Create(30, 30, canvasSize - 60, canvasSize - 60), r, r);
            canvas.DrawRoundRect(borderRect, borderPaint);
        }

        // Draw rounded rectangle background
        using (var bgPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            var bgRect = new SKRoundRect(SKRect.Create(30, 30, canvasSize - 60, canvasSize - 60), r, r);
            canvas.DrawRoundRect(bgRect, bgPaint);
        }
    }

    // Generate QR data
    {
        var qrData = QRCodeGenerator.CreateQrCode(content, ECCLevel.H, quietZoneSize: 2);

        // Instagram gradient
        var instagramGradient = new GradientOptions([
                SKColor.Parse("FCAF45"),  // Orange
                SKColor.Parse("F77737"),  // Orange-Red
                SKColor.Parse("E1306C"),  // Pink
                SKColor.Parse("C13584"),  // Purple
                SKColor.Parse("833AB4")   // Deep Purple
            ],
            GradientDirection.TopLeftToBottomRight,
            [0f, 0.25f, 0.5f, 0.75f, 1f]);

        // Render QR code
        using var logo = SKBitmap.Decode(File.ReadAllBytes(iconInstaPath));
        var icon = new IconData
        {
            Icon = logo,
            IconSizePercent = 15,
            IconBorderWidth = 4,
        };

        var qrRect = SKRect.Create(sidePadding, topPadding, qrSize, qrSize);
        QRCodeRenderer.Render(
            canvas,
            qrRect,
            qrData,
            codeColor: null,
            backgroundColor: SKColors.White,
            iconData: icon,
            moduleShape: CircleModuleShape.Default,
            moduleSizePercent: 0.95f,
            gradientOptions: instagramGradient,
            finderPatternShape: RoundedRectangleCircleFinderPatternShape.Default);
    }

    // Draw bottom text
    {
        using (var titleFont = new SKFont
        {
            Size = 48,
            Typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal),
        })
        using (var appFont = new SKFont
        {
            Size = 36,
            Typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal),
        })
        using (var paint = new SKPaint
        {
            Color = SKColors.OrangeRed,
            IsAntialias = true
        })
        {
            var bottomText = "POST SHARED ON " + DateTime.Now.ToString("MMM dd").ToUpper();
            var textWidth = titleFont.MeasureText(bottomText);
            canvas.DrawText(bottomText,
                (canvasSize - textWidth) / 2,
                textY,
                SKTextAlign.Left,
                titleFont,
                paint);

            var usernameText = "@SKIASHARP.QRCODE";
            var usernameWidth = appFont.MeasureText(usernameText);
            canvas.DrawText(usernameText,
                (canvasSize - usernameWidth) / 2,
                textY + 60,
                SKTextAlign.Left,
                appFont,
                paint);
        }
    }

    // Save
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Compress & Decompress QR Code Data
Console.WriteLine("""
    Pattern 16: Compress & Decompress QR Code Data
      - Best for: Reducing size of QR code data storage/transmission
    """);
{
    var path = Path.Combine(outputDir, "pattern16_compress_decompress.png");

    // compression to zstandard ...
    var qrCodeData = QRCodeGenerator.CreateQrCode(content, ECCLevel.L);
    var src = qrCodeData.GetRawData();
    var size = qrCodeData.GetRawDataSize();

    var maxSize = NativeCompressions.Zstandard.GetMaxCompressedLength(size);
    var compressed = new byte[maxSize];
    NativeCompressions.Zstandard.Compress(src, compressed, NativeCompressions.ZstandardCompressionOptions.Default);

    // decompression from zstandard ...
    var decompressed = NativeCompressions.Zstandard.Decompress(compressed);

    var qr = new QRCodeData(decompressed, 4);
    var pngBytes = new QRCodeImageBuilder(qr)
        .WithSize(512, 512)
        .ToByteArray();
    File.WriteAllBytes(path, pngBytes);

    Console.WriteLine($"  ✓ Saved to: {path}");
}
Console.WriteLine();

// Console Output of QR Code
Console.WriteLine("""
    Pattern 17: Console Output of QR Code
      - Best for: Quick visual verification in console
      - API: QRCodeGenerator.CreateQrCode() + Console.Write()
    """);
{
    var qrCodeData = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, quietZoneSize: 4);
    for (var row = 0; row < qrCodeData.Size; row++)
    {
        for (var col = 0; col < qrCodeData.Size; col++)
        {
            Console.Write(qrCodeData[row, col] ? "🔵" : "  ");
        }
        Console.Write("\n");
    }
}
Console.WriteLine();

Console.WriteLine("=== All patterns completed! ===");
Console.WriteLine($"Output directory: {Path.GetFullPath(outputDir)}");
