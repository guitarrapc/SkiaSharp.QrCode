# ISO/IEC 18004 Spec-to-Code Map

An index of where each part of the QR code specification (ISO/IEC 18004) is implemented in this library.

This document is intentionally a **map, not a spec copy**. The normative details — bit layouts, formulas, edge-case constraints, and the reasoning behind implementation choices — live in code comments next to the implementation, where they stay in sync with the code. Section numbers below follow the citations in the code comments (the codebase references both ISO/IEC 18004:2015 `7.x` numbering and earlier-edition `8.x` numbering).

## Encoding Pipeline Overview

```
Text ──> Mode analysis ──> Data encoding ──> ECC ──> Interleaving ──> Module placement ──> Masking
```

## Text Analysis and Encoding Modes

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.4.1 | Encoding modes (Numeric / Alphanumeric / Byte) | [EncodingMode.cs](../../../src/SkiaSharp.QrCode/Internals/EncodingMode.cs), [TextAnalyzer.Analyze](../../../src/SkiaSharp.QrCode/Internals/TextAnalyzer.cs) |
| Section 7.4.3 | Alphanumeric character set (45 characters) and values | [QRCodeConstants.alphanumericLookup / GetAlphanumericValue](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) |
| — | Numeric / Alphanumeric / Byte mode bit stream encoding, character count indicator | [QRBinaryEncoder](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/QRBinaryEncoder.cs) (`WriteNumericData`, `WriteAlphanumericData`, `WriteByteData`) |
| Section 7.4.5 | UTF-8 BOM bytes count toward the character count indicator | [QRCodeGenerator.cs](../../../src/SkiaSharp.QrCode/QRCodeGenerator.cs), [QRBinaryEncoder.GetUtf8Data](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/QRBinaryEncoder.cs) |
| — | ECI header structure (indicator 4 bits + assignment number) | [EciModeExtensions](../../../src/SkiaSharp.QrCode/EciMode.cs) |

Reference tests: [QRBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/QRBinaryEncoderUnitTest.cs) (includes the canonical "HELLO WORLD" version 1-M example), [QRCodeDecodabilityTest](../../../tests/SkiaSharp.QrCode.Tests/QRCodeDecodabilityTest.cs) (BOM handling).

## Capacity and Symbol Tables

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 7-11 | Data capacity per version / ECC level | [QRCodeConstants.capacityBaseValues](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) — practical capacities are documented in [Data Capacity Reference](../../../docs/data-capacity.md) |
| Table 9 | ECC characteristics (block counts, codewords per block) | [QRCodeConstants.capacityECCBaseValues](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) |
| Table 1 | Remainder bits per version | [QRCodeConstants.remainderBits](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) |
| Annex E | Alignment pattern center coordinates | [QRCodeConstants.alignmentPatternBaseValues](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) |

## Error Correction (Reed–Solomon)

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 8.5 | Generator polynomial and polynomial division over GF(2^8) | [EccBinaryEncoder.CalculateECC](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.cs) — scalar kernel plus SIMD variants ([SSSE3 / GFNI](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.Simd.cs), [ARM AdvSimd](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/EccBinaryEncoder.Simd.Arm.cs)) |
| Annex I | Worked encoding example | Used as test vectors in [EccBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/EccBinaryEncoderUnitTest.cs) |

Reference tests: [EccBinaryEncoderKernelParityTest](../../../tests/SkiaSharp.QrCode.Tests/EccBinaryEncoderKernelParityTest.cs) — every SIMD kernel is checked against a naive Section 8.5 reference implementation.

## Final Message Construction

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.6 | Data / ECC codeword interleaving across blocks, trailing remainder bits | [BinaryInterleaver.InterleaveCodewords](../../../src/SkiaSharp.QrCode/Internals/BinaryEncoders/BinaryInterleaver.cs) |

Reference tests: [BinaryInterleaverParityTest](../../../tests/SkiaSharp.QrCode.Tests/BinaryInterleaverParityTest.cs).

## Module Placement

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Finder patterns, separators, timing patterns, dark module | [ModulePlacer](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.cs) (`PlaceFinderPatterns`, `ReserveSeparatorAreas`, `PlaceTimingPatterns`, `PlaceDarkModule`) |
| Annex E | Alignment pattern placement | [ModulePlacer.PlaceAlignmentPatterns](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.cs) |
| Section 7.7.3 | Zigzag data placement (bottom-right, 2-column strips) | [ModulePlacer.PlaceDataWords](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.cs) |
| Section 7.9 | Format information placement (two redundant copies) | [ModulePlacer.PlaceFormat](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.cs) |
| Section 7.8.2 | Format information bits and mask (BCH + XOR mask) | [QRCodeConstants.GetFormatBits](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) |
| Section 7.10 | Version information (version 7+, two 3×6 patterns) | [ModulePlacer.PlaceVersion](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.cs), [QRCodeConstants.GetVersionBits](../../../src/SkiaSharp.QrCode/Internals/QRCodeConstants.cs) |

Reference tests: [ModulePlacerPlaceDataWordsParityTest](../../../tests/SkiaSharp.QrCode.Tests/ModulePlacerPlaceDataWordsParityTest.cs) — bit-parallel placement vs. naive per-module Section 7.7.3 reference.

## Data Masking

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.8.2 | The 8 mask pattern formulas (patterns 0–7) | [ModulePlacer.MaskCode / Pattern0–Pattern7](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.cs) |
| Section 8.8.2 | Penalty scoring rules 1–4 for mask selection | [ModulePlacer.Masking.cs](../../../src/SkiaSharp.QrCode/Internals/ModulePlacer.Masking.cs) — bit-parallel implementation of all four rules |

Reference tests: [ModulePlacerMaskPackedParityTest](../../../tests/SkiaSharp.QrCode.Tests/ModulePlacerMaskPackedParityTest.cs) — packed masking/scoring vs. naive byte-per-module reference formulas.

## Decoding Pipeline

```
Image ──> Binarization ──> Finder detection ──> Grid sampling ──┐
                                                                ├──> Format info ──> Unmask/Extract ──> Deinterleave ──> RS correction ──> Bit stream ──> Text
Module matrix (QRCodeData / span) ──────────────────────────────┘
```

Design notes (WHAT/WHY and Tier-1 scope) are in [QR Code Decoder](.github/docs/specs/qrcode-decoder.md).

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.8.2 | Format information decoding (Hamming match against all 32 valid patterns) | [FormatInformationDecoder](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/FormatInformationDecoder.cs) |
| Section 7.7.3 | Codeword extraction (inverse zigzag walk + unmasking on the fly) | [QRMatrixDecoder.ExtractCodewords](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/QRMatrixDecoder.cs) |
| Section 7.6 | Codeword deinterleaving into RS blocks | [QRMatrixDecoder.DeinterleaveCodewords](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/QRMatrixDecoder.cs) |
| Section 8.5 | Reed-Solomon error correction (syndromes → Berlekamp-Massey → Chien → Forney) | [EccBinaryDecoder.TryCorrect](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/EccBinaryDecoder.cs) |
| Section 7.4 | Bit stream decoding (mode segments, character count, ECI) | [QRBinaryDecoder.DecodeBitStream](../../../src/SkiaSharp.QrCode/Internals/BinaryDecoders/QRBinaryDecoder.cs) |
| Section 6.3.3 | Finder pattern detection (1:1:3:1:1 ratio scan + cross checks) | [FinderPatternFinder](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/FinderPatternFinder.cs) |
| — | Binarization (Otsu), module-size measurement, affine grid sampling | [QRImageDecoder](../../../src/SkiaSharp.QrCode/Internals/ImageDecoders/QRImageDecoder.cs) |

Reference tests: [QRCodeDecoderRoundTripTest](../../../tests/SkiaSharp.QrCode.Tests/QRCodeDecoderRoundTripTest.cs) (encode→decode round-trips, error injection), [EccBinaryDecoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/EccBinaryDecoderUnitTest.cs) (correction capacity boundaries), [QRCodeDecoderZXingCrossTest](../../../tests/SkiaSharp.QrCode.Tests/QRCodeDecoderZXingCrossTest.cs) (independent-encoder cross-validation), [QRCodeDecoderImageTest](../../../tests/SkiaSharp.QrCode.Tests/QRCodeDecoderImageTest.cs) (rotation/mirror/scale image cases).

## Maintenance Notes

- When adding or moving a spec-referenced implementation, update this map — but keep the detailed explanation (bit layouts, formulas, constraints) in the code comment next to the implementation, not here.
- Parity tests deliberately keep a *naive* reference implementation of the spec text; they are the executable form of the specification and should stay simple even when the production kernels change.
