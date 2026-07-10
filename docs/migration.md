# Migration

- **v1.0.0 removes the obsolete `QrCode` class.** If you still use `QrCode`, see [from before v1.0.0 to v1.0.0](#from-before-v100-to-v100) below.
- v0.11.0 introduces further improvements to Icon handling. See the IconData section below.
- v0.9.0 introduces significant performance improvements and API changes. Here's what you need to know to upgrade:

## from before v1.0.0 to v1.0.0

The `QrCode` class has been **removed** in v1.0.0. It was marked obsolete in v0.9.0; use `QRCodeImageBuilder` instead.

> **Default ECC level change:** `QrCode.GenerateImage()` defaulted to `ECCLevel.L`. `QRCodeImageBuilder` defaults to `ECCLevel.M`. Pass `WithErrorCorrection(ECCLevel.L)` or the `eccLevel` argument on static methods if you need the previous behavior.

### Basic: generate to stream

**Before (0.12.x and earlier):**

```csharp
using SkiaSharp.QrCode.Image;

var qrCode = new QrCode(content, new Vector2Slim(256, 256), SKEncodedImageFormat.Png);
using var stream = File.OpenWrite(path);
qrCode.GenerateImage(stream);
```

**After (v1.0.0):**

```csharp
using SkiaSharp.QrCode.Image;

using var stream = File.OpenWrite(path);
QRCodeImageBuilder.SavePng(content, stream, ECCLevel.L, size: 256);
```

Or with the builder pattern:

```csharp
using var stream = File.OpenWrite(path);
new QRCodeImageBuilder(content)
    .WithSize(256, 256)
    .WithErrorCorrection(ECCLevel.L)
    .SaveTo(stream);
```

### Format and quality

**Before:**

```csharp
var qrCode = new QrCode(content, new Vector2Slim(512, 512), SKEncodedImageFormat.Jpeg, quality: 90);
qrCode.GenerateImage(stream);
```

**After:**

```csharp
new QRCodeImageBuilder(content)
    .WithSize(512, 512)
    .WithFormat(SKEncodedImageFormat.Jpeg, quality: 90)
    .SaveTo(stream);
```

### Get bytes instead of writing to stream

**Before:**

```csharp
using var stream = new MemoryStream();
qrCode.GenerateImage(stream);
var bytes = stream.ToArray();
```

**After:**

```csharp
var bytes = QRCodeImageBuilder.GetPngBytes(content, ECCLevel.L, size: 256);
// Or with format:
var bytes = QRCodeImageBuilder.GetImageBytes(content, SKEncodedImageFormat.Png, ECCLevel.L, size: 256);
```

### Stream position (`resetStreamPosition`)

`QrCode.GenerateImage()` could rewind a seekable stream before writing (`resetStreamPosition: true` by default). `QRCodeImageBuilder` does not reset stream position. Reset manually when needed:

```csharp
if (stream.CanSeek)
    stream.Seek(0, SeekOrigin.Begin);

QRCodeImageBuilder.SavePng(content, stream, size: 256);
```

### Overlay QR code on a base image

`QrCode` had overloads to composite a QR code onto an existing image. Use SkiaSharp canvas drawing instead:

**Before:**

```csharp
var qrCode = new QrCode(content, new Vector2Slim(qrWidth, qrHeight), SKEncodedImageFormat.Png);
using var output = File.OpenWrite(path);
qrCode.GenerateImage(output, baseImageBytes, new Vector2Slim(canvasWidth, canvasHeight), new Vector2Slim(x, y));
```

**After:**

```csharp
using var baseBitmap = SKBitmap.Decode(baseImageBytes);
var info = new SKImageInfo(canvasWidth, canvasHeight);
using var surface = SKSurface.Create(info);
var canvas = surface.Canvas;

canvas.DrawBitmap(baseBitmap, 0, 0);

using (var qrBitmap = new QRCodeImageBuilder(content)
    .WithSize(qrWidth, qrHeight)
    .ToBitmap())
{
    canvas.DrawBitmap(qrBitmap, x, y);
}

using var image = surface.Snapshot();
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
using var output = File.OpenWrite(path);
data.SaveTo(output);
```

## from 0.10.0 to 0.11.0 and higher

Take advantage of new capabilities:

- **Logo customization** - Now you can customize center placed logos. Library offers icons with both images and text.

For complete migration details and examples, see [Release 0.11.0](https://github.com/guitarrapc/SkiaSharp.QrCode/releases/tag/0.11.0).

### ⚠️ IconData.Data changed Icon from SKBitmap to IconShape

**Before (0.10.0):**

```csharp
using var bitmap = SKBitmap.Decode(File.ReadAllBytes(iconPath));

// Old code
var icon = new IconData
{
    Icon = bitmap;
    IconSizePercent = 15,
    IconBorderWidth = 10
};
```

**After (0.11.0):**

```csharp
using var bitmap = SKBitmap.Decode(File.ReadAllBytes(iconPath));

// New code Image only (Short hand)
var icon = IconData.FromImage(bitmap, iconSizePercent: 15, iconBorderWidth: 10);

// New code Image only
var icon = new IconData
{
    Icon = new ImageIconShape(bitmap),
    IconSizePercent = 15,
    IconBorderWidth = 10
};

// New approach with text
var icon = new IconData
{
    Icon = new ImageTextIconShape(bitmap, "Text", SKColors.Black, font),
    IconSizePercent = 15,
    IconBorderWidth = 10
};
```

## from 0.8.0 to 0.9.0 and higher

Take advantage of new capabilities:

- **Gradient colors** - Create eye-catching QR codes with color gradients
- **Enhanced customization** - More control over module shapes and colors
- **Better performance** - Dramatically faster generation with lower memory usage

For complete migration details and examples, see [Release 0.9.0](https://github.com/guitarrapc/SkiaSharp.QrCode/releases/tag/0.9.0).

### 🔄 Primary API Change: `QrCode` → `QRCodeImageBuilder`

The `QrCode` class was marked **obsolete** in v0.9.0 and **removed** in v1.0.0. Replace it with `QRCodeImageBuilder`. For full migration examples (stream output, format/quality, base-image overlay, and more), see [from before v1.0.0 to v1.0.0](#from-before-v100-to-v100).

### 🗑️ Remove `using` Statements

`QRCodeData` and `QRCodeRenderer` are no longer `IDisposable`:

**Before (0.8.0):**
```csharp
using var qrCodeData = QRCodeGenerator.CreateQrCode("Hello", ECCLevel.L);
using var renderer = new QRCodeRenderer();
renderer.Render(...);
```

**After (0.9.0):**
```csharp
var qrCodeData = QRCodeGenerator.CreateQrCode("Hello", ECCLevel.L);
QRCodeRenderer.Render(...);  // Now a static method
```

## 📦 Update Namespace for IconData

If using icons in QR codes:

```csharp
// Add this namespace
using SkiaSharp.QrCode.Image;
```

### 🚫 Removed Features

The following features have been removed:

- `forceUtf8` parameter
- ISO-8859-2 encoding support
- Compression feature
- Kanji encoding mode

If you were using these features, you'll need to adjust your code accordingly.

- `forceUtf8`: SkiaSharp.QrCode now automatically selects UTF-8 when needed.
- ISO-8859-2 and Kanji: Currently not supported; UTF-8 is recommended for most use cases.
- Compression: Removed to simplify the API and improve performance. Please handle compression externally if needed.

Here's an example of how to handle compression externally using [NativeCompressions](https://github.com/Cysharp/NativeCompressions):

```csharp
// compression to zstandard ...
var qrCodeData = QRCodeGenerator.CreateQrCode("Hello", ECCLevel.L);
var src = qrCodeData.GetRawData();
var size = qrCodeData.GetRawDataSize();

var maxSize = NativeCompressions.Zstandard.GetMaxCompressedLength(size);
var compressed = new byte[maxSize];
NativeCompressions.Zstandard.Compress(src, compressed, NativeCompressions.ZstandardCompressionOptions.Default);

// decompression from zstandard ...
var decompressed = NativeCompressions.Zstandard.Decompress(compressed);

// render QR code
var qr = new QRCodeData(decompressed, 4);
var pngBytes = QRCodeImageBuilder.GetPngBytes(qr, 512);
File.WriteAllBytes(path, pngBytes);
```
