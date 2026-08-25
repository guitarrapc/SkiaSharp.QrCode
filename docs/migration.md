# Migration

- **v1.2.0 decodes Kanji mode segments, and adds `QRCodeDecodeStatus.UnmappedCharacter`.** Behaviour change in the decoders: symbols that previously returned `UnsupportedContent` now return `Success` with text, or `UnmappedCharacter` when a character has no JIS X 0208 mapping. The new enum member is appended, so existing values keep their numbers. See [Kanji mode decoding](#kanji-mode-decoding).
- **v1.2.0 adds `TryGetRequiredBufferSize` to all three generators.** Purely additive; nothing existing changes. See [non-throwing sizing](#non-throwing-sizing) below.
- **v1.2.0 adds a `segmentation` argument to the rMQR generator.** Source compatible and behaviour preserving; recompile if you referenced the binary. See [rMQR mixed-mode segmentation](#rmqr-mixed-mode-segmentation) below.
- **After v1.1.0, the image builders share a common base class.** Source compatible; recompile if you referenced the binary. See [image builder base class](#image-builder-base-class) below.
- **v1.0.0 removes the obsolete `QrCode` class.** If you still use `QrCode`, see [from before v1.0.0 to v1.0.0](#from-before-v100-to-v100) below.
- v0.11.0 introduces further improvements to Icon handling. See the IconData section below.
- v0.9.0 introduces significant performance improvements and API changes. Here's what you need to know to upgrade:

## Kanji mode decoding

`QRCodeDecoder`, `MicroQRCodeDecoder` and `RmQRCodeDecoder` now read ISO/IEC 18004 Kanji mode segments (Standard QR all versions, Micro QR M3 / M4, rMQR all versions). Nothing about encoding changed: the generators still write Japanese text as UTF-8 in Byte mode.

- **Behaviour change, not source or binary breaking.** A symbol carrying a Kanji segment previously decoded to `QRCodeDecodeStatus.UnsupportedContent`; it now returns `Success` with the decoded text. Code that branches on `UnsupportedContent` to hand the symbol to another reader will stop taking that branch, and `TryDecode` now writes characters into a destination span where it previously wrote none.
- **The mapping is JIS X 0208, not CP932.** The two disagree on seven Shift_JIS cells (0x815F, 0x8160, 0x8161, 0x817C, 0x8191, 0x8192, 0x81CA: reverse solidus, wave dash, double vertical line, minus sign, and the cent / pound / not signs). If you compare output against a CP932-based reader such as ZXing.Net, expect those seven to differ.
- **CP932-only characters are rejected, not substituted.** Within the Kanji-mode range CP932 defines 83 characters JIS X 0208 does not: the NEC row 13 block (circled digits, roman numerals, unit ligatures). A Kanji segment containing one fails the whole symbol with the new `QRCodeDecodeStatus.UnmappedCharacter`, which is deliberately distinct from `UnsupportedContent` so a caller can route just these symbols to a CP932-capable reader.
- **ECI 20 (Shift_JIS) Byte segments are still unsupported** and still report `UnsupportedContent`.

## non-throwing sizing

`QRCodeGenerator`, `MicroQRCodeGenerator` and `RmQRCodeGenerator` gained `TryGetRequiredBufferSize`, plus `RmQRCodeGenerator.TryGetRequiredBufferSizeWithEci`. Nothing existing changed: the `GetRequiredBufferSize` overloads keep their signatures, their exceptions and their messages.

```csharp
if (!MicroQRCodeGenerator.TryGetRequiredBufferSize(userInput, MicroQREccLevel.L, out var size))
    return "Content does not fit a Micro QR symbol.";
```

Reach for these when the content is user-supplied. Micro QR holds 5 digits at M1 and rMQR 5–150 bytes, so overflow is an ordinary branch there rather than a defect, and an exception costs one to two orders of magnitude more than the encode itself.

- **`false` means the content does not fit, and nothing else.** For Micro QR that includes content whose encoding mode the requested version or ECC level does not offer, since the text is what picks the mode.
- **Invalid arguments still throw**, with the same type and message as `GetRequiredBufferSize`: an undefined ECC level, a `requestedVersion` and `height` that disagree, a Micro QR version and ECC level that cannot be combined, a negative quiet zone, or `EciMode.Iso8859_1` declared over content that is not Latin-1. This mirrors the BCL's own configurable `Try` overloads (`int.TryParse` with a malformed `NumberStyles`, `Dictionary.TryGetValue` with a null key) and keeps a caller from reporting a configuration mistake as "content too long".
- **`true` does not promise the following `Create` call cannot throw** — only that no length-related error can. Pass the returned `Version` back as `requestedVersion` to skip the fit; a destination buffer that is too small still throws.
- **Pass rMQR's `segmentation` the same value you will encode with**, exactly as with `GetRequiredBufferSize`: the two modes can select different versions.

## rMQR mixed-mode segmentation

`RmQRCodeGenerator.CreateRmQRCode`, `CreateRmQRCodeWithEci`, `GetRequiredBufferSize` and `GetRequiredBufferSizeWithEci` gained a trailing optional `RmQRSegmentation segmentation` argument, and `RmQRCodeImageBuilder` gained `WithSegmentation`.

- **Behaviour unchanged by default.** The argument defaults to `RmQRSegmentation.Single`, which is the existing single-mode encoding; every symbol you generate today is byte for byte the same.
- **Source compatible**, including positional calls, because the new argument is last.
- **Binary breaking**, as with any added parameter: assemblies compiled against v1.1.1 or earlier must be recompiled (no code changes needed).

Pass `RmQRSegmentation.Optimal` to let the generator split mixed content into Numeric / Alphanumeric / Byte runs. It never selects a symbol with more core modules than `Single`, it emits the `Single` bit stream verbatim whenever splitting would not shrink it, and it additionally encodes content that overflows every version in a single mode.

Two things to know before opting in: the quiet zone adds a fixed 4 modules to each dimension, so a symbol with fewer core modules can still render onto a *larger* grid with a different aspect ratio; and `GetRequiredBufferSize` must be passed the same `segmentation` as the encode, or the destination buffer can be too small.

```csharp
var optimal = RmQRCodeGenerator.CreateRmQRCode(
    "https://example.com/p/1234567890123456",
    RmQREccLevel.M,
    segmentation: RmQRSegmentation.Optimal);   // R15x43 instead of R11x77
```

## image builder base class

`QRCodeImageBuilder` and `MicroQRCodeImageBuilder` now derive from `QRCodeImageBuilderBase<TSelf>`, which carries the options every symbology shares (`WithSize`, `WithModulePixelSize`, `WithFormat`, `WithQuietZone`, `WithColors`, `WithModuleShape`, `WithGradient`) and the complete output surface (`SaveTo`, `SaveToSvg`, `ToSvgString`, `ToByteArray`, `ToImage`, `ToBitmap`). Symbology-specific options (`WithErrorCorrection`, `WithVersion`, and on Standard QR `WithIcon`, `WithFinderPatternShape`, `WithEciMode`) stay on the concrete builders.

- **Source compatible**, fluent chains compile unchanged; the self-referential type parameter keeps every method returning the concrete builder type.
- **Binary breaking**, the shared members moved to the base class, so assemblies compiled against an older version must be recompiled (no code changes needed).
- `WithQuietZone` no longer declares a default argument value (it was 4 for Standard QR, 2 for Micro QR, a value the builder already starts with). Calling `WithQuietZone()` with no argument no longer compiles; simply remove the call.

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
- ISO-8859-2 and Kanji: not supported for ENCODING; UTF-8 is recommended for most use cases. Kanji segments produced by other encoders are read since v1.2.0, see [Kanji mode decoding](#kanji-mode-decoding).
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
