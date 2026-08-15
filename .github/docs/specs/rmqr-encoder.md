# rMQR Encoder

Design record for the rMQR Code (ISO/IEC 23941) encode feature (`RmQRCodeGenerator`, planned): what it will do, the symbol parameters it is built on, why the pipeline is structured this way, and the decisions made up front so implementation phases share one understanding. Normative details and implementation locations are indexed in the [spec-to-code map](rmqr-spec-map.md); the implementation order is the [rMQR implementation plan](../plans/skiasharp-qrcode-rmqr-implementation-plan.md). The decoder design record (`rmqr-decoder.md`) is written when Phase 6 lands.

Status: **in progress, spec-first**. Written 2026-08-15 before any `src/` code existed; the tables now live in `Internals/RmQr/RmQRConstants` (Phase 5.1b) and every parameter below is pinned by `RmQRConstantsUnitTest` (structural invariants) and `RmQRConstantsOracleTest` (the committed two-lineage corpus), see the [Verification record](#verification-record). Sections that can only be filled by implementing (measured performance, lessons learned) are marked as such.

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
bool TryDecode(ReadOnlySpan<byte> modules, int width, int height, out string text, out RmQRCodeDecodeInfo info);                          // byte per module, any uniform quiet zone
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
  RmQRCodeImageBuilder WithFitStrategy(RmQRFitStrategy fitStrategy);  WithHeight(RmQRHeight height);   // rMQR-only, listed in the parity test's allowed differences
  // static helpers exactly as MicroQRCodeImageBuilder (GetPngBytes / GetImageBytes / SavePng / GetSvgBytes / SaveSvg / GetSvgString / WriteSvg / WritePng / WriteImage,
  // string + RmQREccLevel eccLevel = RmQREccLevel.M and RmQRCodeData overloads); their `int size = 512` is the image WIDTH, height follows the symbol aspect ratio
QRCodeRenderer.Render(SKCanvas canvas, SKRect area, RmQRCodeData data, SKColor? codeColor, SKColor? backgroundColor, ModuleShape? moduleShape = null, float moduleSizePercent = 1.0f, GradientOptions? gradientOptions = null);
SKCanvas.Render(this SKCanvas canvas, RmQRCodeData data, int width, int height, SKColor? clearColor = null, SKColor? codeColor = null, SKColor? backgroundColor = null, ModuleShape? moduleShape = null, float moduleSizePercent = 1.0f, GradientOptions? gradientOptions = null);
SKCanvas.Render(this SKCanvas canvas, RmQRCodeData data, SKRect area, …same tail…);
```

Rectangular geometry rule shared by every rendering entry: the symbol (quiet zone included) is drawn with a uniform module scale, centered in the target area or canvas (letterbox); `WithModulePixelSize` yields exactly `Width × Height` modules × pixels; `WithSize(w, h)` letterboxes into `w × h`. Standard and Micro QR rendering is unchanged.

### Supported (planned scope)

| Area | Coverage |
|---|---|
| Symbology | rMQR |
| Versions | All 32 (R7x43 … R17x139) |
| ECC levels | M, H |
| Data modes | Numeric, Alphanumeric, Byte (UTF-8 bytes for non-Latin-1 text, same fallback as Micro QR) |
| Version selection | Exact version, or automatic fit by strategy, optionally within a fixed height |
| Quiet zone | Configurable non-negative size, default 2 (the ISO/IEC 23941 quiet zone) |
| Output | Bit-packed `RmQRCodeData` or byte-per-module `Span<byte>` |

### Not implemented (planned scope)

- Kanji mode (tables keep the column; shared scope decision in [QR Symbology Architecture](qrcode-symbologies.md))
- ECI header emission (the decoder parses ECI segments; encoding always uses Byte mode without an ECI header, matching Micro QR)
- FNC1, Structured Append (rMQR does not define Structured Append)
- Multi-segment optimization within one payload (single-mode segment, as for Standard and Micro QR)

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

Kanji count-indicator widths are not in this table: they cannot be verified with the available oracle command lines and the mode is deferred; `RmQRConstants.GetKanjiCountIndicatorLength` carries them spec-transcribed with an "unverified" comment (values 2-7, monotone below the byte widths).

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

Shared `TextAnalyzer` (Numeric / Alphanumeric / Byte, single segment). Non-Latin-1 text is encoded as UTF-8 bytes in Byte mode with no ECI header (Micro QR precedent; decoders apply the UTF-8 heuristic in shared `SegmentDecoders`).

### 3. Fit the version

Required bits = 3 (mode) + count indicator (per version, table above) + payload bits. The terminator may shrink to the remaining capacity, including zero bits.

- `requestedVersion` given: use it or fail with an actionable capacity error (actual length, applicable maximum in mode units, remedy: shorten, lower ECC, choose a larger version, or use Standard QR).
- Otherwise the candidate set is all 32 versions, or the versions of the constrained `height`; keep those whose data-codeword capacity holds the required bits; choose by `fitStrategy`:
  - `MinimizeArea`: fewest modules (height × width); ties toward the smaller height (i.e. the wider symbol).
  - `MinimizeWidth`: smallest width; ties toward the smaller height.
  - `MinimizeHeight`: smallest height; ties toward the smaller width.
- No candidate fits: capacity error stating the maximum for the most capacious candidate in the set.

### 4. Build the data codewords

3-bit mode indicator, count indicator, payload bits, terminator `000` (shortened at capacity), zero bits to a byte boundary, alternating 0xEC / 0x11 pads to the data-codeword count.

### 5. Reed-Solomon per block, 6. interleave

Blocks per the table (smaller data blocks first, sizes differ by at most one), ECC per block via shared `EccBinaryEncoder`, then Standard-QR-style interleaving: all data codewords column-wise across blocks, then all ECC codewords. Remainder bits (free modules − 8 × total codewords, 0..7 per version) are light.

### 7. Place function patterns and data

Finder (7×7 with separators), sub-finder (5×5), four edge timing patterns, the two corner patterns, vertical timing columns with 3×3 alignment patterns at both ends, and both format regions are function modules; data fills the rest in zigzag order. Coordinates live in code comments in Phase 5.5.

### 8. Fixed mask, 9. format information

The single mask is applied to data modules while placing. Both format copies come from a static 64-entry table indexed by (version, ECC).

---

## Rendering

`RmQRCodeImageBuilder` derives from `QRCodeImageBuilderBase<TSelf>` and adds `WithErrorCorrection(RmQREccLevel)`, `WithVersion(RmQRVersion)`, `WithFitStrategy(RmQRFitStrategy)`, `WithHeight(RmQRHeight)`; quiet zone default 2; no icon overlay or finder styling (one finder, no ECC headroom to spend). Canvas layout is rectangular: with a module pixel size the content is `width × height` modules at that size; with only an explicit canvas size the symbol is fitted with a uniform module scale and centered on whole pixels (letterbox), never stretched non-uniformly. Standard and Micro QR layout is unchanged.

---

## Why

- Separate `RmQR*` entry points, not `CreateQrCode` overloads: version, ECC and fit semantics differ per symbology; see [QR Symbology Architecture](qrcode-symbologies.md).
- Two-dimensional fit exposed as strategy + optional height constraint: rMQR exists to fit narrow print lanes; "fixed height, auto width" is the dominant real-world request (libzint's `R<h>xauto`), and area/width/height minimization covers the rest without a free-form size search that would mostly select non-existent sizes.
- Letterbox instead of stretch for explicit canvas sizes: a rectangular symbol drawn into an arbitrary rectangle at non-uniform scale is not the same symbol; module aspect ratio must survive.
- Fixed mask means the placer is a static permutation per version; no mask scoring machinery is designed in.
- Byte-mode UTF-8 without ECI keeps encoder and decoder symmetric with Micro QR and avoids exposing an option whose interoperability we cannot verify with the available oracles.

## Decisions

| Decision | Choice | Revisit when |
|---|---|---|
| Naming | `RmQR*` family, `RmQRVersion` with 32 named members | Never (mirrors shipped `MicroQR*`) |
| Version fit API | `RmQRFitStrategy` + `RmQRHeight?` | User demand for width constraints (would add `RmQRWidth?` symmetric to height) |
| Explicit-canvas layout | Uniform scale, centered (letterbox) | - |
| ECI on encode | Not emitted | Interop demand; decoder parses ECI regardless |
| Kanji | Deferred (tables keep the column) | Cross-symbology decision |
| Interleaver | Lift `BinaryInterleaver` to shared if it has no Standard-QR-only assumptions, else `RmQRInterleaver` | Read at Phase 5.4 |
| Placer performance | Reference per-module placer first; fused/SIMD fast path as a benchmark-driven follow-up | After Phase 7 |

## Verification record

Performed 2026-08-15 with the pinned qrtool 0.13.2 binary (`--variant rmqr`, `--type ascii` module-exact output; second encoder lineage per the [fixture record](qrcode-test-fixtures.md)), before any implementation existed. Each item is now a permanent test (Phase 5.1b): the structural rows in `RmQRConstantsUnitTest`, the oracle rows in `RmQRConstantsOracleTest` over the committed corpus (both lineages, all 32 versions × M/H; 96 single-character libzint symbols for the count widths).

| Fact | How verified | Result |
|---|---|---|
| Dimensions and version index order | ASCII output size for all 32 `-v H W` combinations | 32/32 |
| Data capacities (N/A/B × M/H) | Binary search of the longest accepted payload per version × ECC × forced mode | 192/192 match the table above (also matches the published Denso capacities) |
| Data codewords per version × ECC | Reproduce all 192 capacities from data codewords + count widths | 192/192 |
| Total codewords | Free-module count from an independent function-pattern painter must equal 8 × total + remainder (0..7) | 32/32 after correcting R17x59 (88, not the initially recalled 90; 90 would also require 31 ECC per block at H, above the 30 maximum) |
| Count indicator widths (N/A/B) | Read the first data codewords of one-character payloads from oracle matrices (inverse zigzag + unmask + deinterleave), width = position of the count's leading 1 | 96/96 (three numeric widths of the initial recall were off by one and corrected) |
| Format information | Both 18-bit copies of all 64 version × ECC symbols equal BCH(18,6) of (ECC bit, version index) XOR the copy's mask | 128/128 |
| Mask, zigzag start and direction, interleaving | The R7x43-M "1" symbol yields exactly the predicted codewords `22 20 EC 11` and multi-block versions deinterleave to the predicted streams | Confirmed |
| Alignment column positions, sub-finder and corner patterns | Visual inspection of R7x43 / R9x59 / R11x27 plus the free-module count agreement above | Consistent |

Not verified here: Kanji count widths, and the ISO/IEC 23941 misdecode-protection question (whether ECC counts reserve codewords beyond the correction capacity), both scheduled for the phases that need them.

## Lessons Learned

Pre-implementation (from the verification itself):

- Published capacity tables cannot pin count-indicator widths: byte alignment slack lets several widths reproduce the same capacity. Reading the width directly from an oracle's bit stream does pin it, and doing so also validates the mask, the zigzag start, and the interleaving order in one step.
- A recalled table can be internally consistent and still wrong: the R17x59 total-codeword error passed the "ECC divisible by blocks" check and was only caught by the geometric free-module count. Structural invariants that connect independent tables (geometry ↔ codewords ↔ ECC bounds) are the transcription guard, not per-table plausibility.
- On multi-block versions the leading data bytes in placement order are interleaved; any "read the first bits" check must deinterleave first or it silently reads block-2 codewords.

Implementation lessons: appended per phase (Phase 5 progress log in the [implementation plan](../plans/skiasharp-qrcode-rmqr-implementation-plan.md), then consolidated here).

## Validation

Planned, per phase (see the implementation plan): structural table tests + oracle format/dimension tests (5.1), naive-reference parity for the bit stream (5.3), interleave reference (5.4), extraction test over all 64 combinations (5.5), the `spot-check-rmqr` zxing-cpp gate over every version × ECC × mode (5.6), module-to-pixel rendering parity (5.7); Standard and Micro QR benchmarks flat at every step.
