# QR Test Fixtures

Design record for the committed fixture corpus and its generator (`tools/QrInteropFixtures`): what it is, why it exists, and the external-oracle landscape it draws from. The test strategy behind it lives in [the test strategy plan](../plans/skiasharp-qrcode-microqr-rmqr-test-strategy.md).

---

## What

### Purpose

The corpus is a set of symbols produced by encoders **other than SkiaSharp.QrCode**, committed to the repository so that PR CI can verify this library's decoder against independent implementations without requiring external toolchains at test time. It exists to break the shared-bug blind spot of round-trip-only testing: if our encoder and decoder agree on the same mistake, round-trips still pass — externally generated fixtures do not.

### Layout

```
tests/SkiaSharp.QrCode.Tests/Fixtures/
└── StandardQr/                 (later: MicroQr/, RmQr/)
    └── zxing-net/              (one directory per generator)
        ├── case-name.json       manifest
        ├── case-name.matrix.txt core module matrix, '1' dark / '0' light, row-major, LF, no quiet zone
        └── case-name.png        clean black-on-white render (quiet zone 4, 8 px/module)
```

### Manifest schema (case-name.json, camelCase)

| Field | Meaning |
|---|---|
| `id` | Case name (= file stem) |
| `generator`, `generatorVersion` | Producing implementation and its pinned version |
| `symbolType` | `StandardQR` (later `MicroQR`, `rMQR`) |
| `version`, `width`, `height` | Symbol version and core module dimensions |
| `errorCorrectionLevel` | `L` / `M` / `Q` / `H` (requested and honored by the generator) |
| `mode` | Data segment mode reported by the generator |
| `maskPattern` | Mask chosen by the generator, `-1` when unknown |
| `payloadText`, `payloadUtf8Hex` | Expected decode result (text and UTF-8 bytes) |
| `eciCharset` | `UTF-8` when the generator was asked to emit an ECI segment, else null |
| `quietZoneModules`, `pixelsPerModule` | PNG render parameters |

The loader (`FixtureLoader` in the test project) mirrors this schema; the two must change together.

### Corpus contents (Standard QR, 21 cases)

Every mode (Numeric / Alphanumeric / Byte) × every ECC level at small versions, plus: version 1-L alphanumeric capacity boundary (25 chars), mid (v10/v15/v25), maximum (v40-L at exactly 7089 digits), full alphanumeric charset, and UTF-8/ECI payloads (Japanese, emoji). All payloads are fixed literals or fixed repetitions — no randomness or timestamps, so regeneration is byte-reproducible for a given generator version.

### Consuming tests

`StandardQrFixtureTest` decodes every fixture twice — matrix path (`TryDecode(modules, size, …)`) and image path (`TryDecode(SKBitmap, …)`) — asserting payload, version, ECC level, and (matrix path) the generator's mask pattern.

### Regeneration

```bash
dotnet run --project tools/QrInteropFixtures -- regenerate
```

The tool wipes and rewrites each available generator's directory. Fixture updates must be committed as an explicit, reviewed change — a generator-version bump that silently alters fixtures is exactly what the corpus is meant to catch.

## Oracle capability matrix

Status meaning — **verified**: exercised in this repository; **documented**: capability confirmed from project documentation, not yet run here.

| Oracle | Standard QR | Micro QR | rMQR | Status | Notes |
|---|---|---|---|---|---|
| ZXing.Net 0.16.11 (NuGet, pinned) | encode + decode | — | — | verified | Fixture generator + `QRCodeDecoderZXingCrossTest`; in-process, no toolchain |
| [zxing-cpp](https://github.com/zxing-cpp/zxing-cpp) | read + write | read | read | documented | The practical OSS *decode* oracle for Micro QR / rMQR; writing them requires the experimental zint-backed writer (`ZXING_WRITERS=NEW`) |
| [Zint](https://zint.org.uk/) | encode | encode | encode | documented | CLI encoder; encode-only (no decoder) |
| [qrcode2 / qrtool (Rust)](https://docs.rs/qrcode2) | encode | encode | encode | documented | `qrtool` CLI exposes `--variant micro` / `--variant rmqr`; fork of kennytm/qrcode |
| rmqrcode-python | — | — | encode | claimed | Capability not independently confirmed yet — verify before relying on it |
| BoofCV (Java) | decode | decode | — | claimed | Candidate additional decode oracle; not evaluated |

Independence caveat: ZXing.Net and zxing-cpp descend from the same ZXing lineage — count them as one independent implementation family, not two. Zint and the Rust crates are separate lineages.

Local toolchain availability (this dev machine, 2026-07): Docker and Python present; Zint, Rust/cargo absent. Container-pinned generators (Zint, qrtool, zxing-cpp) are planned for the Micro QR phase and require explicit tool installation or image pulls — deferred until then.

## Why

### Why ZXing.Net is the first generator

It is already a pinned test dependency, runs in-process, and needs zero external toolchain — so the harness (manifest schema, loader, writer, tests, CI wiring) could be built and proven against the shipped Standard QR implementation immediately. The plug-in generator interface (`IFixtureGenerator`) exists precisely so Zint / qrtool / zxing-cpp can be added for Micro QR and rMQR without touching the harness.

### Why fixtures assert the decode direction only

Two conformant encoders may legitimately produce different final matrices: mask selection and mode segmentation are implementation choices, not normative outputs. Matrix equality against an external fixture is therefore **not** a valid encoder-conformance test. The corpus verifies our *decoder* against external symbols; our *encoder* is verified by external decoders (ZXing.Net cross tests today, zxing-cpp in interop CI later) and, for Micro QR / rMQR, by spec-derived matrix tests where the mask is pinned by the manifest.

### Why fixtures are committed rather than generated in CI

PR CI stays self-contained and deterministic (no Rust/C++/Python toolchains), and fixture drift becomes a reviewable diff instead of a silent dependency-upgrade side effect.

## Lessons learned

- ZXing.Net's `ZXing.QrCode.Internal.Encoder` (rather than the public `BarcodeWriter`) returns the core `ByteMatrix` plus version, mode, and **mask pattern** — the mask metadata lets fixture tests assert that our format-information decode reproduces the generator's mask choice exactly, a much stronger check than payload equality alone.
- The public `QRCodeWriter.encode` path scales and pads to a requested pixel size; extracting the core matrix from it is lossy. Generating from the internal encoder and rendering PNGs ourselves keeps the matrix and the image pixel-exact for a known quiet zone and module size.
- Requested ECC is honored (never downgraded) by ZXing's encoder, so the manifest can record the requested level as the expected decode result without reading it back from the symbol.
