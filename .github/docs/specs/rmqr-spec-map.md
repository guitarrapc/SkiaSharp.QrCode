# rMQR Spec-to-Code Map (ISO/IEC 23941)

An index of where each part of the rMQR Code symbology specification (ISO/IEC 23941, versions R7x43-R17x139) is, or will be, implemented in this library. See [QR Symbology Architecture](qrcode-symbologies.md) for the document set and the shared/per-symbology component split, and [rMQR Encoder](rmqr-encoder.md) for the design record (public API, symbol parameter tables, decisions).

This document is intentionally a **map, not a spec copy**. The normative details, bit layouts, formulas, edge-case constraints, and the reasoning behind implementation choices, live in code comments next to the implementation, where they stay in sync with the code.

Status: **planned**. Implementation entries below name the components the [rMQR implementation plan](../plans/skiasharp-qrcode-rmqr-implementation-plan.md) will create; each entry becomes a link when the phase lands (Phase 5: encoder + rendering, Phase 6: matrix decoder, Phase 7: image detection). Rows whose parameters were already verified against external oracles before implementation say so; the verification record is in the design record.

## Encoding Pipeline Overview

```
Text ──> Mode analysis ──> Version fit ──> Data encoding ──> ECC per block ──> Interleave ──> Module placement ──> Fixed mask ──> Format info (×2)
```

rMQR has exactly one data mask pattern, so there is no mask evaluation or selection stage; the placer is a fixed permutation per version. Codewords are block-interleaved as in Standard QR (Micro QR has no interleaving stage).

## Decoding Pipeline Overview (matrix level)

```
Matrix ──> Format info (2 copies) ──> Version + ECC (dimensions cross-check) ──> Unmask + extract ──> Deinterleave ──> RS per block ──> Bitstream ──> Text
```

Same internal boundary as the Standard QR `QRMatrixDecoder` and `MicroQRMatrixDecoder`. Unlike both, the format information carries the version index, so the decoder learns the symbol dimensions from the format information and only cross-checks them against the physical matrix. Public entry (planned): `RmQRCodeDecoder` (`RmQRCodeData` / module-matrix with width and height / zero-allocation span overloads), diagnostics in `RmQRCodeDecodeInfo`.

| Spec reference | Topic | Implementation |
|---|---|---|
| - | Matrix → payload orchestration (version from format info, dimensions cross-check, deinterleave, per-block RS) | `Internals/RmQr/RmQRMatrixDecoder` (Phase 6) |
| Section 7.9 | Format information decode: two 18-bit copies (finder side, sub-finder side, distinct XOR masks), each matched against the 64 valid words, closer valid copy wins; version must agree with the physical width × height | `Internals/RmQr/RmQRFormatInformationDecoder` (Phase 6) |
| Section 7.7 | Inverse zigzag codeword extraction with on-the-fly unmasking, reusing the encoder's own function-module predicate and mask so both sides always agree | `RmQRMatrixDecoder.ExtractCodewords` (Phase 6) |
| Table 3 / Section 7.4 | Bitstream decode: 3-bit mode indicators, per-version count indicator widths, terminator `000`, ECI segments parsed, Kanji reported as `UnsupportedContent` | `Internals/RmQr/RmQRBinaryDecoder` (Phase 6) |
| Section 7.4.3-7.4.5 | Segment payload decoding (numeric 10/7/4-bit groups, alphanumeric 11/6, byte with UTF-8/Latin-1 heuristic) | [SegmentDecoders](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/SegmentDecoders.cs), shared across symbologies |
| Section 7.5 / 8 | Reed-Solomon correction per block, block deinterleaving | [EccBinaryDecoder](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/EccBinaryDecoder.cs), shared across symbologies; deinterleave in `RmQRMatrixDecoder` |
| Table 8 | Misdecode-protection: whether ECC codeword counts reserve codewords beyond the correction capacity t (as ISO/IEC 18004 Table 9 does for Micro QR) is confirmed against the spec text in Phase 6.3; if so, the post-correction cap mirrors `MicroQRMatrixDecoder` | `RmQRConstants.GetErrorCorrectionCapacity` (Phase 6, conditional) |

Reference tests (planned, Phase 6): `RmQRFormatInformationDecoderUnitTest` (exhaustive 18-bit space vs a naive nearest-candidate reference, copy selection, dimension contradiction), `RmQRBinaryDecoderUnitTest` (golden vectors, malformed streams), `RmQRCodeDecoderRoundTripTest` (all 32 versions × M/H × modes × quiet zones, span parity), `RmQRCodeDecoderRobustnessTest` (per-block damage classes, format copies damaged singly and both, cross-symbology rejection), `RmQrFixtureTest` (committed external-encoder corpus, two lineages).

## Text Analysis and Encoding Modes

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.4.1 | Mode detection (Numeric / Alphanumeric / Byte) | [TextAnalyzer.Analyze](../../../src/SkiaSharp.QrCode/Internals/TextAnalyzer.cs), shared across symbologies |
| Table 2 | Mode indicators, 3 bits for every mode (Numeric, Alphanumeric, Byte, Kanji, ECI), terminator `000` | `RmQRConstants` (Phase 5.1) |
| Table 3 | Character count indicator widths per version and mode (verified: 96/96 numeric/alphanumeric/byte widths read from oracle bit streams) | `RmQRConstants.GetCountIndicatorLength` (Phase 5.1) |
| Section 7.4.3-7.4.5 | Numeric / Alphanumeric / Byte segment bit streams | `Internals/RmQr/RmQRBinaryEncoder` (Phase 5.3) |
| Section 7.4.9 | Terminator (shortened at capacity), byte alignment, pad codewords 0xEC/0x11 | `RmQRBinaryEncoder.EncodeDataCodewords` (Phase 5.3) |
| Section 7.4.2 / 7.4.6 | ECI: parsed on decode; not emitted on encode in this plan (Byte mode carries UTF-8) | decision in [rMQR Encoder](rmqr-encoder.md) |
| Section 7.4.7 | Kanji mode: not implemented (tables keep the column) | scope decision in [QR Symbology Architecture](qrcode-symbologies.md) |

Reference tests (planned, Phase 5.3): `RmQRBinaryEncoderUnitTest` (golden vectors incl. the R7x43-M "1" stream `22 20 EC 11 EC 11` read from an oracle symbol, naive bit-string references), `RmQRBinaryEncoderParityTest` (encoder vs an independent naive reference across all 64 version/ECC combinations, every mode and length up to capacity, UTF-8 fallbacks).

## Capacity and Symbol Tables

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 1 / 6 | The 32 versions: heights 7/9/11/13/15/17 × widths 27/43/59/77/99/139 (27 only with 11 and 13); version index 0-31 in height-major order (verified: dimensions of all 32 oracle symbols) | `RmQRConstants.Versions` (Phase 5.1) |
| Table 8 | Total codewords, data codewords and RS block structure per version × ECC (M/H) (verified: total codewords by free-module count for all 32 versions; data codewords by 192/192 capacity agreement with an oracle encoder) | `RmQRConstants` (`ECCInfo` per version × ECC) (Phase 5.1) |
| Table 7 | Data capacity per version × ECC × mode (verified 192/192 numeric/alphanumeric/byte against qrtool) | `RmQRConstants.GetMaxDataLength` (Phase 5.3, also error-message path) |
| - | Vertical timing / alignment column positions per width (verified indirectly: free-module count matches total codewords × 8 + remainder for all 32 versions) | `RmQRConstants.AlignmentColumns` (Phase 5.1) |
| - | Version fit: exact version, `RmQRFitStrategy` (MinimizeArea / MinimizeWidth / MinimizeHeight), `RmQRHeight` constraint (fixed height, auto width) | `RmQRCodeGenerator.PrepareConfiguration` (Phase 5.3/5.6), semantics in [rMQR Encoder](rmqr-encoder.md) |

Reference tests (planned, Phase 5.1): `RmQRConstantsUnitTest` (structural invariants: 32 entries with the exact dimension set, data + ECC = total for all 64 combos, total × 8 = free-module count from an independent painter, ECC per block even and in 7..30; oracle test: dimensions + both format copies of every libzint and qrtool symbol decode to the expected version/ECC), `RmQRCodeGeneratorUnitTest` (capacity boundaries per mode × ECC, fit-strategy tables, illegal combinations, error messages).

## Error Correction (Reed-Solomon)

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.5 | Reed-Solomon over GF(256), 0x11D, per block; ECC codewords per block ∈ {7,8,9,10,12,14,16,18,20,22,24,26,28,30} | [EccBinaryEncoder.CalculateECC](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.cs), shared across symbologies (generator polynomials built and cached on demand) |
| Section 7.6 | Block interleaving, data then ECC, blocks differ by at most one data codeword (verified: oracle bit streams deinterleave with the Standard QR rule) | `BinaryInterleaver` lifted to `Internals.BinaryEncoders` (shared) or `Internals/RmQr/RmQRInterleaver`, decided in Phase 5.4 |

Reference tests (planned, Phase 5.4/5.5): interleaved layout vs a naive reference for every block structure; `RmQRMatrixExtractionTest` (inverse zigzag + unmask + deinterleave + ECC recomputation, all 64 version/ECC combinations).

## Module Placement

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 6.3 | Function patterns: 7×7 finder with separators (top-left), 5×5 sub-finder (bottom-right), timing patterns on all four edges, corner patterns, vertical timing columns with 3×3 alignment patterns at their top and bottom ends | `Internals/RmQr/RmQRModulePlacer.PlaceFunctionModules` (Phase 5.5) |
| - | Function region predicate (shared by placer, extraction test, and decoder) | `RmQRModulePlacer.IsFunctionModule` (Phase 5.5) |
| Section 7.7 | Two-column zigzag data placement, starting at the column pair left of the right timing column, upward first, right column first (verified against oracle bit streams) | `RmQRModulePlacer.PlaceDataCodewords` (Phase 5.5) |
| - | Fused production pipeline (follow-up after Phase 7, benchmark-driven; rows up to 139 modules exceed one ulong, so the Micro QR packed-row kernels do not port directly) | `RmQRModulePlacer.PlaceSymbol` (follow-up) |

Reference tests (planned, Phase 5.5): `RmQRCodeGeneratorUnitTest` matrix-structure invariants (finder / sub-finder / timing / alignment for representative widths), `RmQRMatrixExtractionTest`, later `RmQRModulePlacerParityTest` (fused vs reference).

## Data Masking

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.8 | Single mask pattern `((row ⁄ 2) + (col ⁄ 3)) mod 2 = 0` applied to data modules only; no evaluation, no selection (verified against oracle symbols) | `RmQRModulePlacer.GetMaskBit` (Phase 5.5) |

## Format Information

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.9 | 6 data bits (ECC level bit + 5-bit version index) BCH-extended to 18 bits, two copies each XOR-masked with its own constant (verified: 128/128 copies of all 64 version × ECC oracle symbols) | `RmQRConstants.GetFormatBits` (static 64-entry table, Phase 5.1) |
| Section 7.9.1 | Placement: one copy adjacent to the finder separator, one adjacent to the sub-finder (verified, coordinates in code comments) | `RmQRModulePlacer.PlaceFormat` (Phase 5.5) |

Reference tests (planned, Phase 5.1/5.5): `RmQRConstantsUnitTest` (64 words vs a naive BCH reference, pairwise Hamming distance ≥ BCH minimum), `RmQRCodeGeneratorUnitTest.CreateRmQRCode_FormatInfo_RoundTripsFromMatrix` (both copies).

## Image Rendering

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 6.3.9 | Quiet zone: 2 modules, the builder default | `RmQRCodeImageBuilder.WithQuietZone` (Phase 5.7) |
| - | High-level image builder (PNG/JPEG/WEBP/SVG, fluent options; typed ECC / version / fit-strategy / height options; no icon overlay or finder styling); shared options and output surface from `QRCodeImageBuilderBase<TSelf>` | `RmQRCodeImageBuilder`, base in [QRCodeImageBuilderBase](../../../src/SkiaSharp.QrCode/Image/QRCodeImageBuilderBase.cs) (Phase 5.7) |
| - | Rectangular canvas layout: content rect = width × height modules at the module pixel size; explicit canvas size fits the symbol with a uniform module scale (letterbox), never a non-uniform stretch | [QRImageLayout](../../../src/SkiaSharp.QrCode/Image/QRImageLayout.cs), generalized to width/height (Phase 5.7) |
| - | Low-level canvas rendering through the internal `IModuleMatrixView` struct views, generalized from `Size` to width/height | [QRCodeRenderer](../../../src/SkiaSharp.QrCode/QRCodeRenderer.cs) (`RmQRCodeData` overload), views in [ModuleMatrixView](../../../src/SkiaSharp.QrCode/Internals/ModuleMatrixView.cs) (Phase 5.7) |
| - | SKCanvas extension entry points | [QRCodeExtensions](../../../src/SkiaSharp.QrCode/QRCodeExtensions.cs) (`RmQRCodeData` overloads, Phase 5.7) |

Reference tests (planned, Phase 5.7): `RmQRCodeImageBuilderUnitTest` (module-to-pixel parity for all 32 versions, letterbox layout cases, rectangular SVG viewBox, quiet zone defaults, validation negatives), [QrImageBuilderApiParityTest](../../../tests/SkiaSharp.QrCode.Tests/Rendering/QrImageBuilderApiParityTest.cs) extended to the third builder.

## Image Detection and Sampling

```
Luminance ──> Otsu threshold ──> Finder candidates (shared 1:1:3:1:1 scan)
          ──> Module size from the finder ──> Format info sampled at 4 orientations × transpose ──> Version → dimensions
          ──> Sub-finder confirmation at its expected corner ──> Projective grid sampling (finder + sub-finder corners)
          ──> Matrix decoding (format/RS checks arbitrate) ──> Inverted retry
```

| Spec reference | Topic | Implementation |
|---|---|---|
| - | Detection pipeline orchestration; inverted retry | `Internals/RmQr/RmQRImageDecoder` (Phase 7) |
| - | Binarization (Otsu), finder candidates | [Binarizer](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/Binarizer.cs), [FinderPatternFinder.FindCandidates](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/FinderPatternFinder.cs), shared across symbologies |
| Section 6.3.2 | Sub-finder pattern (5×5) located at the corner predicted by the decoded version; used for orientation confirmation and as the fourth correspondence | `RmQRImageDecoder.TryLocateSubFinder` (Phase 7) |
| - | Projective transform and module-center sampler | [PerspectiveTransform](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/PerspectiveTransform.cs), shared; sampler generalized to width/height |
| - | Public image entry points (SKBitmap / luminance span / zero-allocation destination) | `RmQRCodeDecoder.TryDecode / TryDecodeImage` (Phase 7) |

Supported envelope (to be measured in Phase 7 and stated here): clean renders and mild optical degradation with arbitrary right-angle orientation, mirroring, reflectance reversal, scale, translation, quiet-zone variants, and mild perspective anchored on the finder and sub-finder corners; extreme aspect ratios (R7x139, R11x27) are covered explicitly. `QRCodeDecoder` remains Standard QR-only.

Reference tests (planned, Phase 7): `RmQRCodeDecoderImageTest`, `RmQRCodeDecoderPerspectiveTest`, `RmQrFixtureTest` (PNG corpus through the image path).

## Data Model and Serialization

| Spec reference | Topic | Implementation |
|---|---|---|
| - | Bit-packed rectangular core matrix with virtual quiet zone (spec quiet zone: 2 modules); `Width` / `Height` include the quiet zone | `RmQRCodeData` (Phase 5.2) |
| - | "QRX" serialization container (magic + symbol type 2 + width + height + packed bits), Micro QR (type 1) and QRR streams rejected | `RmQRCodeData.GetRawData` (Phase 5.2) |

Reference tests (planned, Phase 5.2): `RmQRCodeDataUnitTest`.

## Maintenance Notes

- When a planned component lands, replace its name with a link in the same change and drop the "(Phase N)" marker; when adding or moving a spec-referenced implementation, update this map but keep the detailed explanation (bit layouts, coordinates, formulas) in the code comment next to the implementation, not here.
- The pre-implementation oracle verification (what was checked, how, and the corrections it forced) is recorded in [rMQR Encoder](rmqr-encoder.md); Phase 5.1 turns those checks into permanent tests.
- External-encoder fixtures and the oracle matrix are tracked in the [fixture record](qrcode-test-fixtures.md).
- Components marked "shared across symbologies" live outside `Internals/RmQr`; the split is defined in [QR Symbology Architecture](qrcode-symbologies.md).
