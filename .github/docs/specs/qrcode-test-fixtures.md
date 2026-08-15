# QR Test Fixtures

Design record for the committed fixture corpus and its generator (`tools/QRInteropFixtures`): what it is, why it exists, and the external-oracle landscape it draws from. The test strategy behind it lives in [the test strategy plan](../plans/skiasharp-qrcode-microqr-rmqr-test-strategy.md).

---

## What

### Purpose

The corpus is a set of symbols produced by encoders **other than SkiaSharp.QrCode**, committed to the repository so that PR CI can verify this library's decoder against independent implementations without requiring external toolchains at test time. It exists to break the shared-bug blind spot of round-trip-only testing: if our encoder and decoder agree on the same mistake, round-trips still pass, externally generated fixtures do not.

### Layout

```
tests/SkiaSharp.QrCode.Tests/Fixtures/
├── StandardQr/
│   └── zxing-net/              (one directory per generator)
│       ├── case-name.json       manifest
│       ├── case-name.matrix.txt core module matrix, '1' dark / '0' light, row-major, LF, no quiet zone
│       └── case-name.png        clean black-on-white render (quiet zone 4, 8 px/module)
├── MicroQR/
│   ├── zint-libzint/           (same three files; PNG quiet zone 2 per the Micro QR spec)
│   └── qrtool/
└── RmQr/
    ├── zint-libzint/           (same three files; rectangular matrix / PNG, quiet zone 2 per ISO/IEC 23941)
    └── qrtool/
```

### Manifest schema (case-name.json, camelCase)

| Field | Meaning |
|---|---|
| `id` | Case name (= file stem) |
| `generator`, `generatorVersion` | Producing implementation and its pinned version |
| `symbolType` | `StandardQR`, `MicroQR` or `rMQR` |
| `version`, `width`, `height` | Symbol version and core module dimensions (rMQR: `version` = ISO/IEC 23941 version index + 1 = `RmQRVersion` value = libzint version number; `width` ≠ `height`) |
| `versionName` | Human-readable version where it is not a plain integer (rMQR: `R7x43`); omitted otherwise |
| `errorCorrectionLevel` | `L` / `M` / `Q` / `H` (requested and honored by the generator) |
| `mode` | Data segment mode reported by the generator |
| `maskPattern` | Mask chosen by the generator, `-1` when unknown (Micro QR / rMQR: reader-sourced by the sanity gate; rMQR is always `4`, the single rMQR mask expressed as a Standard QR pattern number) |
| `payloadText`, `payloadUtf8Hex` | Expected decode result (text and UTF-8 bytes) |
| `eciCharset` | `UTF-8` when the generator was asked to emit an ECI segment, else null |
| `quietZoneModules`, `pixelsPerModule` | PNG render parameters |

The loader (`FixtureLoader` in the test project) mirrors this schema; the two must change together.

### Corpus contents (Standard QR, 21 cases)

Every mode (Numeric / Alphanumeric / Byte) × every ECC level at small versions, plus: version 1-L alphanumeric capacity boundary (25 chars), mid (v10/v15/v25), maximum (v40-L at exactly 7089 digits), full alphanumeric charset, and UTF-8/ECI payloads (Japanese, emoji). All payloads are fixed literals or fixed repetitions, no randomness or timestamps, so regeneration is byte-reproducible for a given generator version.

### Corpus contents (Micro QR, 18 cases × 2 lineages)

Every version × legal ECC combination (M1 detection-only, M2/M3 L+M, M4 L+M+Q), every supported mode, capacity-boundary payloads (padding-free) plus short payloads (terminator + pad paths), and a UTF-8 byte-mode case (qrtool lineage only, libzint rejects UTF-8 input). Both lineages share the case list (`MicroQRCorpus`): 17 fixtures from zint-libzint, 18 from qrtool. Version and ECC are pinned per case; the mask pattern in the manifest comes from the zxing-cpp READER during the sanity gate, so it is externally sourced.

**Sanity gate**: every generated Micro QR fixture is rendered and decoded with the pinned zxing-cpp reader before it is written, payload, version and ECC level must match the manifest, so a broken generator cannot poison the committed corpus.

### Corpus contents (rMQR, 108 zint-libzint + 36 qrtool cases)

Built in implementation-plan Phase 5.1a, BEFORE the tables (its symbols are the oracle for the table tests). The two lineages carry different case lists (`RmQRCorpus`) on purpose:

- **zint-libzint** (ASCII-only): the systematic sweep, every one of the 32 versions × {Numeric `1`, Alphanumeric `A`, Byte `a`} single-character cases (96; ECC alternates so every version appears at both M and H; the leading data codewords pin the count-indicator width per version × mode), plus per-height capacity boundaries (numeric max at M on the widest width, byte max at H on the narrowest, 12).
- **qrtool**: one capacity-boundary case per version with rotating mode / ECC (32) plus UTF-8 / Japanese byte-mode cases (4; libzint rejects non-ASCII). No ECI header in either lineage.

Payloads are fixed literals or fixed cyclic patterns; the tool keeps its own copy of the version / capacity table (`RmQRVersionTable`, values from [rMQR Encoder](rmqr-encoder.md)) so it never depends on the library it produces oracles for.

**Sanity gate** (`RmQRSanityGate`): every fixture is rendered and decoded with the pinned zxing-cpp reader before it is written; the payload is compared on raw bytes (`Bytes` vs `payloadUtf8Hex`, because UTF-8 without ECI is exposed with a legacy-charset `Text` guess), `Extra("Version")` must equal `R{H}x{W}` and `Extra("EcLevel")` the manifest ECC; the reader's `DataMask` (4) is recorded. All 144 cases passed on first generation.

### Consuming tests

`StandardQrFixtureTest` decodes every fixture twice, matrix path (`TryDecode(modules, size, …)`) and image path (`TryDecode(SKBitmap, …)`), asserting payload, version, ECC level, and (matrix path) the generator's mask pattern. `MicroQRFixtureTest` likewise exercises both the matrix path and the PNG image path (`MicroQRCodeDecoder.TryDecode(SKBitmap, …)`, Phase 4b) and additionally asserts zero corrected errors and (matrix path) the reader-sourced mask pattern. `RmQrFixtureTest` (Phase 5.1a scaffolding) pins the rMQR corpus shape: both lineages, every version in both lineages and at both ECC levels, one single-character libzint case per version × mode, manifest ↔ matrix ↔ PNG consistency and table-independent structural invariants (finder / sub-finder corners, edge timing rows); the Phase 5.1b table oracle tests and the Phase 6.4 / 7 decode assertions join it as they land.

### Regeneration

```bash
# qrtool binary (pinned version + SHA-256), one-time per machine:
pwsh tools/QRInteropFixtures/get-qrtool.ps1

dotnet run --project tools/QRInteropFixtures -- regenerate
```

The tool wipes and rewrites each available generator's directory. Fixture updates must be committed as an explicit, reviewed change, a generator-version bump that silently alters fixtures is exactly what the corpus is meant to catch.

## Oracle capability matrix

Status meaning, **verified**: exercised in this repository; **documented**: capability confirmed from project documentation, not yet run here.

| Oracle | Standard QR | Micro QR | rMQR | Status | Notes |
|---|---|---|---|---|---|
| ZXing.Net 0.16.11 (NuGet, pinned) | encode + decode | - | - | verified | Fixture generator + `QRCodeDecoderZXingCrossTest`; in-process, no toolchain |
| [zxing-cpp](https://github.com/zxing-cpp/zxing-cpp) (via [ZXingCpp](https://www.nuget.org/packages/ZXingCpp) 0.5.2, pinned) | read + write | read | read | verified | Micro QR reading exercised by `tools/QRInteropFixtures -- spot-check-microqr` against this library's encoder (all versions × ECC, UTF-8) and as the fixture sanity gate. Its reader exposes `Extra("Version"/"EcLevel"/"DataMask")`, which supplies externally-sourced metadata for the Micro QR manifests (note: reports M1's implicit level as "L"). The official .NET wrapper bundles native binaries, so no external toolchain is needed. rMQR (`probe-rmqr`, 2026-08-15): the reader reports `Extra("Version")` as `"R7x43"`…`"R17x139"` (same spelling as the planned `RmQRVersion` members), `Extra("EcLevel")` as `"M"`/`"H"`, and `Extra("DataMask")` as `"4"` (rMQR's single mask is Standard QR mask pattern 4), verified on all 64 libzint version × ECC symbols and on qrtool rMQR output. Byte-mode UTF-8 content without ECI (qrtool Japanese payload) is exposed with `Text` decoded as a legacy charset guess (mojibake) while `Bytes` holds the exact UTF-8 bytes and `HasECI = false`; the wrapper offers no charset hint, so the rMQR sanity gate must compare `Bytes` against the manifest's `payloadUtf8Hex`, not `Text` |
| [Zint](https://zint.org.uk/) (libzint via ZXingCpp `BarcodeCreator`) | encode | encode | encode | verified | zxing-cpp's writer is libzint compiled into the same pinned native binary; Micro QR encoding exercised as a fixture lineage (`Options = "version=N,ecLevel=X"` honored; `ToImage(Scale=1, AddQuietZones=false)` is module-exact). Limits found: rejects UTF-8 Micro QR input ("Invalid UTF-8 in input"), and a Latin-1 payload with diacritics round-tripped transliterated, keep zint-lineage payloads ASCII. As an ENCODER lineage this counts as zint, independent of both this library and the Rust crates. rMQR (`probe-rmqr`, 2026-08-15): `version=1..32` maps to R7x43…R17x139 in height-major order (= the ISO version index + 1, 32/32 verified by output dimensions), `version=33..38` = fixed height 7/9/11/13/15/17 with automatic width, `ecLevel=M|H` honored; all 64 version × ECC symbols round-trip through the zxing-cpp reader |
| [qrcode2 / qrtool (Rust)](https://docs.rs/qrcode2) | encode | encode | encode | verified | `qrtool` 0.13.2 prebuilt binary pinned by version + SHA-256 (`get-qrtool.ps1`); Micro QR encoding exercised as a fixture lineage (all versions × ECC × modes incl. UTF-8, `--variant micro` with pinned `--symbol-version`/`--error-correction-level`/`--mode`). The `--type ascii` output is module-exact, so no image parsing is involved. M1's detection-only level is requested as `l` (the qrcode crate models it as L). rMQR encoding verified 2026-08-15 (`--variant rmqr -v <H> <W>`, all 32 versions × M/H, `--mode` honored): dimensions, capacities, format information and leading bit streams agree with the parameter tables in [rMQR Encoder](rmqr-encoder.md); qrtool has no Kanji mode option. **Known defect (found in rMQR Phase 5.4):** on versions 11 modules high or taller, qrtool 0.13.2 never writes the last h − 10 modules of the placement walk (column 1, rows 8..h−3), so the final ECC codeword of the last block loses its lowest (h − 10 − remainder) bits; zxing-cpp still reads every such symbol (one codeword error, corrected). Data codewords, all other ECC codewords, format copies and remainder bits match the spec-derived encoding exactly, and libzint symbols match byte for byte. Encoder-side oracle tests tolerate exactly this defect for the qrtool lineage; decoder tests must expect `ErrorsCorrected` = 1 (not 0) on the affected qrtool fixtures (R11x59, R13x43, R13x139, R15x43, R15x59, R15x139, R17x43, R17x59, R17x77, R17x99, R17x139 cases) |
| rmqrcode-python | - | - | encode | claimed | Capability not independently confirmed yet, verify before relying on it |
| BoofCV (Java) | decode | decode | - | claimed | Candidate additional decode oracle; not evaluated |

Independence caveat: ZXing.Net and zxing-cpp descend from the same ZXing lineage, count them as one independent implementation family, not two. Zint and the Rust crates are separate lineages. Note that zxing-cpp's READER and the libzint WRITER ship in one native binary but are algorithmically independent codebases; a created-then-read round-trip within that binary still exercises two lineages.

Oracle scarcity, decode direction: zxing-cpp is the only maintained OSS decoder for Micro QR and rMQR (ZXing Java/.NET, rqrr (Rust) and gozxing (Go) do not read them; BoofCV (Java) reads Micro QR only). Encoder verification therefore rests on one external decode lineage plus specification-derived vectors and the in-repo extraction tests, this is a structural limit, not a tooling gap. The decode direction has no such limit: multiple independent encoder lineages (zint, Rust qrcode2) generate the fixture corpus that exercises our decoder.

Toolchain policy: oracles must be pinned and acquirable without fragile environment-dependent builds, NuGet packages and prebuilt static binaries qualify; building C++/Python toolchains on dev machines or CI does not. Rust tools qualify via prebuilt release binaries. Under this policy the fixture generators for Phase 3 are libzint (via the pinned ZXingCpp package) and qrtool (prebuilt binary, pinned by version + checksum); Docker-pinned builds remain a fallback.

## Why

### Why ZXing.Net is the first generator

It is already a pinned test dependency, runs in-process, and needs zero external toolchain, so the harness (manifest schema, loader, writer, tests, CI wiring) could be built and proven against the shipped Standard QR implementation immediately. The plug-in generator interface (`IFixtureGenerator`) exists precisely so Zint / qrtool / zxing-cpp can be added for Micro QR and rMQR without touching the harness.

### Why fixtures assert the decode direction only

Two conformant encoders may legitimately produce different final matrices: mask selection and mode segmentation are implementation choices, not normative outputs. Matrix equality against an external fixture is therefore **not** a valid encoder-conformance test. The corpus verifies our *decoder* against external symbols; our *encoder* is verified by external decoders (ZXing.Net cross tests today, zxing-cpp in interop CI later) and, for Micro QR / rMQR, by spec-derived matrix tests where the mask is pinned by the manifest.

### Why fixtures are committed rather than generated in CI

PR CI stays self-contained and deterministic (no Rust/C++/Python toolchains), and fixture drift becomes a reviewable diff instead of a silent dependency-upgrade side effect.

## Lessons learned

- ZXing.Net's `ZXing.QrCode.Internal.Encoder` (rather than the public `BarcodeWriter`) returns the core `ByteMatrix` plus version, mode, and **mask pattern**, the mask metadata lets fixture tests assert that our format-information decode reproduces the generator's mask choice exactly, a much stronger check than payload equality alone.
- For Micro QR the equivalent mask metadata comes from the zxing-cpp READER during the sanity gate (`Extra("DataMask")`), not from the encoders, neither libzint (through the wrapper) nor qrtool reports its mask choice. Reader-sourced metadata is equally external and additionally proves the value is on the wire.
- qrtool's `--type ascii` output (two characters per module) makes the fixture matrix extraction exact and image-free, but trailing light modules are trimmed from each line, pad when parsing. `--mode` requires an explicit `--symbol-version`.
- Regenerating fixtures on Windows rewrites working-copy files with LF endings; when content is unchanged the only diff is EOL churn (`git diff --ignore-cr-at-eol` is empty), restore rather than commit such no-op rewrites.
- The public `QRCodeWriter.encode` path scales and pads to a requested pixel size; extracting the core matrix from it is lossy. Generating from the internal encoder and rendering PNGs ourselves keeps the matrix and the image pixel-exact for a known quiet zone and module size.
- Requested ECC is honored (never downgraded) by ZXing's encoder, so the manifest can record the requested level as the expected decode result without reading it back from the symbol.
- A committed external symbol is not automatically a byte-exact oracle: qrtool's rMQR output carries a systematic tail defect (last h − 10 placement modules never written) that zxing-cpp corrects silently, so the sanity gate (payload + metadata equality) passed 36/36 while the encoder-side final-message comparison found 12 mismatches. Where the reader can correct, cross-lineage agreement (libzint byte-exact, qrtool differing only in the last codeword's low bits, both consistent with the ISO codeword counts that qrtool's layout could not even hold) is what arbitrates; the corpus keeps the qrtool symbols as real-world "one corrupted ECC codeword" decoder cases with the defect documented, rather than dropping them.
