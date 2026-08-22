# rMQR Encoder

Design record for the rMQR Code (ISO/IEC 23941) encode feature (`RmQRCodeGenerator`): what it does, the symbol parameters it is built on, why the pipeline is structured this way, and the decisions made up front so implementation phases share one understanding. Normative details and implementation locations are indexed in the [spec-to-code map](rmqr-spec-map.md); the implementation order was the rMQR implementation plan, now retired into this record and the [spec-to-code map](rmqr-spec-map.md). The decoder design record is [rMQR Decoder](rmqr-decoder.md).

Status: **shipped (Phase 5, 2026-08-15; adversarial review 2026-08-16)**. Written spec-first on 2026-08-15 before any `src/` code existed; the tables live in `Internals/RmQr/RmQRConstants` (Phase 5.1b), the data model in `RmQRCodeData` (5.2), the bit stream in `RmQRBinaryEncoder` and the fit logic in `RmQRVersionSelector` (5.3), RS + interleave in `RmQRCodewordEncoder` (5.4), placement in `RmQRModulePlacer` (5.5) and the public `RmQRCodeGenerator` (5.6, encoder MVT met: 256/256 symbols read by zxing-cpp) and rendering in `RmQRCodeImageBuilder` / `QRCodeRenderer` (5.7); Phase 5 is complete and every parameter below is pinned by `RmQRConstantsUnitTest` (structural invariants) and `RmQRConstantsOracleTest` (the committed two-lineage corpus), see the [Verification record](#verification-record). Measured performance and lessons learned are consolidated below.

---

## What

`RmQRCodeGenerator` converts text into an rMQR module matrix through the ISO/IEC 23941 encoding pipeline:

```
Text
  -> mode analysis
  -> version fit (exact version, or fit strategy within an optional height constraint)
  -> data bit stream and padding
  -> Reed-Solomon ECC per block
  -> data / ECC interleaving
  -> function-pattern and data placement
  -> fixed data mask
  -> format information (two copies)
  -> RmQRCodeData or byte-per-module matrix
```

### Public entry points (signatures frozen 2026-08-15, Phase 5.0 review)

Names and overload sets mirror the shipped `MicroQR*` family member for member (reviewed against `MicroQRCodeGenerator`, `MicroQRCodeDecoder`, `MicroQRCodeImageBuilder`, `MicroQRCodeData`, the renderer overloads and `QrImageBuilderApiParityTest`); the only additions are the rectangular geometry and the two fit parameters. Names below are the contract the implementation phases code against; a deviation is a spec change first.

Enumerations:

```csharp
public enum RmQREccLevel { M = 0, H = 1 }            // own domain like MicroQREccLevel; value = the ECC bit in the format information
public enum RmQRVersion  { R7x43 = 1, R7x59, R7x77, R7x99, R7x139, R9x43, …, R17x139 = 32 }   // height-major, value = version index + 1 = libzint version number
public enum RmQRFitStrategy { MinimizeArea = 0, MinimizeWidth = 1, MinimizeHeight = 2 }
public enum RmQRHeight { H7 = 7, H9 = 9, H11 = 11, H13 = 13, H15 = 15, H17 = 17 }
```

Generator (`public static class RmQRCodeGenerator`, `DefaultQuietZone = 2`):

```csharp
RmQRCodeData CreateRmQRCode(string plainText, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone);
RmQRCodeData CreateRmQRCode(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone);
int CreateRmQRCode(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, Span<byte> destination, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone);   // byte per module, row-major, quiet zone included, returns bytes written
RmQRCodeCalculatedSize GetRequiredBufferSize(ReadOnlySpan<char> text, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone);
public readonly struct RmQRCodeCalculatedSize { int BufferSize; int Width; int Height; RmQRVersion Version; }   // Width/Height include the quiet zone
```

`requestedVersion` and `height` together are accepted only when they agree (else `ArgumentException`); `fitStrategy` is ignored when `requestedVersion` is given.

Data model (`public class RmQRCodeData`):

```csharp
RmQRCodeData(RmQRVersion version, int quietZoneSize);
RmQRCodeData(byte[] rawData, int quietZoneSize);
RmQRCodeData(ReadOnlySpan<byte> rawData, int quietZoneSize);
int Width { get; }   int Height { get; }   RmQRVersion Version { get; }     // quiet zone included
bool this[int row, int col] { get; }                                          // quiet zone reads false
int GetRawDataSize();  byte[] GetRawData();  int GetRawData(IBufferWriter<byte> writer);   // "QRX" + type 2 + width + height + packed core bits
```

Decoder (`public static class RmQRCodeDecoder`):

```csharp
bool TryDecode(RmQRCodeData data, out string text);
bool TryDecode(RmQRCodeData data, out string text, out RmQRCodeDecodeInfo info);
bool TryDecode(ReadOnlySpan<byte> modules, int width, int height, out string text, out RmQRCodeDecodeInfo info);                          // byte per module, any light border (uniform or not: the dark bounding box is the core)
bool TryDecode(ReadOnlySpan<byte> modules, int width, int height, Span<char> destination, out int charsWritten, out RmQRCodeDecodeInfo info);
bool TryDecode(SKBitmap bitmap, out string text);
bool TryDecode(SKBitmap bitmap, out string text, out RmQRCodeDecodeInfo info);
bool TryDecodeImage(ReadOnlySpan<byte> luminance, int width, int height, out string text, out RmQRCodeDecodeInfo info);
bool TryDecodeImage(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out RmQRCodeDecodeInfo info);
int GetMaxDecodedLength(RmQRVersion version);
public readonly struct RmQRCodeDecodeInfo { QRCodeDecodeStatus Status; RmQRVersion Version; RmQREccLevel EccLevel; int ErrorsCorrected; }   // no MaskPattern: rMQR has one mask
```

Rendering:

```csharp
public class RmQRCodeImageBuilder : QRCodeImageBuilderBase<RmQRCodeImageBuilder>
  RmQRCodeImageBuilder(string content);  RmQRCodeImageBuilder(RmQRCodeData data);          // default quiet zone 2
  RmQRCodeImageBuilder WithErrorCorrection(RmQREccLevel eccLevel);  WithVersion(RmQRVersion version);
  RmQRCodeImageBuilder WithFitStrategy(RmQRFitStrategy fitStrategy);  WithHeight(RmQRHeight height);  WithWidth(int width);   // rMQR-only, listed in the parity test's allowed differences (WithWidth: image width in pixels, height from the aspect ratio, background over the whole image)
  // static helpers exactly as MicroQRCodeImageBuilder (GetPngBytes / GetImageBytes / SavePng / GetSvgBytes / SaveSvg / GetSvgString / WriteSvg / WritePng / WriteImage,
  // string + RmQREccLevel eccLevel = RmQREccLevel.M and RmQRCodeData overloads); their `int size = 512` is the image WIDTH, height follows the symbol aspect ratio
QRCodeRenderer.Render(SKCanvas canvas, SKRect area, RmQRCodeData data, SKColor? codeColor, SKColor? backgroundColor, ModuleShape? moduleShape = null, float moduleSizePercent = 1.0f, GradientOptions? gradientOptions = null);
SKCanvas.Render(this SKCanvas canvas, RmQRCodeData data, int width, int height, SKColor? clearColor = null, SKColor? codeColor = null, SKColor? backgroundColor = null, ModuleShape? moduleShape = null, float moduleSizePercent = 1.0f, GradientOptions? gradientOptions = null);
SKCanvas.Render(this SKCanvas canvas, RmQRCodeData data, SKRect area, …same tail…);
```

Rectangular geometry rule shared by every rendering entry: the symbol (quiet zone included) is drawn with a uniform module scale, centered in the target area or canvas (letterbox); `WithModulePixelSize` yields exactly `Width × Height` modules × pixels; `WithSize(w, h)` letterboxes into `w × h` (clear-colour pad); `WithWidth(w)` (the static helpers' `size`, default 512) makes the image `w` wide with the height from the aspect ratio rounded to whole pixels, the background covering the whole image and the symbol drawn at a uniform module scale inside it. Standard and Micro QR rendering is unchanged.

### Supported

| Area | Coverage |
|---|---|
| Symbology | rMQR |
| Versions | All 32 (R7x43 … R17x139) |
| ECC levels | M, H |
| Data modes | Numeric, Alphanumeric, Byte (ECI 3 for ISO-8859-1, ECI 26 for UTF-8; ASCII omits ECI) |
| Segmentation | One segment in a single mode (default), or the minimal-bit mixed-mode split (opt-in `RmQRSegmentation.Optimal`) |
| Version selection | Exact version, or automatic fit by strategy, optionally within a fixed height |
| Quiet zone | Configurable non-negative size, default 2 (the ISO/IEC 23941 quiet zone) |
| Output | Bit-packed `RmQRCodeData` or byte-per-module `Span<byte>` |

### Not implemented

- Kanji mode, intentionally (tables keep the column only for specification completeness;
  Japanese text uses Byte mode with UTF-8 ECI, matching the Standard QR product policy)
- FNC1, Structured Append (rMQR does not define Structured Append)

### Symbol parameters (verified)

Version index is height-major (all widths of height 7, then 9, …); it is the 5-bit value carried in the format information. Alignment columns are the 0-based columns of the vertical timing patterns, each capped by a 3×3 alignment pattern at the top and bottom edge. Data codewords are split across blocks with sizes differing by at most one (the smaller blocks first), and every block carries the same ECC codeword count.

| Index | Version | Modules | Alignment columns | Total codewords | M: data / blocks / ECC per block | H: data / blocks / ECC per block | Count indicator bits N / A / B |
|---|---|---|---|---|---|---|---|
| 0 | R7x43 | 7 x 43 | 21 | 13 | 6 / 1 / 7 | 3 / 1 / 10 | 4 / 3 / 3 |
| 1 | R7x59 | 7 x 59 | 19, 39 | 21 | 12 / 1 / 9 | 7 / 1 / 14 | 5 / 5 / 4 |
| 2 | R7x77 | 7 x 77 | 25, 51 | 32 | 20 / 1 / 12 | 10 / 1 / 22 | 6 / 5 / 5 |
| 3 | R7x99 | 7 x 99 | 23, 49, 75 | 44 | 28 / 1 / 16 | 14 / 1 / 30 | 7 / 6 / 5 |
| 4 | R7x139 | 7 x 139 | 27, 55, 83, 111 | 68 | 44 / 1 / 24 | 24 / 2 / 22 | 7 / 6 / 6 |
| 5 | R9x43 | 9 x 43 | 21 | 21 | 12 / 1 / 9 | 7 / 1 / 14 | 5 / 5 / 4 |
| 6 | R9x59 | 9 x 59 | 19, 39 | 33 | 21 / 1 / 12 | 11 / 1 / 22 | 6 / 5 / 5 |
| 7 | R9x77 | 9 x 77 | 25, 51 | 49 | 31 / 1 / 18 | 17 / 2 / 16 | 7 / 6 / 5 |
| 8 | R9x99 | 9 x 99 | 23, 49, 75 | 66 | 42 / 1 / 24 | 22 / 2 / 22 | 7 / 6 / 6 |
| 9 | R9x139 | 9 x 139 | 27, 55, 83, 111 | 99 | 63 / 2 / 18 | 33 / 3 / 22 | 8 / 7 / 6 |
| 10 | R11x27 | 11 x 27 | - | 15 | 7 / 1 / 8 | 5 / 1 / 10 | 4 / 4 / 3 |
| 11 | R11x43 | 11 x 43 | 21 | 31 | 19 / 1 / 12 | 11 / 1 / 20 | 6 / 5 / 5 |
| 12 | R11x59 | 11 x 59 | 19, 39 | 47 | 31 / 1 / 16 | 15 / 2 / 16 | 7 / 6 / 5 |
| 13 | R11x77 | 11 x 77 | 25, 51 | 67 | 43 / 1 / 24 | 23 / 2 / 22 | 7 / 6 / 6 |
| 14 | R11x99 | 11 x 99 | 23, 49, 75 | 89 | 57 / 2 / 16 | 29 / 2 / 30 | 8 / 7 / 6 |
| 15 | R11x139 | 11 x 139 | 27, 55, 83, 111 | 132 | 84 / 2 / 24 | 42 / 3 / 30 | 8 / 7 / 7 |
| 16 | R13x27 | 13 x 27 | - | 21 | 12 / 1 / 9 | 7 / 1 / 14 | 5 / 5 / 4 |
| 17 | R13x43 | 13 x 43 | 21 | 41 | 27 / 1 / 14 | 13 / 1 / 28 | 6 / 6 / 5 |
| 18 | R13x59 | 13 x 59 | 19, 39 | 60 | 38 / 1 / 22 | 20 / 2 / 20 | 7 / 6 / 6 |
| 19 | R13x77 | 13 x 77 | 25, 51 | 85 | 53 / 2 / 16 | 29 / 2 / 28 | 7 / 7 / 6 |
| 20 | R13x99 | 13 x 99 | 23, 49, 75 | 113 | 73 / 2 / 20 | 35 / 3 / 26 | 8 / 7 / 7 |
| 21 | R13x139 | 13 x 139 | 27, 55, 83, 111 | 166 | 106 / 3 / 20 | 54 / 4 / 28 | 8 / 8 / 7 |
| 22 | R15x43 | 15 x 43 | 21 | 51 | 33 / 1 / 18 | 15 / 2 / 18 | 7 / 6 / 6 |
| 23 | R15x59 | 15 x 59 | 19, 39 | 74 | 48 / 1 / 26 | 26 / 2 / 24 | 7 / 7 / 6 |
| 24 | R15x77 | 15 x 77 | 25, 51 | 103 | 67 / 2 / 18 | 31 / 3 / 24 | 8 / 7 / 7 |
| 25 | R15x99 | 15 x 99 | 23, 49, 75 | 136 | 88 / 2 / 24 | 48 / 4 / 22 | 8 / 7 / 7 |
| 26 | R15x139 | 15 x 139 | 27, 55, 83, 111 | 199 | 127 / 3 / 24 | 69 / 5 / 26 | 9 / 8 / 7 |
| 27 | R17x43 | 17 x 43 | 21 | 61 | 39 / 1 / 22 | 21 / 2 / 20 | 7 / 6 / 6 |
| 28 | R17x59 | 17 x 59 | 19, 39 | 88 | 56 / 2 / 16 | 28 / 2 / 30 | 8 / 7 / 6 |
| 29 | R17x77 | 17 x 77 | 25, 51 | 122 | 78 / 2 / 22 | 38 / 3 / 28 | 8 / 7 / 7 |
| 30 | R17x99 | 17 x 99 | 23, 49, 75 | 160 | 100 / 3 / 20 | 56 / 4 / 26 | 8 / 8 / 7 |
| 31 | R17x139 | 17 x 139 | 27, 55, 83, 111 | 232 | 152 / 4 / 20 | 76 / 6 / 26 | 9 / 8 / 8 |

Kanji count-indicator widths are not in this table: the mode is intentionally unsupported and
cannot be verified with the available oracle command lines. `RmQRConstants.GetKanjiCountIndicatorLength`
carries them spec-transcribed with an "unverified" comment (values 2-7, monotone below the byte
widths) only to keep the constants table complete.

Data capacity in characters (Numeric / Alphanumeric / Byte), single segment, no ECI header:

| Version | M: Numeric / Alphanumeric / Byte | H: Numeric / Alphanumeric / Byte |
|---|---|---|
| R7x43 | 12 / 7 / 5 | 5 / 3 / 2 |
| R7x59 | 26 / 16 / 11 | 14 / 8 / 6 |
| R7x77 | 45 / 27 / 19 | 21 / 13 / 9 |
| R7x99 | 64 / 39 / 27 | 30 / 18 / 13 |
| R7x139 | 102 / 62 / 42 | 54 / 33 / 22 |
| R9x43 | 26 / 16 / 11 | 14 / 8 / 6 |
| R9x59 | 47 / 29 / 20 | 23 / 14 / 10 |
| R9x77 | 71 / 43 / 30 | 37 / 23 / 16 |
| R9x99 | 97 / 59 / 40 | 49 / 30 / 20 |
| R9x139 | 147 / 89 / 61 | 75 / 46 / 31 |
| R11x27 | 14 / 8 / 6 | 9 / 6 / 4 |
| R11x43 | 42 / 26 / 18 | 23 / 14 / 10 |
| R11x59 | 71 / 43 / 30 | 33 / 20 / 14 |
| R11x77 | 100 / 60 / 41 | 52 / 31 / 21 |
| R11x99 | 133 / 81 / 55 | 66 / 40 / 27 |
| R11x139 | 198 / 120 / 82 | 97 / 59 / 40 |
| R13x27 | 26 / 16 / 11 | 14 / 8 / 6 |
| R13x43 | 62 / 37 / 26 | 28 / 17 / 12 |
| R13x59 | 88 / 53 / 36 | 45 / 27 / 18 |
| R13x77 | 124 / 75 / 51 | 66 / 40 / 27 |
| R13x99 | 171 / 104 / 71 | 80 / 49 / 33 |
| R13x139 | 251 / 152 / 104 | 126 / 76 / 52 |
| R15x43 | 76 / 46 / 31 | 33 / 20 / 13 |
| R15x59 | 112 / 68 / 46 | 59 / 36 / 24 |
| R15x77 | 157 / 95 / 65 | 71 / 43 / 29 |
| R15x99 | 207 / 126 / 86 | 111 / 68 / 46 |
| R15x139 | 301 / 182 / 125 | 162 / 98 / 67 |
| R17x43 | 90 / 55 / 37 | 47 / 28 / 19 |
| R17x59 | 131 / 79 / 54 | 63 / 38 / 26 |
| R17x77 | 183 / 111 / 76 | 87 / 53 / 36 |
| R17x99 | 236 / 143 / 98 | 131 / 79 / 54 |
| R17x139 | 361 / 219 / 150 | 178 / 108 / 74 |

Other symbol facts the pipeline is built on (all verified, see the record below): a single data mask `((row ⁄ 2) + (col ⁄ 3)) mod 2 = 0`; format information = 6 data bits (ECC bit, M = 0 / H = 1, above the 5-bit version index) BCH-extended to 18 bits, two copies with distinct XOR masks (finder side, sub-finder side); 3-bit mode indicators, terminator `000`, pad codewords 0xEC / 0x11; standard block interleaving; two-column zigzag placement starting at the column pair left of the right-edge timing column, upward first, right column first; quiet zone 2 modules.

---

## Pipeline

### 1. Validate the request

Reject an unknown version, an unknown ECC level, a `height` constraint combined with a `requestedVersion` of a different height, and negative quiet zones. Span sizing / output additionally reject dimensions that overflow `int`, exactly as `MicroQRCodeGenerator` does.

### 2. Analyze text

Shared `TextAnalyzer` (Numeric / Alphanumeric / Byte, single segment). The default charset policy matches Standard QR: ASCII omits ECI, ISO-8859-1 text emits assignment 3, and other Unicode text is encoded as UTF-8 with assignment 26. An explicit `EciMode` can select ISO-8859-1 or UTF-8; explicit ISO-8859-1 rejects unrepresentable input instead of narrowing it.

The analyzer decides the charset for every path. It also decides the mode for the default single-segment path; `RmQRSegmentation.Optimal` decides modes per run instead (see [Mixed-mode segmentation](#mixed-mode-segmentation)) but takes the charset from the same analysis, because the charset is a property of the content and not of the split.

### Mixed-mode segmentation

**What.** `RmQRSegmentation.Optimal` splits the content into the Numeric / Alphanumeric / Byte runs whose total bit cost is minimal for a candidate version, and fits the version against that cost instead of the single-mode cost. `RmQRSegmentation.Single` (the default) keeps one run in one mode.

**Why.** rMQR data capacities are small (5 Byte-mode characters at R7x43-M, 150 at R17x139-M), so the modes a payload mixes decide the symbol size far more often than in Standard QR. A URL followed by a numeric identifier is the common case: `https://example.com/p/1234567890123456` needs 313 bits as one Byte run (R11x77, 847 modules) and 249 bits as Byte + Numeric (R15x43, 645 modules).

**Why opt-in, and why the ceiling.** Changing the default would move the emitted bit stream, and therefore the rendered symbol, for existing callers. When the content fits in a single mode, that fit bounds the search from above: only versions the strategy ranks strictly better than it are tried, so the plan is emitted only when it actually shrinks the symbol, and the single-mode stream is emitted byte for byte in every other case. This is the property the end-to-end tests assert for every content, ECC level and strategy.

**When no single mode fits.** The ceiling does not exist, so the scan runs to the end. This is the one place where `Optimal` accepts input `Single` rejects rather than merely shrinking it, and it is the case the option is worth the most in: 100 letters followed by 100 digits is 200 Byte-mode characters, 50 over the 150 R17x139-M holds, but 1157 bits of the 1216 available once the digits split off. Only when a mixed plan fails as well does `RmQRVersionSelector` produce the capacity error, so a genuinely oversized payload reports exactly what it reports today.

**Content that cannot benefit.** All-Numeric content skips planning: digits are the cheapest characters in the cheapest mode, splitting a Numeric run never lowers its payload, and every extra run adds a header, so one run is provably the optimum. Without the shortcut a 361-digit payload paid 11.9x a `Single` encode to rediscover it; with it, 1.05x.

**Pruning the scan.** The scan is best-first, i.e. smallest-version-first, so for mixed content most early ranks are hopeless. One cost run at the narrowest count indicator widths any version uses (4 / 3 / 3) prices a floor for every version — widening a count indicator can only raise the price of the run carrying it, and the minimum over plans of a pointwise larger cost is itself larger — so candidates that cannot hold even that floor are skipped without a run of their own. This replaces up to a dozen cost runs with one: normalised against the Standard QR v1 span encode in the same benchmark run, the 150-byte worst case fell from 13.2x to 3.0x and a mixed URL from 3.9x to 2.1x.

**What it costs, and what drives it.** Planning allocates nothing. Its cost is driven by *how much the split helps*, not by how mixed the content looks: the lower a split drives the bit cost, the more candidate versions clear the floor and get priced. Measured on the span path at 120 characters (same run): all digits 714 ns to 763 ns (short-circuited), all lowercase 989 ns to 2 510 ns (searched, never wins), 60 lowercase + 60 digits 989 ns to 7 161 ns (searched, wins a version). Cost is linear in length at a fixed shape (20 / 60 / 120 / 150 characters of half letters half digits: 1.8 / 4.8 / 8.5 / 12.0 us). The consequence worth stating plainly is that the expensive inputs are the rewarding ones.

The corollary is why this cannot be made free: whether a split helps is only knowable by planning it. All-Numeric is the one shape where "no gain" is provable up front. A payload of a known shape — a URL followed by a numeric identifier — therefore wins predictably, while arbitrary user input trades a few microseconds per symbol against the chance of a smaller one. `RmQREncodeEndToEnd` carries `MixedUrl` / `MaxByte` / `MaxNumeric` Single-vs-Optimal pairs as the regression guard.

**How the optimum is exact.** A run does not cost a constant per character (Numeric packs 3 digits into 10 bits, Alphanumeric 2 characters into 11), so the dynamic program carries the group remainder in its state: 3 Numeric states, 2 Alphanumeric, 1 Byte, plus a virtual start. Each transition either continues the current run or opens a new one paying `3 + count indicator`. Rounding a per-character average instead would misprice the tail of every run.

**Bounds.** Content longer than 361 characters is rejected before any cost run, so pathological input never pays for planning. 361 is the largest character count any rMQR symbol holds in any mode (Numeric at R17x139-M: 1204 payload bits plus a 12-bit header against 1216 available), and no mixed plan can beat it because mixing only adds headers to a mode that is already the densest per character. Since a mixed plan can now encode content no single mode holds, this constant is a rejection rule and not merely a work cap: at 362 characters the cheapest possible cost is 1207 payload bits plus the narrowest R17x139 header of 11, i.e. 1218 against 1216, a margin of 2 bits. A plan is capped at 96 runs, above the 76 the largest capacity could hold. The reconstructed plan is re-costed from the byte counts the encoder will actually emit and rejected on disagreement, because the bit-stream writers store without per-flush bounds checks.

**ECI.** One prefix ahead of the first run: an rMQR decoder carries the declared charset across the runs that follow, so a plan needs no repetition. Its 11 bits are part of the cost the version scan compares.

**Kanji.** Still unsupported, so a Japanese payload mixes Byte (UTF-8) with Numeric runs rather than reaching for 13-bit Kanji.

### 3. Fit the version

Required bits = optional 11-bit ECI prefix (`111` + 8-bit assignment) + 3 (data mode) + count indicator (per version, table above) + payload bits. The terminator may shrink to the remaining capacity, including zero bits. Automatic fit is a table scan (versions pre-ordered best-first per strategy with their capacity per mode × ECC × ECI-presence, height as a bitmask); it selects exactly what the definitional "best fitting version" scan selects, and a test pins the two for every input.

Under `RmQRSegmentation.Optimal` the required bits are the planned mixed-mode cost for the candidate version rather than the single-mode cost. The candidate set, the strategy ordering, the height constraint and the error text are all unchanged; the one behavioural difference is that input the single mode overflows at every version can now succeed instead of throwing (see [Mixed-mode segmentation](#mixed-mode-segmentation)).

- `requestedVersion` given: use it or fail with an actionable capacity error (actual length, applicable maximum in mode units, remedy: shorten, lower ECC, choose a larger version, or use Standard QR). Under `Optimal` a requested version that the single mode overflows is still accepted when the mixed-mode plan fits it.
- Otherwise the candidate set is all 32 versions, or the versions of the constrained `height`; keep those whose data-codeword capacity holds the required bits; choose by `fitStrategy`:
  - `MinimizeArea`: fewest modules (height × width); ties toward the smaller height (i.e. the wider symbol).
  - `MinimizeWidth`: smallest width; ties toward the smaller height.
  - `MinimizeHeight`: smallest height; ties toward the smaller width.
- No candidate fits: capacity error stating the maximum for the most capacious candidate in the set.

### 4. Build the data codewords

Optional ECI mode `111` plus an 8-bit assignment, then per run a 3-bit data-mode indicator, count indicator and payload bits (one run by default, the planned runs in order under `Optimal`), terminator `000` (shortened at capacity), zero bits to a byte boundary, alternating 0xEC / 0x11 pads to the data-codeword count. The stream is written straight into the caller's buffer (no intermediate copy); vectorized value kernels exist per mode on x64 (see Decisions), all producing the identical stream. UTF-8 stays in a separate cold writer so adding ECI does not address-expose the hot writer locals.

### 5. Reed-Solomon per block, 6. interleave

Blocks per the table (smaller data blocks first, sizes differ by at most one), ECC per block via shared `EccBinaryEncoder`, then Standard-QR-style interleaving: all data codewords column-wise across blocks, then all ECC codewords. Remainder bits (free modules − 8 × total codewords, 0..7 per version) are light.

### 7. Place function patterns and data

Finder (7×7 with separators), sub-finder (5×5), four edge timing patterns, the two corner patterns, vertical timing columns with 3×3 alignment patterns at both ends, and both format regions are function modules; data fills the rest in zigzag order. Coordinates live in code comments in Phase 5.5. The fast placer reproduces the reference module for module from cached per-version tables (see Decisions).

### 8. Fixed mask, 9. format information

The single mask is applied to data modules while placing. Both format copies come from a static 64-entry table indexed by (version, ECC).

---

## Rendering

`RmQRCodeImageBuilder` derives from `QRCodeImageBuilderBase<TSelf>` and adds `WithErrorCorrection(RmQREccLevel)`, `WithEciMode(EciMode)`, `WithVersion(RmQRVersion)`, `WithFitStrategy(RmQRFitStrategy)`, `WithHeight(RmQRHeight)`; quiet zone default 2; no icon overlay or finder styling (one finder, no ECC headroom to spend). Canvas layout is rectangular: with a module pixel size the content is `width × height` modules at that size; with only an explicit canvas size the symbol is fitted with a uniform module scale and centered on whole pixels (letterbox), never stretched non-uniformly. Standard and Micro QR layout is unchanged. Shipped in Phase 5.7 exactly so; additionally, `WithWidth(int)` (public since the 2026-08-16 review; the static helpers use it with their `size`, and 512 is the default when no size option is given) makes the image that wide with the height following the symbol aspect ratio rounded to whole pixels, the background covering the whole image and the symbol drawn at a uniform module scale inside it (no clear-colour pad, so the image is opaque with an opaque background; the review found that letterboxing this aspect-derived canvas again left 1-3 transparent columns on 12 of the 32 versions), and the low-level `QRCodeRenderer.Render(canvas, area, RmQRCodeData, …)` / `SKCanvas.Render` overloads letterbox into the given area with the background covering the whole area.

---

## Why

- Separate `RmQR*` entry points, not `CreateQrCode` overloads: version, ECC and fit semantics differ per symbology; see [QR Symbology Architecture](qrcode-symbologies.md).
- Two-dimensional fit exposed as strategy + optional height constraint: rMQR exists to fit narrow print lanes; "fixed height, auto width" is the dominant real-world request (libzint's `R<h>xauto`), and area/width/height minimization covers the rest without a free-form size search that would mostly select non-existent sizes.
- Letterbox instead of stretch for explicit canvas sizes: a rectangular symbol drawn into an arbitrary rectangle at non-uniform scale is not the same symbol; module aspect ratio must survive.
- Fixed mask means the placer is a static permutation per version; no mask scoring machinery is designed in.
- Superseded 2026-08-18: emitting UTF-8 without ECI made decoding depend on reader heuristics.
  rMQR supports ECI unlike Micro QR, so the encoder will explicitly emit ISO-8859-1 assignment
  3 or UTF-8 assignment 26, following Standard QR's policy. Kanji mode remains intentionally
  unsupported; ECI + Byte mode is the interoperable Unicode path.

## Decisions

| Decision | Choice | Revisit when |
|---|---|---|
| Naming | `RmQR*` family, `RmQRVersion` with 32 named members | Never (mirrors shipped `MicroQR*`) |
| Version fit API | `RmQRFitStrategy` + `RmQRHeight?` | User demand for width constraints (would add `RmQRWidth?` symmetric to height) |
| Default fit strategy | `MinimizeArea` (fewest modules), **confirmed in Phase 5.6**: both reference encoders choose the same versions automatically (libzint and qrtool with no version option: 12 digits at M → R11x27, 15 → R13x27, 100 → R11x77, measured by `probe-rmqr`), so the default keeps interoperability parity and the printable-area argument; the surprise case (12 digits at M: R11x27 (297) rather than the flatter R7x43 (301)) is documented in the generator XML docs and pinned by `RmQRCodeGeneratorUnitTest`; users wanting the flattest symbol use `MinimizeHeight` or a fixed `RmQRHeight` (README example lands with the rendering surface in 5.7) | User feedback after release |
| Explicit-canvas layout | Uniform scale, centered (letterbox) | - |
| ECI on encode | Implemented 2026-08-18: none for ASCII; assignment 3 for ISO-8859-1; assignment 26 for UTF-8. Only R7x43-H cannot hold an ECI header plus a one-byte payload | Additional charset demand (cross-symbology decision) |
| Kanji | Intentionally unsupported; use Byte mode with UTF-8 ECI (tables retain the column only for specification completeness) | Cross-symbology policy change backed by concrete demand |
| Interleaver | Lifted `BinaryInterleaver` to `Internals.BinaryEncoders` (Phase 5.4): it never used the version, only the `ECCInfo` block structure; the remainder-bit count became a parameter | - |
| Placer performance | Reference per-module placer first (Phase 5.5), then the benchmark-driven fast path (follow-up, 2026-08-16): per-version tables built once by the reference painters (painted template per version × ECC, zigzag order as core indices, mask per position, column-pair segmentation), vector bit expansion fused with the mask, 16-bit pair stores + index scatter; the reference stays the source of truth (tables, decoder predicate) and the parity test pins both. ARM64 gained a second store tier (2026-08-20, `RmQRModulePlacer.Simd.Arm.cs`): eight consecutive columns are transposed in registers so one symbol row is one 8-byte store instead of one 16-bit store per two modules, and the leftovers are segmented by row RUN rather than by whole column pair — the pair test disqualified 56 % of R11x27's modules although 91.8 % of those sit in stretches where both columns are ordinary data, leaving only 4-12 % genuinely isolated. Encode E2E improved an honest 15-43 % after accounting for a -4.5 % control drift. The portable expand became branch-free SWAR in the same round, which is what netstandard2.0/2.1 and non-SIMD targets run for the whole message | Placement stops being about half of the encode pipeline, or a profile names the template copy (33 ns of 287 ns at R17x139) |
| Bit-stream performance | Reference shape first (Phase 5.3), then the benchmark-driven fast path (follow-up, 2026-08-16): raw-local writer, SWAR / SSE numeric and alphanumeric value kernels, SSE2 byte narrowing, capability-gated with scalar fallbacks; kernel-level parity tests pin vector vs scalar, the naive-reference parity pins the stream. Latin-1 gained a portable `Vector128.Narrow` tier (2026-08-20) for targets with 128-bit vectors and no SSE2 (ARM64 NEON, WASM), 16 characters per iteration: 9.2x on the writer and -11 % on byte-mode encode E2E. What was slow was the writer-state update rate, not character decoding — every gain in that round came from making one append cover more characters | The numeric and alphanumeric writers were measured and DECLINED, so their missing ARM tiers are a decision rather than an oversight: post-placement shares are byte 10.4 %, Latin-1 ECI 8.5 %, alphanumeric 5.4 %, numeric **0.3 %**. Alphanumeric batching won 19-28 % in isolation and measured worse end to end (331.6/304.7 → 339.9/348.2 ns) because production keeps all three writers in one `switch`, so enlarging one arm changes the whole method's codegen; it becomes available for free if encode is ever restructured so each mode compiles independently. Numeric is -9 % at 361 digits but +6 % at 12, the only numeric payload in the E2E set |

## Verification record

Performed 2026-08-15 with the pinned qrtool 0.13.2 binary (`--variant rmqr`, `--type ascii` module-exact output; second encoder lineage per the [fixture record](qrcode-test-fixtures.md)), before any implementation existed. Each item is now a permanent test (Phase 5.1b): the structural rows in `RmQRConstantsUnitTest`, the oracle rows in `RmQRConstantsOracleTest` over the committed corpus (both lineages, all 32 versions × M/H; 96 single-character libzint symbols for the count widths).

| Fact | How verified | Result |
|---|---|---|
| Dimensions and version index order | ASCII output size for all 32 `-v H W` combinations | 32/32 |
| Data capacities (N/A/B × M/H) | Binary search of the longest accepted payload per version × ECC × forced mode | 192/192 match the table above (also matches the published Denso capacities) |
| Data codewords per version × ECC | Reproduce all 192 capacities from data codewords + count widths | 192/192 |
| Total codewords | Free-module count from an independent function-pattern painter must equal 8 × total + remainder (0..7) | 32/32 after correcting R17x59 (88, not the initially recalled 90; 90 would also require 31 ECC per block at H, above the 30 maximum) |
| Count indicator widths (N/A/B) | Read the first data codewords of one-character payloads from oracle matrices (inverse zigzag + unmask + deinterleave), width = position of the count's leading 1 | 96/96 (three numeric widths of the initial recall were off by one and corrected) |
| Count indicator widths (Kanji) | No oracle here emits Kanji, so the column is pinned by derivation: Table 3 takes the narrowest count field that still expresses the largest count the version's M-level data capacity allows. The rule is validated by reproducing all 96 measured N/A/B widths above, then applied to Kanji | 32/32 after correcting R17x99 (6, not the transcribed 7) |
| Format information | Both 18-bit copies of all 64 version × ECC symbols equal BCH(18,6) of (ECC bit, version index) XOR the copy's mask | 128/128 |
| Mask, zigzag start and direction, interleaving | The R7x43-M "1" symbol yields exactly the predicted codewords `22 20 EC 11` and multi-block versions deinterleave to the predicted streams | Confirmed |
| Alignment column positions, sub-finder and corner patterns | Visual inspection of R7x43 / R9x59 / R11x27 plus the free-module count agreement above | Consistent |

Not verified here: the ISO/IEC 23941 misdecode-protection question (whether ECC counts reserve codewords beyond
the correction capacity). The decoder resolves it indirectly — the block structure verified
above leaves at most one unused ECC codeword per block, and zxing-cpp corrects rMQR at full
Reed-Solomon strength — but the Table 8 capacity column itself is still unread; see the Correction
cap decision in [rMQR Decoder](rmqr-decoder.md).

## Lessons Learned

Pre-implementation (from the verification itself):

- Published capacity tables cannot pin count-indicator widths: byte alignment slack lets several widths reproduce the same capacity. Reading the width directly from an oracle's bit stream does pin it, and doing so also validates the mask, the zigzag start, and the interleaving order in one step.
- A recalled table can be internally consistent and still wrong: the R17x59 total-codeword error passed the "ECC divisible by blocks" check and was only caught by the geometric free-module count. Structural invariants that connect independent tables (geometry ↔ codewords ↔ ECC bounds) are the transcription guard, not per-table plausibility.
- On multi-block versions the leading data bytes in placement order are interleaved; any "read the first bits" check must deinterleave first or it silently reads block-2 codewords.

Implementation lessons, consolidated from the retired phase-by-phase progress log.

- Placement, ARM64 store tier (2026-08-20). Measured against the shipped portable path on Apple M2: 0.29x at R17x139, 0.34x at R13x99, 0.50x at R11x59, 0.56x at R7x43, 0.68x at R11x27 — it wins every size, so no size switch is needed. Three designs were measured and rejected. **Generalized blocks** ("four adjacent pairs × their common data row range") raised in-block coverage to 89 % and lost at every size: the ZIP network costs a fixed ~40 instructions per block whatever its height, so short blocks pay a tall block's price and take modules away from the cheaper run path; raising the minimum block height to 8 rows recovered a tie at the largest symbol and still lost everywhere else. **Branch-free split lists** (per-version clean-up / clean-down / irregular lists, removing both per-pair branches) lost at R17x139 because splitting the pairs out of walk order destroys the bit array's read locality, and at 1,858 bytes that costs more than the branches saved — branch count was not the right objective. A **wider expand step** (4 message bytes per iteration) lost via `CreateScalarUnsafe`, where the GPR→SIMD FMOV is paid every iteration and LD1R replicates straight from memory instead, and merely tied via two independent LD1R broadcasts, because the expand is not load-bound.

- Bit-stream fast path (follow-up, 2026-08-16): a memory-backed writer's cost is the per-flush range check, not the struct; a fixed-lane horizontal instruction (`pmaddwd`) cannot express a 3-digit group that straddles its lane pairs, so the group must own the load; at 10-50 ns per encode, code-layout noise (±30 % on identical code) is the measurement floor, and only same-run, same-mode deltas above it count.

## Validation

Per phase (every exit met): structural table tests + oracle format/dimension tests (5.1), naive-reference parity for the bit stream (5.3), interleave reference (5.4), extraction test over all 64 combinations (5.5), the `spot-check-rmqr` zxing-cpp gate over every version × ECC × mode (5.6), module-to-pixel rendering parity (5.7); Standard and Micro QR benchmarks flat at every step. The 2026-08-18 ECI follow-up adds exact assignment-3/26 streams, ECI capacity-boundary and exhaustive selector parity tests, public class/span/sizing round trips, unsupported-charset validation, and a 318-symbol zxing-cpp text/bytes/version/ECC gate with 63 ECI-3 and 63 ECI-26 symbols.
- A table column that no oracle can reach still has a check available: derive it. The Kanji count widths sat in the tables for months marked "spec-transcribed, unverified" because no oracle in this repo emits Kanji — but Table 3's own rule (narrowest count field that still holds the largest count the version's data capacity allows) reproduces all 96 measured Numeric/Alphanumeric/Byte widths exactly, which validates the rule on evidence and then applies it to the column that has none. It found R17x99 transcribed as 7 where the rule gives 6, latent since Phase 5.1b and invisible to every test because the mode is unimplemented. The bound tests that did cover the column (`b >= k >= 2`) passed on the wrong value; a loose invariant on an unverified column is not coverage.
