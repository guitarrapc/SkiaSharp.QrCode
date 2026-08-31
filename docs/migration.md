# Migration

- **v1.2.0 decodes Kanji mode segments, and adds `QRCodeDecodeStatus.UnmappedCharacter`.** Behaviour change in the decoders: symbols that previously returned `UnsupportedContent` now return `Success` with text, or `UnmappedCharacter` when a character has no JIS X 0208 mapping. The new enum member is appended, so existing values keep their numbers. See [Kanji mode decoding](#kanji-mode-decoding).
- **v1.2.0 adds generator options structs and version ranges.** Purely additive for Standard QR and Micro QR: every existing overload keeps its signature, exceptions and output. See [generator options](#generator-options) below.
- **v1.2.0 introduces rMQR**, whose generator takes `RmQRCodeGeneratorOptions` rather than a parameter list. New in this release, so there is nothing to migrate from. See [rMQR](#rmqr) below.
- **v1.2.0 makes buffer sizing `Try`-only.** `TryGetRequiredBufferSize` is on all three generators; `GetRequiredBufferSize` is `[Obsolete]` on Standard QR and Micro QR and will be removed in 2.0.0. A warning, not a break. See [sizing is Try-only](#sizing-is-try-only) below.
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

## sizing is Try-only

**`GetRequiredBufferSize` is `[Obsolete]` on `QRCodeGenerator` and `MicroQRCodeGenerator`, and will be removed in 2.0.0.** `TryGetRequiredBufferSize` replaces it on all three generators. rMQR has no throwing sizing method at all — it was never released with one.

```csharp
// before
var size = MicroQRCodeGenerator.GetRequiredBufferSize(userInput, MicroQREccLevel.L, quietZoneSize: 2);

// after
if (!MicroQRCodeGenerator.TryGetRequiredBufferSize(userInput, MicroQREccLevel.L, out var size, new MicroQRCodeGeneratorOptions { QuietZoneSize = 2 }))
    return "Content does not fit a Micro QR symbol.";
```

**Why the throwing form is going away, rather than being kept as a convenience.** "The content does not fit" is a data-dependent answer, not a defect: Micro QR holds 5 digits at M1 and rMQR 5–150 bytes, so any caller handling input it did not choose meets an overflow as an ordinary outcome. Reporting that with an exception also costs one to two orders of magnitude more than the encode it is reporting on. This is the shape the modern BCL uses wherever a caller sizes or formats into its own buffer — `Utf8Formatter.TryFormat`, `Utf8Parser.TryParse`, `IUtf8SpanFormattable.TryFormat`, `Base64.EncodeToUtf8` — none of which has a throwing twin. `Parse` / `TryParse` was the wrong precedent to copy.

**Nothing breaks on upgrade.** The obsolete overloads still work and still behave exactly as they did in v1.1.1 — same signatures, same exceptions, same messages. You get a compiler warning (CS0618), not an error, and you have until 2.0.0 to act on it.

- **`false` means the content does not fit, and nothing else.** For Micro QR that includes content whose encoding mode the requested version or ECC level does not offer, since the text is what picks the mode.
- **Invalid arguments still throw**: an undefined ECC level, a `Version` and `Height` that disagree, a Micro QR version and ECC level that cannot be combined, a negative quiet zone, or `EciMode.Iso8859_1` declared over content that is not Latin-1. This mirrors the BCL's own configurable `Try` overloads (`int.TryParse` with a malformed `NumberStyles`, `Dictionary.TryGetValue` with a null key) and keeps a caller from reporting a configuration mistake as "content too long".
- **`true` does not promise the following `Create` call cannot throw** — only that no length-related error can. Pass the returned `Version` back through the options struct to skip the fit; a destination buffer that is too small still throws.
- **Pass `Segmentation` the same value you will encode with** (all three symbologies): the two modes can select different versions, so a buffer sized under one can be too small for the other.
- **The `options` parameter is optional on all three `TryGetRequiredBufferSize` overloads**, so `TryGetRequiredBufferSize(text, ecc, out var size)` is the shortest correct call. This is possible only because there is exactly one sizing overload per generator; the `Create` overloads still require an explicit options value on Standard QR and Micro QR, where the released parameter lists would otherwise make the call ambiguous.

## generator options

`QRCodeGenerator` and `MicroQRCodeGenerator` gained an overload of every entry point that takes an options struct instead of a parameter list. **No behaviour changed**: the parameter list overloads keep their signatures, their exceptions and their output, and the options overloads are an additional way to spell the same calls.

One resolution detail, since adding overloads can move a call: `Create…(text, eccLevel, default)` now binds to the options overload rather than to the third parameter of the parameter list, because a candidate that needs no optional-parameter substitution wins. The symbol it produces is identical, since `default` meant "all defaults" under both readings. It also *fixes* three shapes that did not compile before: `CreateQrCode(text, ecc, default)`, `CreateQrCode(span, ecc, default)` and `CreateMicroQRCode(span, ecc, default)` were ambiguous between the `bool` / `MicroQRVersion?` overload and the `Span<byte>` destination overload, and now resolve.

```csharp
// unchanged, and still the shortest correct call
var a = QRCodeGenerator.CreateQrCode("https://example.com", ECCLevel.M);

// the same thing with options
var b = QRCodeGenerator.CreateQrCode("https://example.com", ECCLevel.M, new QRCodeGeneratorOptions
{
    EciMode = EciMode.Utf8,
    QuietZoneSize = 0,
});
```

New options go on the struct from now on; the parameter lists are frozen at their current shape. `QRCodeGeneratorOptions.Default` is `default`, so an option you do not set keeps the value the parameter list would have applied — including the quiet zone, which is 4 for Standard QR and 2 for Micro QR.

The options parameter has **no default value** on these two symbologies, so pass `QRCodeGeneratorOptions.Default` explicitly if you want the defaults through that overload. Giving it one would make `CreateQrCode(text, eccLevel)` ambiguous between the two sets.

### version ranges

`QRCodeGeneratorOptions.Version` is a `QRCodeVersionRange`, not a single version, and Micro QR has `MicroQRVersionRange`. A pinned version is the degenerate case, so there is one setting rather than two that could contradict each other.

```csharp
Version = 15                                  // pin version 15
Version = new(10, 20)                         // versions 10 to 20, both inclusive
Version = QRCodeVersionRange.AtLeast(10)      // 10 or larger
Version = configuredVersion                   // an int?; null means automatic
// omitted                                    // automatic, exactly as before
```

Bounds are **inclusive**, unlike C#'s `..` range syntax whose end is exclusive. They are validated when the range is constructed, so an impossible one is rejected before any generator sees it.

Two things behave differently from the `requestedVersion` parameter, and only through the options overloads:

- **A pinned version is checked against the content.** `Version = 1` with content that does not fit version 1 throws an `ArgumentException` naming the version, ECC level and mode. The `requestedVersion` parameter still behaves as it did, failing inside the encoder with `ArgumentOutOfRangeException (Parameter 'length')`.
- **`GetRequiredBufferSize` honours the version.** The parameter list overload has no version parameter and is unchanged; the options overload reports the version the range resolves to, and `TryGetRequiredBufferSize` returns `false` when no version in the range holds the content.

`-1` means automatic only in `QRCodeImageBuilder.WithVersion(int)`, which is unchanged. It is **not** accepted by the range type: use `null`, or leave `Version` unset. A `-1` that reached a range would otherwise silently produce an automatically sized symbol where a pinned one was asked for.

### ecc boost

`QRCodeGeneratorOptions.BoostEccLevel` (Standard QR only, off by default) treats the requested ECC level as a minimum: the version is chosen for it as before, then the level is raised as far as that version's spare capacity allows. The symbol size never changes, and sizing is unaffected — only the emitted format information (and possibly the mask) differs. `QRCodeImageBuilder` exposes the same switch as `WithErrorCorrectionBoost()`, which pairs well with `WithIcon`.

```csharp
// Requests M as the floor; the symbol may come out as Q or H at the same size.
var data = QRCodeGenerator.CreateQrCode("https://example.com", ECCLevel.M,
    new QRCodeGeneratorOptions { BoostEccLevel = true });
```

Nothing to migrate: with the option unset, every call produces the exact symbol it produced before.

### mask pattern pinning

`QRCodeGeneratorOptions.MaskPattern` and `MicroQRCodeGeneratorOptions.MaskPattern` (`null` = automatic) pin a specific data mask pattern instead of the automatic selection — one of eight for Standard QR (penalty-scored), one of four for Micro QR (edge-scored); the two numberings are unrelated. Any pattern is a valid symbol; pinning exists to reproduce a symbol produced elsewhere byte-for-byte (`QRCodeDecodeInfo.MaskPattern` / `MicroQRCodeDecodeInfo.MaskPattern` report the pattern a decoder saw) and to exercise scanners against every pattern. The builders expose the same setting as `WithMaskPattern(int?)`. Values outside the symbology's range are rejected when the option is set, like `Version`. rMQR has a single fixed mask, so it has no such option.

Nothing to migrate: with the option unset, every call produces the exact symbol it produced before.

### image builders

`QRCodeImageBuilder.WithVersion` and the Micro QR equivalent gained an overload taking the range type. `WithVersion(int)` and `WithVersion(MicroQRVersion)` are unchanged, including `WithVersion(-1)` meaning automatic.

One behaviour change: `WithVersion(n)` followed by `ToByteArray()` with content too large for version *n* now throws an `ArgumentException` that names the problem, where it previously failed inside the encoder with `ArgumentOutOfRangeException (Parameter 'length')`.

## rMQR

rMQR (ISO/IEC 23941) is new in v1.2.0, so nothing here is a migration. Its generator takes an options struct rather than a parameter list, and unlike the other two it has no parameter list overloads at all:

```csharp
var data = RmQRCodeGenerator.CreateRmQRCode("https://example.com", RmQREccLevel.M);

var constrained = RmQRCodeGenerator.CreateRmQRCode("https://example.com", RmQREccLevel.M, new RmQRCodeGeneratorOptions
{
    Height = RmQRHeight.H9,
    Segmentation = RmQRSegmentation.Optimal,
});
```

Because there is nothing to collide with, the options parameter is defaulted here: `CreateRmQRCode(text, eccLevel)` is the full-defaults call.

There is no version *range* for rMQR. Its 32 versions are not totally ordered (R7x43, R9x43 and R7x59 have no min/max relation), so fit is constrained with `FitStrategy` and `Height` instead.

### mixed-mode segmentation

Set `Segmentation = RmQRSegmentation.Optimal` (rMQR), `QRCodeSegmentation.Optimal` (Standard QR) or `MicroQRSegmentation.Optimal` (Micro QR) to let the generator split mixed content into Numeric / Alphanumeric / Byte runs. It never selects a symbol with more core modules (Standard / Micro QR: a larger version) than `Single`, it emits the `Single` bit stream verbatim whenever splitting would not shrink it, and it additionally encodes content that overflows every version in a single mode. On Micro QR the plan respects each version's mode set (M1 is Numeric-only, M2 has no Byte mode).

Two things to know before opting in: on rMQR the quiet zone adds a fixed 4 modules to each dimension, so a symbol with fewer core modules can still render onto a *larger* grid with a different aspect ratio; and on both symbologies `TryGetRequiredBufferSize` must be passed the same `Segmentation` as the encode, or the destination buffer can be too small.

```csharp
var optimal = RmQRCodeGenerator.CreateRmQRCode(
    "https://example.com/p/1234567890123456",
    RmQREccLevel.M,
    new RmQRCodeGeneratorOptions { Segmentation = RmQRSegmentation.Optimal });   // R15x43 instead of R11x77

var standard = QRCodeGenerator.CreateQrCode(
    "https://example.com/item?id=123456789012345678901234567890",
    ECCLevel.M,
    new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal });   // version 3 instead of 4
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
