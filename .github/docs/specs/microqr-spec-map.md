# Micro QR Spec-to-Code Map (ISO/IEC 18004)

An index of where each part of the Micro QR symbology specification (ISO/IEC 18004, versions M1-M4) is implemented in this library. See [QR Symbology Architecture](qrcode-symbologies.md) for the document set and the shared/per-symbology component split.

This document is intentionally a **map, not a spec copy**. The normative details — bit layouts, formulas, edge-case constraints, and the reasoning behind implementation choices — live in code comments next to the implementation, where they stay in sync with the code.

Decoding is not implemented yet (implementation plan Phase 3); this map covers the encoding pipeline.

## Encoding Pipeline Overview

```
Text ──> Mode analysis ──> Data encoding ──> ECC ──> Module placement ──> Masking ──> Format info
```

Micro QR has a single Reed-Solomon block and no codeword interleaving; the interleaving stage of the Standard QR pipeline has no Micro QR counterpart.

## Text Analysis and Encoding Modes

| Spec reference | Topic | Implementation |
|---|---|---|
| Section 7.4.1 | Mode detection (Numeric / Alphanumeric / Byte) | [TextAnalyzer.Analyze](../../../src/SkiaSharp.QrCode/Internals/TextAnalyzer.cs) — shared across symbologies |
| Table 2 | Mode indicator widths (M1: none, M2-M4: version − 1 bits) and values | [MicroQrConstants.GetModeIndicatorLength / GetModeIndicatorValue](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| Table 3 | Character count indicator widths (Numeric = version + 2, Alphanumeric/Byte = version + 1) | [MicroQrConstants.GetCountIndicatorLength](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| Section 7.4.3-7.4.5 | Numeric / Alphanumeric / Byte segment bit streams | [MicroQrBinaryEncoder](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrBinaryEncoder.cs) |
| Table 2 | Terminator (3/5/7/9 zero bits, shortened at capacity) and pad codewords (0xEC/0x11, final 4-bit pad = 0000) | [MicroQrBinaryEncoder.EncodeDataCodewords](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrBinaryEncoder.cs) |
| — | Mode availability per version (M1: Numeric; M2: +Alphanumeric; M3/M4: +Byte; Kanji not implemented) | [MicroQrConstants.IsModeSupported](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |

Reference tests: [MicroQrBinaryEncoderUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrBinaryEncoderUnitTest.cs) (M1 golden vectors, the ISO "01234567" M2-L example, naive bit-string references for alphanumeric/byte/half-codeword padding).

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

Reference tests: [MicroQrCodeGeneratorUnitTest.CreateMicroQrCode_M2_MatrixStructure](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeGeneratorUnitTest.cs) (finder/separator/timing invariants), [MicroQrMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrMatrixExtractionTest.cs) (independent inverse-zigzag extraction).

## Data Masking

| Spec reference | Topic | Implementation |
|---|---|---|
| Table 10 | The 4 Micro QR mask conditions (Standard QR patterns 1/4/6/7) | [MicroQrModulePlacer.GetMaskBit](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |
| Section 7.8.3 | Edge-based mask evaluation (dark counts of right/lower edges, min·16 + max, highest wins) — evaluated on the two edges only, no trial matrices | [MicroQrModulePlacer.SelectAndApplyMask](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |

## Format Information

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Symbol number (3 bits from version + ECC) + mask (2 bits), BCH(15,5), XOR mask 0x4445 | [MicroQrConstants.GetFormatBits](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrConstants.cs) |
| — | Placement: bits 14…8 along row 8 cols 1-7, bit 7 at (8,8), bits 6…0 down col 8 rows 7-1 | [MicroQrModulePlacer.PlaceFormat](../../../src/SkiaSharp.QrCode/Internals/MicroQr/MicroQrModulePlacer.cs) |

Reference tests: [MicroQrConstantsUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrConstantsUnitTest.cs) (all 32 format patterns against the ISO-derived table plus a naive BCH reference), [MicroQrCodeGeneratorUnitTest.CreateMicroQrCode_FormatInfo_RoundTripsFromMatrix](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeGeneratorUnitTest.cs).

## Data Model and Serialization

| Spec reference | Topic | Implementation |
|---|---|---|
| — | Bit-packed core matrix with virtual quiet zone (spec quiet zone: 2 modules) | [MicroQrCodeData](../../../src/SkiaSharp.QrCode/MicroQrCodeData.cs) |
| — | "QRX" serialization container (magic + symbol type + width + height + packed bits) | [MicroQrCodeData.GetRawData](../../../src/SkiaSharp.QrCode/MicroQrCodeData.cs) |

Reference tests: [MicroQrCodeDataUnitTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrCodeDataUnitTest.cs).

## Maintenance Notes

- When adding or moving a spec-referenced implementation, update this map — but keep the detailed explanation (bit layouts, formulas, constraints) in the code comment next to the implementation, not here.
- Until the Micro QR decoder ships (Phase 3), [MicroQrMatrixExtractionTest](../../../tests/SkiaSharp.QrCode.Tests/MicroQr/MicroQrMatrixExtractionTest.cs) is the whole-pipeline consistency guard: it reads the matrix back with independent extraction code and recomputes the ECC. External-decoder verification is tracked in the [fixture record](qrcode-test-fixtures.md).
- Components marked "shared across symbologies" live outside `Internals/MicroQr`; the split is defined in [QR Symbology Architecture](qrcode-symbologies.md).
