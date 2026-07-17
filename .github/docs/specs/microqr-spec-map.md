# Micro QR Spec-to-Code Map (ISO/IEC 18004)

An index of where each part of the Micro QR symbology specification (ISO/IEC 18004, versions M1-M4) is implemented in this library. See [QR Symbology Architecture](qrcode-symbologies.md) for the document set and the shared/per-symbology component split.

This document is intentionally a **map, not a spec copy**. The normative details — bit layouts, formulas, edge-case constraints, and the reasoning behind implementation choices — live in code comments next to the implementation, where they stay in sync with the code.

## Encoding Pipeline Overview

```
Text ──> Mode analysis ──> Data encoding ──> ECC ──> Module placement ──> Masking ──> Format info
```

Micro QR has a single Reed-Solomon block and no codeword interleaving; the interleaving stage of the Standard QR pipeline has no Micro QR counterpart.

## Decoding Pipeline Overview (matrix level)

```
Matrix ──> Version from size ──> Format info ──> Unmask + extract ──> RS correction ──> Bitstream ──> Text
```

Same internal boundary as the Standard QR `QRMatrixDecoder`. Public entry: [MicroQrCodeDecoder](../../../src/SkiaSharp.QrCode/MicroQrCodeDecoder.cs) (`MicroQrCodeData` / module-matrix / zero-allocation span overloads, uniform quiet-zone stripping; image overloads below), diagnostics in [MicroQrCodeDecodeInfo](../../../src/SkiaSharp.QrCode/MicroQrCodeDecodeInfo.cs).

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Matrix → payload orchestration (version from size 11/13/15/17, single RS block, no deinterleaving) | [MicroQrMatrixDecoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrMatrixDecoder.cs) |
| — | Format information decode: single 15-bit copy matched against all 32 valid patterns, ≤ 3 bit errors correctable (BCH(15,5) min distance 7); format version must agree with the physical matrix size | [MicroQrFormatInformationDecoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrFormatInformationDecoder.cs), cross-check in [MicroQrMatrixDecoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrMatrixDecoder.cs) |
| Table 9 | Error correction capacity t (2t + p = ecc codewords; M1 p=2 detection-only, M2-L p=3, M2-M/M3-L/M4-L p=2): the decoder must reject corrections beyond t even where Reed-Solomon could correct more | [MicroQrConstants.GetErrorCorrectionCapacity](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs), enforced in [MicroQrMatrixDecoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrMatrixDecoder.cs) |
| Section 7.7.3 | Inverse zigzag codeword extraction with on-the-fly unmasking (reuses the encoder's own `IsFunctionModule` / `GetMaskBit`, so both sides always agree) | [MicroQrMatrixDecoder.ExtractCodewords](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrMatrixDecoder.cs) |
| Table 2/3 | Bitstream decode: mode indicator (version − 1 bits, M1 implicit Numeric), count indicators, terminator = Numeric mode with zero count (possibly truncated at capacity), stream bounded by the bit capacity (M1/M3 half codeword), Kanji reported as UnsupportedContent | [MicroQrBinaryDecoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrBinaryDecoder.cs) |
| Section 7.4.3-7.4.5 | Segment payload decoding (numeric 10/7/4-bit groups, alphanumeric 11/6, byte with UTF-8/Latin-1 heuristic) — shared with the Standard QR decoder | [SegmentDecoders](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/SegmentDecoders.cs) |
| Section 8.5 | Reed-Solomon correction | [EccBinaryDecoder](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/EccBinaryDecoder.cs) — shared across symbologies |

Reference tests: [MicroQrFormatInformationDecoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrFormatInformationDecoderUnitTest.cs) (exhaustive 15-bit space vs a naive nearest-candidate reference, ISO Table 9 capacities), [MicroQrBinaryDecoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrBinaryDecoderUnitTest.cs) (M1 golden vectors, the ISO "01234567" M2-L example, encoder round-trips, malformed-stream negatives), [MicroQrCodeDecoderRoundTripTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeDecoderRoundTripTest.cs) (all versions × ECC × modes, quiet zones, span parity), [MicroQrCodeDecoderRobustnessTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeDecoderRobustnessTest.cs) (per-equivalence-class damage: within t, the t&lt;errors≤⌊ecc/2⌋ misdecode-protection class, beyond RS range, M1 detection-only, format damage, cross-symbology rejection), [MicroQrFixtureTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrFixtureTest.cs) (committed external-encoder corpus, two lineages).

## Text Analysis and Encoding Modes

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.4.1 | Mode detection (Numeric / Alphanumeric / Byte) | [TextAnalyzer.Analyze](../../../src/SkiaSharp.QrCode/Internals/TextAnalyzer.cs) — shared across symbologies |
| Table 2 | Mode indicator widths (M1: none, M2-M4: version − 1 bits) and values | [MicroQrConstants.GetModeIndicatorLength / GetModeIndicatorValue](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| Table 3 | Character count indicator widths (Numeric = version + 2, Alphanumeric/Byte = version + 1) | [MicroQrConstants.GetCountIndicatorLength](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| Section 7.4.3-7.4.5 | Numeric / Alphanumeric / Byte segment bit streams (128-bit accumulator; byte-mode uses platform-specific SIMD fast paths with a scalar fallback) | [MicroQrBinaryEncoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrBinaryEncoder.cs) |
| Table 2 | Terminator (3/5/7/9 zero bits, shortened at capacity) and pad codewords (0xEC/0x11, final 4-bit pad = 0000) | [MicroQrBinaryEncoder.EncodeDataCodewords](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrBinaryEncoder.cs) |
| — | Mode availability per version (M1: Numeric; M2: +Alphanumeric; M3/M4: +Byte; Kanji not implemented) | [MicroQrConstants.IsModeSupported](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |

Reference tests: [MicroQrBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrBinaryEncoderUnitTest.cs) (M1 golden vectors, the ISO "01234567" M2-L example, naive bit-string references for alphanumeric/byte/half-codeword padding), [MicroQrBinaryEncoderParityTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrBinaryEncoderParityTest.cs) (optimized encoder vs an independent naive reference across all 8 version/ECC combinations, every supported mode and length, full Latin-1 range, UTF-8 fallbacks including surrogate-pair / lone-surrogate handling, and a single non-Latin-1 char probed at every position of every length so each SIMD tier's overlapped-window Latin-1 detector is proven to see it), [MicroQrBitAccumulatorUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrBitAccumulatorUnitTest.cs) (Append / Append64 / AppendWide boundary positions vs an independent bit-string reference).

## Capacity and Symbol Tables

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 7 | Data capacity in bits per version/ECC (M1/M3 end on a 4-bit half codeword) | [MicroQrConstants.dataBitCapacities](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| Table 9 | Data / ECC codeword counts (single block, no interleaving) | [MicroQrConstants.dataCodewordCounts / eccCodewordCounts](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| — | Version/ECC legality (M1: detection only; M2/M3: L, M; M4: L, M, Q) and smallest-version auto-selection | [MicroQrConstants.IsValidCombination](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs), [MicroQrCodeGenerator.PrepareConfiguration](../../../src/SkiaSharp.QrCode/MicroQrCodeGenerator.cs) |

Reference tests: [MicroQrConstantsUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrConstantsUnitTest.cs) (table values), [MicroQrCodeGeneratorUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeGeneratorUnitTest.cs) (capacity boundaries per mode × ECC, illegal-combination rejection).

## Error Correction (Reed-Solomon)

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 8.5 | Reed-Solomon over GF(256), single block | [EccBinaryEncoder.CalculateECC](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.cs) — shared across symbologies (generator polynomials for the Micro QR ECC counts 2/5/6/8/10/14 are built and cached on demand) |
| Section 7.5 | M1/M3 final 4-bit data codeword participates as its high-nibble byte value | [MicroQrBinaryEncoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrBinaryEncoder.cs) (packing), [MicroQrCodeGenerator.WriteCoreModules](../../../src/SkiaSharp.QrCode/MicroQrCodeGenerator.cs) |

Reference tests: [MicroQrMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrMatrixExtractionTest.cs) (ECC recomputation over matrix-extracted codewords, all 8 version/ECC combinations).

## Module Placement

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 6.3 | Single finder pattern, separators, edge timing patterns (row 0 / column 0) | [MicroQrModulePlacer.PlaceFunctionModules](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |
| — | Function region predicate: `row == 0 ‖ col == 0 ‖ (row ≤ 8 ∧ col ≤ 8)` | [MicroQrModulePlacer.IsFunctionModule](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |
| Section 7.7.3 | Two-column zigzag data placement; M1/M3 half codeword emits its high nibble only | [MicroQrModulePlacer.PlaceDataCodewords](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |
| — | Fused production pipeline (function patterns + data + mask + format in one call, packed-row representation for all sizes; four runtime tiers: BMI2+AVX2 with placement as a static per-row PEXT/PDEP permutation — gated on fast-PEXT hardware (Intel or AMD Zen 3+) — SSSE3 and ARM64 NEON sharing the serial placement + 16-module unpack pipeline (only the bit-expand idiom differs: PSHUFB+PAND+PCMPEQB vs TBL+CMTST), and a portable scalar fallback) — the per-module methods above remain as the readable reference | [MicroQrModulePlacer.PlaceSymbol](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.PlaceSymbol.cs) |

Reference tests: [MicroQrCodeGeneratorUnitTest.CreateMicroQrCode_M2_MatrixStructure](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeGeneratorUnitTest.cs) (finder/separator/timing invariants), [MicroQrMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrMatrixExtractionTest.cs) (independent inverse-zigzag extraction), [MicroQrModulePlacerParityTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrModulePlacerParityTest.cs) (fused pipeline vs naive reference: byte-identical matrix and mask across all 8 version/ECC combinations, random/all-zero/all-one streams; every tier is exercised explicitly via the named internal entry points `PlaceSymbolBmi2`, `PlaceSymbolSsse3`, `PlaceSymbolAdvSimd` and `PlaceSymbolScalar`).

## Data Masking

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 10 | The 4 Micro QR mask conditions (Standard QR patterns 1/4/6/7) | [MicroQrModulePlacer.GetMaskBit](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |
| Section 7.8.3 | Edge-based mask evaluation (dark counts of right/lower edges, min·16 + max, highest wins) — evaluated on the two edges only, no trial matrices | [MicroQrModulePlacer.SelectAndApplyMask](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) (reference); the production path scores both edges bit-packed in [MicroQrModulePlacer.PlaceSymbol](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.PlaceSymbol.cs) |

## Format Information

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Symbol number (3 bits from version + ECC) + mask (2 bits), BCH(15,5), XOR mask 0x4445 | [MicroQrConstants.GetFormatBits](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| — | Placement: bits 14…8 along row 8 cols 1-7, bit 7 at (8,8), bits 6…0 down col 8 rows 7-1 | [MicroQrModulePlacer.PlaceFormat](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |

Reference tests: [MicroQrConstantsUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrConstantsUnitTest.cs) (all 32 format patterns against the ISO-derived table plus a naive BCH reference), [MicroQrCodeGeneratorUnitTest.CreateMicroQrCode_FormatInfo_RoundTripsFromMatrix](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeGeneratorUnitTest.cs).

## Image Rendering

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 9.1 | Quiet zone: 2 modules (narrower than Standard QR's 4) — the builder default | [MicroQrCodeImageBuilder.WithQuietZone](../../../src/SkiaSharp.QrCode/Image/MicroQrCodeImageBuilder.cs) |
| — | High-level image builder (PNG/JPEG/WEBP/SVG, fluent options; no icon overlay or finder styling — single finder, no ECC headroom); shared options and the output surface come from the common `QrCodeImageBuilderBase<TSelf>` | [MicroQrCodeImageBuilder](../../../src/SkiaSharp.QrCode/Image/MicroQrCodeImageBuilder.cs), base in [QrCodeImageBuilderBase](../../../src/SkiaSharp.QrCode/Image/QrCodeImageBuilderBase.cs) |
| — | Low-level canvas rendering (module-run merging shared with Standard QR through the internal `IModuleMatrixView` struct views) | [QRCodeRenderer.Render (MicroQrCodeData overload)](../../../src/SkiaSharp.QrCode/QRCodeRenderer.cs), views in [ModuleMatrixView](../../../src/SkiaSharp.QrCode/Internals/ModuleMatrixView.cs) |
| — | SKCanvas extension entry points | [QRCodeExtensions.Render (MicroQrCodeData overloads)](../../../src/SkiaSharp.QrCode/QRCodeExtensions.cs) |
| — | Canvas layout math (explicit size / module pixel size / centering), shared with the Standard QR builder | [QrImageLayout](../../../src/SkiaSharp.QrCode/Image/QrImageLayout.cs) |

Reference tests: [MicroQrCodeImageBuilderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/Rendering/MicroQrCodeImageBuilderUnitTest.cs) (full-matrix module-to-pixel parity for every version × ECC — every module center sampled against `MicroQrCodeData`, stronger than golden hashes — plus quiet zone defaults, layout, SVG structure, validation negatives), [QrImageBuilderApiParityTest](../../../tests/SkiaSharp.QrCode.Tests/Rendering/QrImageBuilderApiParityTest.cs) (the two builders' public surfaces must correspond 1:1 modulo the documented Standard QR-only options).

## Image Detection and Sampling

```
Luminance ──> Otsu threshold ──> Finder candidates (shared 1:1:3:1:1 scan, ALL candidates)
          ──> Module size refinement ──> Grid sampling (sizes M4..M1 × 4 orientations × transpose)
          ──> Matrix decoding (format/RS/capacity checks arbitrate the right grid)
```

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Detection pipeline orchestration; inverted (reflectance-reversed) retry | [MicroQrImageDecoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrImageDecoder.cs) |
| — | Binarization (Otsu) — shared with Standard QR (lifted to `Internals.ImageDecoders` when Micro QR became the second consumer) | [Binarizer.ComputeOtsuThreshold](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/Binarizer.cs) |
| Section 6.3.1 | Finder pattern candidates: the shared 1:1:3:1:1 run scan collecting every cross-checked candidate (Standard QR keeps its best-three selection; lifted to `Internals.ImageDecoders` when Micro QR became the second consumer) | [FinderPatternFinder.FindCandidates](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/FinderPatternFinder.cs) |
| — | Independent horizontal/vertical module sizes from dark-light-dark runs through the single finder center (7-module span per axis) | [MicroQrImageDecoder.RefineModuleSize](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrImageDecoder.cs) |
| — | Public image entry points (SKBitmap / luminance span / zero-allocation destination) | [MicroQrCodeDecoder.TryDecode / TryDecodeImage](../../../src/SkiaSharp.QrCode/MicroQrCodeDecoder.cs) |

Supported envelope (single-finder tier 1): clean screen-rendered or scanned images with 90°/180°/270° rotation, mirroring, reflectance reversal, non-integer uniform or non-uniform scaling, translation and quiet zone variants, plus mild optical degradation (JPEG artifacts, low contrast, additive noise). Small-angle rotation and perspective distortion are **out of scope**: one finder pattern cannot anchor the orientation/homography recovery that three Standard QR finders allow. `QRCodeDecoder` remains Standard QR-only — Micro QR image scanning is its own explicitly-typed entry point, so default Standard QR scanning performance is unaffected.

Reference tests: [MicroQrCodeDecoderImageTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeDecoderImageTest.cs) (clean renders for every version × ECC, uniform/non-uniform scale, rotation/mirror/inversion/translation/quiet-zone variants, deterministic degradation subset per test strategy §7, negative cases both symbology directions), [MicroQrFixtureTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrFixtureTest.cs) (committed external-encoder PNG corpus through the image path).

## Data Model and Serialization

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Bit-packed core matrix with virtual quiet zone (spec quiet zone: 2 modules) | [MicroQrCodeData](../../../src/SkiaSharp.QrCode/MicroQrCodeData.cs) |
| — | "QRX" serialization container (magic + symbol type + width + height + packed bits) | [MicroQrCodeData.GetRawData](../../../src/SkiaSharp.QrCode/MicroQrCodeData.cs) |

Reference tests: [MicroQrCodeDataUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeDataUnitTest.cs).

## Maintenance Notes

- When adding or moving a spec-referenced implementation, update this map — but keep the detailed explanation (bit layouts, formulas, constraints) in the code comment next to the implementation, not here.
- [MicroQrMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrMatrixExtractionTest.cs) remains as an encoder-side consistency guard with its own independent extraction code (it predates the decoder and does not depend on it). External-encoder fixtures and the oracle matrix are tracked in the [fixture record](qrcode-test-fixtures.md).
- Components marked "shared across symbologies" live outside `Internals/MicroQr`; the split is defined in [QR Symbology Architecture](qrcode-symbologies.md).

## Lessons Learned (decoder)

- The Micro QR terminator is structurally a Numeric mode indicator followed by an all-zero count field (mode bits (v−1) + numeric count bits (v+2) = 2v+1 terminator bits) — decoding it as "zero-count Numeric segment ends the stream" needs no special terminator scanning and handles capacity-truncated terminators for free.
- The ECC codeword counts include misdecode-protection codewords p (ISO Table 9): a decoder wired directly to full Reed-Solomon strength would silently correct ⌊ecc/2⌋ errors where the spec allows only t (e.g. 2 vs 1 for M2-L). The capacity cap is enforced after correction and is covered by its own equivalence-class tests.
- Quiet-zone stripping cannot reuse the Standard QR dark-bounding-box trick: Micro QR has a single finder, so the right/bottom edges are data modules with no darkness guarantee. The top-left dark module is the finder corner, and a uniform border gives the core size.
- libzint (via the ZXingCpp wrapper) rejects UTF-8 Micro QR payloads outright ("Invalid UTF-8 in input"), and a Latin-1 payload with diacritics came back from the round-trip transliterated ("naïve café" → "naive cafe") — UTF-8 fixture coverage comes from the qrtool lineage only, and the sanity gate's payload comparison is what catches such silent drift before a fixture is committed.
