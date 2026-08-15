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

Reference tests (planned, Phase 6): `RmQRFormatInformationDecoderUnitTest` (exhaustive 18-bit space vs a naive nearest-candidate reference, copy selection, dimension contradiction), `RmQRBinaryDecoderUnitTest` (golden vectors, malformed streams), `RmQRCodeDecoderRoundTripTest` (all 32 versions × M/H × modes × quiet zones, span parity), `RmQRCodeDecoderRobustnessTest` (per-block damage classes, format copies damaged singly and both, cross-symbology rejection), [RmQrFixtureTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQrFixtureTest.cs) (committed external-encoder corpus, two lineages; the corpus and its shape tests exist since Phase 5.1a, decode assertions land in 6.4).

## Text Analysis and Encoding Modes

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.4.1 | Mode detection (Numeric / Alphanumeric / Byte) | [TextAnalyzer.Analyze](../../../src/SkiaSharp.QrCode/Internals/TextAnalyzer.cs), shared across symbologies |
| Table 2 | Mode indicators, 3 bits for every mode (Numeric, Alphanumeric, Byte, Kanji, ECI), terminator `000` | [RmQRConstants](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRConstants.cs) (`ModeIndicatorLength`, `GetModeIndicatorValue`, `TerminatorLength`) |
| Table 3 | Character count indicator widths per version and mode (verified: 96/96 numeric/alphanumeric/byte widths read from oracle bit streams) | [RmQRConstants.GetCountIndicatorLength](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRConstants.cs) (Kanji column kept as `GetKanjiCountIndicatorLength`, spec-transcribed, unverified) |
| Section 7.4.3-7.4.5 | Numeric / Alphanumeric / Byte segment bit streams | [RmQRBinaryEncoder](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRBinaryEncoder.cs) (shared `BitWriter`, single segment, Latin-1 narrow / UTF-8 transcode without ECI; readable reference shape, fast path is a follow-up) |
| Section 7.4.9 | Terminator (shortened at capacity), byte alignment, pad codewords 0xEC/0x11 | [RmQRBinaryEncoder.EncodeDataCodewords](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRBinaryEncoder.cs) |
| Section 7.4.2 / 7.4.6 | ECI: parsed on decode; not emitted on encode in this plan (Byte mode carries UTF-8) | decision in [rMQR Encoder](rmqr-encoder.md) |
| Section 7.4.7 | Kanji mode: not implemented (tables keep the column) | scope decision in [QR Symbology Architecture](qrcode-symbologies.md) |

Reference tests: [RmQRBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRBinaryEncoderUnitTest.cs) (golden vectors incl. the R7x43-M "1" stream `22 20 EC 11 EC 11`, terminator shortening / alignment / empty text / UTF-8 / Latin-1 references, and the encoder-side oracle: our data codewords equal the codewords deinterleaved out of EVERY committed libzint and qrtool symbol for the same payload / version / ECC / mode), [RmQRBinaryEncoderParityTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRBinaryEncoderParityTest.cs) (encoder vs `RmQRNaiveReference.NaiveDataCodewords`, an independent bit-string reference, across all 64 version/ECC combinations, every mode and length up to capacity with min / max / cyclic / pseudo-random contents, full Latin-1 range, UTF-8 multi-byte and surrogate fallbacks, every terminator/alignment class).

## Capacity and Symbol Tables

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 1 / 6 | The 32 versions: heights 7/9/11/13/15/17 × widths 27/43/59/77/99/139 (27 only with 11 and 13); version index 0-31 in height-major order (verified: dimensions of all 32 oracle symbols) | [RmQRConstants](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRConstants.cs) (`GetHeight` / `GetWidth` / `TryGetVersion`), public [RmQRVersion](../../../src/SkiaSharp.QrCode/RmQRVersion.cs) (value = index + 1) and [RmQREccLevel](../../../src/SkiaSharp.QrCode/RmQREccLevel.cs) (value = format ECC bit) |
| Table 8 | Total codewords, data codewords and RS block structure per version × ECC (M/H) (verified: total codewords by free-module count for all 32 versions; data codewords by 192/192 capacity agreement with an oracle encoder) | [RmQRConstants.GetEccInfo](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRConstants.cs) (shared `ECCInfo`: shorter blocks first, uniform ECC per block), `GetTotalCodewordCount`, `GetRemainderBitCount` |
| Table 7 | Data capacity per version × ECC × mode (verified 192/192 numeric/alphanumeric/byte against qrtool) | [RmQRVersionSelector.GetMaxDataLength / GetRequiredBits / Fits](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRVersionSelector.cs) |
| - | Vertical timing / alignment column positions per width (verified indirectly: free-module count matches total codewords × 8 + remainder for all 32 versions) | [RmQRConstants.GetAlignmentColumns](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRConstants.cs) |
| - | Version fit: exact version, `RmQRFitStrategy` (MinimizeArea / MinimizeWidth / MinimizeHeight), `RmQRHeight` constraint (fixed height, auto width) | [RmQRVersionSelector.Select / IsBetter](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRVersionSelector.cs) (public enums [RmQRFitStrategy](../../../src/SkiaSharp.QrCode/RmQRFitStrategy.cs), [RmQRHeight](../../../src/SkiaSharp.QrCode/RmQRHeight.cs)); the public [RmQRCodeGenerator](../../../src/SkiaSharp.QrCode/RmQRCodeGenerator.cs) delegates to it (default `MinimizeArea`, confirmed against both reference encoders' automatic choice); semantics in [rMQR Encoder](rmqr-encoder.md) |

Reference tests: [RmQRConstantsUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRConstantsUnitTest.cs) (structural invariants: 32 entries with the exact dimension set and inverse lookup, alignment columns inside the symbol, data + ECC = total for all 64 combos with block splits differing by one, ECC per block in 7..30, total × 8 + remainder = free-module count from an independent painter, published N/A/B capacities reproduced from data codewords + count widths, count widths fit their capacities, format words vs naive BCH(18,6) + XOR with pairwise distance ≥ 7 and no cross-copy collision), [RmQRConstantsOracleTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRConstantsOracleTest.cs) (every committed libzint / qrtool symbol: dimensions → version, both format copies equal the table words, walked bit count = 8 × total + remainder; every single-character symbol: leading codewords after naive inverse zigzag + unmask + deinterleave give the expected mode indicator, count-indicator width and payload bits), both built on the naive helpers in [RmQRNaiveReference](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRNaiveReference.cs); [RmQRVersionSelectorUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRVersionSelectorUnitTest.cs) (`GetMaxDataLength` as the exact inverse of `GetRequiredBits` for all 64 × 3, published capacities, hand-derived fit tables per strategy incl. the R11x27-vs-R7x43 area case, height constraint, requested version honored / too long / height agreement, invalid enums, tie-break comparator rows incl. the R7x99 = R9x77 area tie, error-message content); [RmQRCodeGeneratorUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRCodeGeneratorUnitTest.cs) (public API: default-strategy decision, class / span / `GetRequiredBufferSize` agreement for all 64 combos, quiet-zone offset, oracle symbol through the public path, validation and message content, Release-only zero allocation on the span path); external gate: `tools/QRInteropFixtures -- spot-check-rmqr` (256 symbols: 32 versions × M/H × numeric / alphanumeric / byte / UTF-8, all read by zxing-cpp with matching bytes, version and ECC).

## Error Correction (Reed-Solomon)

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.5 | Reed-Solomon over GF(256), 0x11D, per block; ECC codewords per block ∈ {7,8,9,10,12,14,16,18,20,22,24,26,28,30} | [EccBinaryEncoder.CalculateECC](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.cs), shared across symbologies (generator polynomials built and cached on demand), driven per block by [RmQRCodewordEncoder.AssembleFinalMessage](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRCodewordEncoder.cs) (fixed 156-byte ECC scratch, the R17x139-H maximum) |
| Section 7.6 | Block interleaving, data then ECC, blocks differ by at most one data codeword, zero remainder bits (verified: oracle bit streams deinterleave with the Standard QR rule) | [BinaryInterleaver](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/BinaryInterleaver.cs), shared across symbologies (lifted from `Internals.StandardQr` in Phase 5.4: it never used the version, only the `ECCInfo` block structure; the remainder-bit count is now a parameter), called by `RmQRCodewordEncoder` with `RmQRConstants.GetRemainderBitCount` |

Reference tests: [RmQRCodewordEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRCodewordEncoderUnitTest.cs) (final-message size, data deinterleaves back via the naive reference and ECC equals the shared kernel per block for all 64 combos on a dirty buffer, undersized-buffer negatives, and the encoder-side oracle: our final message equals the interleaved stream walked out of every committed libzint symbol byte for byte, and out of every qrtool symbol except the documented qrtool tail defect on the last ECC codeword), [BinaryInterleaverParityTest](../../../tests/SkiaSharp.QrCode.Tests/Shared/BinaryInterleaverParityTest.cs) (shared); [RmQRMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRMatrixExtractionTest.cs) (place → inverse zigzag + unmask + deinterleave + ECC recomputation, all 64 version/ECC combinations).

## Module Placement

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 6.3 | Function patterns: 7×7 finder with separators (top-left), 5×5 sub-finder (bottom-right), timing patterns on all four edges, corner patterns, vertical timing columns with 3×3 alignment patterns at their top and bottom ends | [RmQRModulePlacer.PlaceFunctionModules](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRModulePlacer.cs) (paint order matters once: on height 9 the finder separator row overrides the bottom-left corner cell (7,0) to light, both external lineages agree) |
| - | Function region predicate (shared by placer, extraction test, and decoder) | [RmQRModulePlacer.IsFunctionModule](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRModulePlacer.cs) |
| Section 7.7 | Two-column zigzag data placement, starting at the column pair left of the right timing column, upward first, right column first (verified against oracle bit streams) | [RmQRModulePlacer.PlaceData](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRModulePlacer.cs) |
| - | Fused production pipeline (follow-up after Phase 7, benchmark-driven; rows up to 139 modules exceed one ulong, so the Micro QR packed-row kernels do not port directly) | `RmQRModulePlacer.PlaceSymbol` (follow-up) |

Reference tests: [RmQRModulePlacerUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRModulePlacerUnitTest.cs) (function predicate vs the naive painter on every module of every version, structural invariants of every function pattern for all 32 versions on a dirty buffer, both format copies read back, undersized-buffer negatives, and the module-exact oracle: placing the same payload reproduces every committed libzint symbol module for module and every qrtool symbol except its documented tail column), [RmQRMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRMatrixExtractionTest.cs) (place → naive inverse walk → deinterleave → data + per-block ECC recompute + zero remainder + payload-independent function modules, all 64 version/ECC × {zero, ones, 2 × random}); planned (follow-up): `RmQRModulePlacerParityTest` (fused fast path vs this reference).

## Data Masking

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.8 | Single mask pattern `((row ⁄ 2) + (col ⁄ 3)) mod 2 = 0` applied to data modules only; no evaluation, no selection (verified against oracle symbols) | [RmQRModulePlacer.GetMaskBit](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRModulePlacer.cs) |

## Format Information

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.9 | 6 data bits (ECC level bit + 5-bit version index) BCH-extended to 18 bits, two copies each XOR-masked with its own constant (verified: 128/128 copies of all 64 version × ECC oracle symbols) | [RmQRConstants.GetFormatBits](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRConstants.cs) (BCH(18,6) computed per call, generator 0x1F25, per-copy XOR constants) |
| Section 7.9.1 | Placement: one copy adjacent to the finder separator, one adjacent to the sub-finder (verified, coordinates in code comments) | [RmQRModulePlacer.PlaceFormat](../../../src/SkiaSharp.QrCode/Internals/RmQr/RmQRModulePlacer.cs) |

Reference tests: [RmQRConstantsUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRConstantsUnitTest.cs) (64 words vs a naive BCH reference, pairwise Hamming distance ≥ 7, no cross-copy collision), [RmQRConstantsOracleTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRConstantsOracleTest.cs) (both copies of every external symbol), [RmQRModulePlacerUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRModulePlacerUnitTest.cs) (both copies read back after placement, all versions × ECC).

## Image Rendering

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 6.3.9 | Quiet zone: 2 modules, the builder default | [RmQRCodeImageBuilder](../../../src/SkiaSharp.QrCode/Image/RmQRCodeImageBuilder.cs) (initial builder state, via the shared `WithQuietZone`) |
| - | High-level image builder (PNG/JPEG/WEBP/SVG, fluent options; typed ECC / version / fit-strategy / height options; no icon overlay or finder styling); shared options and output surface from `QRCodeImageBuilderBase<TSelf>` | [RmQRCodeImageBuilder](../../../src/SkiaSharp.QrCode/Image/RmQRCodeImageBuilder.cs), base in [QRCodeImageBuilderBase](../../../src/SkiaSharp.QrCode/Image/QRCodeImageBuilderBase.cs) (hooks: `ResolveSymbol(out width, out height)`, `PreserveAspectRatio`, `GetDefaultCanvasSize`); the static helpers' `size` is the image width, the height follows the symbol aspect ratio |
| - | Rectangular canvas layout: content rect = width × height modules at the module pixel size; explicit canvas size fits the symbol with a uniform module scale (letterbox), never a non-uniform stretch | [QRImageLayout](../../../src/SkiaSharp.QrCode/Image/QRImageLayout.cs), shared, generalized to width/height with a `preserveAspectRatio` flag (Standard / Micro keep the fill behavior; rMQR letterboxes on whole pixels) |
| - | Low-level canvas rendering through the internal `IModuleMatrixView` struct views, generalized from `Size` to width/height | [QRCodeRenderer.Render (RmQRCodeData overload)](../../../src/SkiaSharp.QrCode/QRCodeRenderer.cs) (letterboxes into the area via `GetLetterboxedArea`), views in [ModuleMatrixView](../../../src/SkiaSharp.QrCode/Internals/ModuleMatrixView.cs) (`IModuleMatrixView` now exposes `Width` / `Height` / `CoreWidth` / `CoreHeight`; the shared run-merge and per-module loops iterate width × height) |
| - | SKCanvas extension entry points | [QRCodeExtensions.Render (RmQRCodeData overloads)](../../../src/SkiaSharp.QrCode/QRCodeExtensions.cs) |

Reference tests: [RmQRCodeImageBuilderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/Rendering/RmQRCodeImageBuilderUnitTest.cs) (module-to-pixel parity for all 32 versions × ECC at every module center, colors / circle shape, quiet zone default, default 512-wide canvas with aspect-ratio height, explicit-canvas letterbox in both orientations with uniform module scale, module-pixel + larger canvas centering, too-small canvas, fit / height / version options, data-builder and invalid-value negatives, static helpers (`size` = width), SVG viewBox and crispEdges, low-level renderer + canvas extensions agree and letterbox), [QrImageBuilderApiParityTest](../../../tests/SkiaSharp.QrCode.Tests/Rendering/QrImageBuilderApiParityTest.cs) (three builders 1:1 modulo the documented Standard-only and rMQR-only options).

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

Reference tests (planned, Phase 7): `RmQRCodeDecoderImageTest`, `RmQRCodeDecoderPerspectiveTest`, [RmQrFixtureTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQrFixtureTest.cs) (PNG corpus through the image path).

## Data Model and Serialization

| Spec reference | Topic | Implementation |
|---|---|---|
| - | Bit-packed rectangular core matrix with virtual quiet zone (spec quiet zone: 2 modules); `Width` / `Height` include the quiet zone | [RmQRCodeData](../../../src/SkiaSharp.QrCode/RmQRCodeData.cs) (internal `GetCoreWidth` / `GetCoreHeight` / `GetCoreModule` / `GetCoreData` / `SetCoreData`, byte-per-module row-major over the core width) |
| - | "QRX" serialization container (magic + symbol type 2 + width + height + packed bits), Micro QR (type 1) and QRR streams rejected | [RmQRCodeData.GetRawData](../../../src/SkiaSharp.QrCode/RmQRCodeData.cs) (array and `IBufferWriter<byte>` overloads; padding bits canonicalized on read) |

Reference tests: [RmQRCodeDataUnitTest](../../../tests/SkiaSharp.QrCode.Tests/RmQr/RmQRCodeDataUnitTest.cs) (dimensions and fresh-light state for all 32 versions, `SetCoreData`/`GetCoreData`/indexer/`GetCoreModule` agreement on synthetic cores and corpus symbols, replace-not-merge, size validation, quiet-zone offset indexing and bounds, QRX round trip for every version with an independent quiet zone, header layout, `IBufferWriter` parity, header/type/dimension/truncation negatives incl. transposed sizes and the Micro QR container in both directions, padding-bit canonicalization, Release-only zero-allocation steady state).

## Maintenance Notes

- When a planned component lands, replace its name with a link in the same change and drop the "(Phase N)" marker; when adding or moving a spec-referenced implementation, update this map but keep the detailed explanation (bit layouts, coordinates, formulas) in the code comment next to the implementation, not here.
- The pre-implementation oracle verification (what was checked, how, and the corrections it forced) is recorded in [rMQR Encoder](rmqr-encoder.md); Phase 5.1 turns those checks into permanent tests.
- External-encoder fixtures and the oracle matrix are tracked in the [fixture record](qrcode-test-fixtures.md).
- Components marked "shared across symbologies" live outside `Internals/RmQr`; the split is defined in [QR Symbology Architecture](qrcode-symbologies.md).
