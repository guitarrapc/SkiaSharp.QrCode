# Standard QR Spec-to-Code Map (ISO/IEC 18004)

An index of where each part of the Standard QR symbology specification (ISO/IEC 18004, versions 1-40) is implemented in this library. Micro QR and rMQR get their own maps as they are implemented — see [QR Symbology Architecture](qrcode-symbologies.md) for the document set and shared components.

This document is intentionally a **map, not a spec copy**. The normative details — bit layouts, formulas, edge-case constraints, and the reasoning behind implementation choices — live in code comments next to the implementation, where they stay in sync with the code. Section numbers below follow the citations in the code comments (the codebase references both ISO/IEC 18004:2015 `7.x` numbering and earlier-edition `8.x` numbering).

## Encoding Pipeline Overview

```
Text ──> Mode analysis ──> Data encoding ──> ECC ──> Interleaving ──> Module placement ──> Masking
```

## Text Analysis and Encoding Modes

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.4.1 | Encoding modes (Numeric / Alphanumeric / Byte) | [EncodingMode.cs](../../../src/SkiaSharp.QrCode/Internals/EncodingMode.cs), [TextAnalyzer.Analyze](../../../src/SkiaSharp.QrCode/Internals/TextAnalyzer.cs) — scalar classification plus SIMD tiers (x64 AVX2 / SSE2, ARM AdvSimd) selected at runtime; parity: [TextAnalyzerAdvSimdParityTest](../../../tests/SkiaSharp.QrCode.Tests/Shared/TextAnalyzerAdvSimdParityTest.cs) |
| Section 7.4.3 | Alphanumeric character set (45 characters) and values | [CharacterSets.alphanumericLookup / GetAlphanumericValue](../../../src/SkiaSharp.QrCode/Internals/CharacterSets.cs) — shared across symbologies |
| — | Numeric / Alphanumeric / Byte mode bit stream encoding | [QRBinaryEncoder](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRBinaryEncoder.cs) (`WriteNumericData`, `WriteAlphanumericData`, `WriteByteData`) |
| Section 7.4.4 | Character count indicator widths (version 1-9 / 10-26 / 27-40) | [EncodingModeExtensions.GetCountIndicatorLength](../../../src/SkiaSharp.QrCode/Internals/StandardQr/EncodingModeExtensions.cs) |
| Section 7.4.5 | UTF-8 BOM bytes count toward the character count indicator | [QRCodeGenerator.cs](../../../src/SkiaSharp.QrCode/QRCodeGenerator.cs), [QRBinaryEncoder.GetUtf8Data](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRBinaryEncoder.cs) |
| — | ECI header structure (indicator 4 bits + assignment number) | [EciModeExtensions](../../../src/SkiaSharp.QrCode/EciMode.cs) |

Reference tests: [QRBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/QRBinaryEncoderUnitTest.cs) (includes the canonical "HELLO WORLD" version 1-M example), [QRCodeDecodabilityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/QRCodeDecodabilityTest.cs) (BOM handling).

## Capacity and Symbol Tables

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 7-11 | Data capacity per version / ECC level | [QRCodeConstants.capacityBaseValues](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRCodeConstants.cs) — practical capacities are documented in [Data Capacity Reference](../../../docs/data-capacity.md) |
| Table 9 | ECC characteristics (block counts, codewords per block) | [QRCodeConstants.capacityECCBaseValues](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRCodeConstants.cs) |
| Table 1 | Remainder bits per version | [QRCodeConstants.remainderBits](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRCodeConstants.cs) |
| Annex E | Alignment pattern center coordinates | [QRCodeConstants.alignmentPatternBaseValues](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRCodeConstants.cs) |

## Error Correction (Reed–Solomon)

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 8.5 | Generator polynomial and polynomial division over GF(2^8) | [EccBinaryEncoder.CalculateECC](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.cs) — scalar kernel plus SIMD variants ([SSSE3 / GFNI](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.Simd.cs), [ARM AdvSimd](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.Simd.Arm.cs)) — shared across symbologies |
| Annex I | Worked encoding example | Used as test vectors in [EccBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/Shared/EccBinaryEncoderUnitTest.cs) |

Reference tests: [EccBinaryEncoderKernelParityTest](../../../tests/SkiaSharp.QrCode.Tests/Shared/EccBinaryEncoderKernelParityTest.cs) — every SIMD kernel is checked against a naive Section 8.5 reference implementation.

## Final Message Construction

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.6 | Data / ECC codeword interleaving across blocks, trailing remainder bits | [BinaryInterleaver.InterleaveCodewords](../../../src/SkiaSharp.QrCode/Internals/StandardQr/BinaryInterleaver.cs) |

Reference tests: [BinaryInterleaverParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/BinaryInterleaverParityTest.cs).

## Module Placement

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Finder patterns, separators, timing patterns, dark module | [ModulePlacer](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.cs) (`PlaceFinderPatterns`, `ReserveSeparatorAreas`, `PlaceTimingPatterns`, `PlaceDarkModule`) |
| Annex E | Alignment pattern placement | [ModulePlacer.PlaceAlignmentPatterns](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.cs) |
| Section 7.7.3 | Zigzag data placement (bottom-right, 2-column strips) | [ModulePlacer.PlaceDataWords](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.cs) |
| Section 7.9 | Format information placement (two redundant copies) | [ModulePlacer.PlaceFormat](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.cs) |
| Section 7.8.2 | Format information bits and mask (BCH + XOR mask) | [QRCodeConstants.GetFormatBits](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRCodeConstants.cs) |
| Section 7.10 | Version information (version 7+, two 3×6 patterns) | [ModulePlacer.PlaceVersion](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.cs), [QRCodeConstants.GetVersionBits](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRCodeConstants.cs) |

Reference tests: [ModulePlacerPlaceDataWordsParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/ModulePlacerPlaceDataWordsParityTest.cs) — bit-parallel placement vs. naive per-module Section 7.7.3 reference.

## Data Masking

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.8.2 | The 8 mask pattern formulas (patterns 0–7) | [ModulePlacer.MaskCode / Pattern0–Pattern7](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.cs) |
| Section 8.8.2 | Penalty scoring rules 1–4 for mask selection | [ModulePlacer.Masking.cs](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.Masking.cs) — bit-parallel implementation of all four rules, plus SIMD variants ([x64 AVX2](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.Masking.Simd.cs), [ARM AdvSimd](../../../src/SkiaSharp.QrCode/Internals/StandardQr/ModulePlacer.Masking.Simd.Arm.cs)) selected at runtime |

Reference tests: [ModulePlacerMaskPackedParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/ModulePlacerMaskPackedParityTest.cs) — packed masking/scoring vs. naive byte-per-module reference formulas; [ModulePlacerMaskSimdParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/ModulePlacerMaskSimdParityTest.cs) / [ModulePlacerMaskAdvSimdParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/ModulePlacerMaskAdvSimdParityTest.cs) — vectorized tiers vs. the scalar bit-packed kernels.

## Decoding Pipeline

```
Image ──> Binarization ──> Finder detection ──> Alignment pattern ──> Perspective sampling ──┐
                                                                                             ├──> Format info ──> Unmask/Extract ──> Deinterleave ──> RS correction ──> Bit stream ──> Text
Module matrix (QRCodeData / span) ───────────────────────────────────────────────────────────┘
```

Design notes (WHAT/WHY and scope tiers) are in [Standard QR Decoder](standardqr-decoder.md).

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.8.2 | Format information decoding (Hamming match against all 32 valid patterns) | [FormatInformationDecoder](../../../src/SkiaSharp.QrCode/Internals/StandardQr/FormatInformationDecoder.cs) |
| Section 7.7.3 | Codeword extraction (inverse zigzag walk + unmasking on the fly) | [QRMatrixDecoder.ExtractCodewords](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRMatrixDecoder.cs) |
| Section 7.6 | Codeword deinterleaving into RS blocks | [QRMatrixDecoder.DeinterleaveCodewords](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRMatrixDecoder.cs) |
| Section 8.5 | Reed-Solomon error correction (syndromes → Berlekamp-Massey → Chien → Forney) | [EccBinaryDecoder.TryCorrect](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/EccBinaryDecoder.cs) — shared across symbologies |
| Section 7.4 | Bit stream decoding (mode segments, character count, ECI) | [QRBinaryDecoder.DecodeBitStream](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRBinaryDecoder.cs); segment payload groups and byte-charset heuristics are shared with the other symbology decoders via [SegmentDecoders](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/SegmentDecoders.cs) |
| Section 6.3.3 | Finder pattern detection (1:1:3:1:1 ratio scan + cross checks) | [FinderPatternFinder](../../../src/SkiaSharp.QrCode/Internals/StandardQr/FinderPatternFinder.cs) — scalar row walk plus a bit-identical SIMD mask walk (AVX2 / ARM NEON / portable 128-bit tiers) selected at runtime; parity: [FinderPatternFinderParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/FinderPatternFinderParityTest.cs) |
| Section 6.3.6 / Annex E | Alignment pattern detection (light-dark-light core + grid-axis border-ring validation) | [AlignmentPatternFinder](../../../src/SkiaSharp.QrCode/Internals/StandardQr/AlignmentPatternFinder.cs) — scalar row walk plus a result-identical SIMD mask walk (AVX2 / ARM NEON / portable 128-bit tiers) selected at runtime; parity: [AlignmentPatternFinderParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/AlignmentPatternFinderParityTest.cs) |
| — | Projective grid sampling (4-point perspective transform) | [PerspectiveTransform](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/PerspectiveTransform.cs) — shared across symbologies, [QRImageDecoder.SampleGrid](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRImageDecoder.cs) — scalar loop plus bit-identical SIMD row kernels (AVX2 / portable 128-bit for ARM NEON and WASM) selected at runtime; parity: [SampleGridParityTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/SampleGridParityTest.cs) |
| — | Binarization (Otsu), module-size measurement, dimension estimation | [QRImageDecoder](../../../src/SkiaSharp.QrCode/Internals/StandardQr/QRImageDecoder.cs) |

Reference tests: [QRCodeDecoderRoundTripTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/QRCodeDecoderRoundTripTest.cs) (encode→decode round-trips, error injection), [EccBinaryDecoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/Shared/EccBinaryDecoderUnitTest.cs) (correction capacity boundaries), [QRCodeDecoderZXingCrossTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/QRCodeDecoderZXingCrossTest.cs) (independent-encoder cross-validation), [QRCodeDecoderImageTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/QRCodeDecoderImageTest.cs) (rotation/mirror/scale/inversion image cases), [QRCodeDecoderPerspectiveTest](../../../tests/SkiaSharp.QrCode.Tests/StandardQr/QRCodeDecoderPerspectiveTest.cs) (keystone envelope, rotation+perspective combinations).

## Maintenance Notes

- When adding or moving a spec-referenced implementation, update this map — but keep the detailed explanation (bit layouts, formulas, constraints) in the code comment next to the implementation, not here.
- Parity tests deliberately keep a *naive* reference implementation of the spec text; they are the executable form of the specification and should stay simple even when the production kernels change.
- Components marked "shared across symbologies" live outside `Internals/StandardQr`; the shared/per-symbology split is defined in [QR Symbology Architecture](qrcode-symbologies.md).
