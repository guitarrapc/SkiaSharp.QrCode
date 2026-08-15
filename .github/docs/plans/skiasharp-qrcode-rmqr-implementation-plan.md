# rMQR Implementation Plan for SkiaSharp.QrCode

## Purpose

This document details Phases 5-7 of the [Micro QR / rMQR implementation plan](skiasharp-qrcode-microqr-rmqr-implementation-plan.md) (the parent plan): adding rMQR Code (ISO/IEC 23941, R7x43-R17x139) encode, render, matrix decode, and image decode. The parent plan's guiding decisions, the [test strategy](skiasharp-qrcode-microqr-rmqr-test-strategy.md), and the [symbology architecture](../specs/qrcode-symbologies.md) all still apply; this document defines WHAT is built for rMQR, in WHICH order, and the concrete decisions the earlier phases left to "implementation time".

Progress log entries for the rMQR phases are appended to THIS document (parent-plan rule: Done / Lessons learned / benchmark delta per phase); the parent plan gets a one-line pointer per phase.

## Starting point (verified 2026-08-15)

Shipped and reusable as-is (Micro QR proved the boundaries):

| Component | Reuse for rMQR |
|---|---|
| `GaloisField`, `EccBinaryEncoder` / `EccBinaryDecoder` (+ SIMD kernels), `Polynom` | RS over GF(256), 0x11D, generator polynomials built on demand for any ECC count |
| `ECCInfo` | Up-to-two-block-group RS structure covers every rMQR version × ECC |
| `TextAnalyzer`, `CharacterSets`, `EncodingMode` | Mode alphabets identical to ISO/IEC 18004 |
| `SegmentDecoders` | Numeric / alphanumeric / byte payload groups and charset heuristics |
| `Binarizer`, `FinderPatternFinder.FindCandidates`, `LuminanceConverter`, `PerspectiveTransform` | rMQR's finder is the same 7×7 1:1:3:1:1 pattern as QR |
| `QRCodeImageBuilderBase<TSelf>`, `QRCodeRenderer` generic draw loops, `QrImageBuilderApiParityTest` | Third symbology joins the same base and the same parity test |
| `tools/QRInteropFixtures` harness (`IFixtureGenerator`, sanity gate, PNG renderer, manifest loader) | Add rMQR generators; the manifest already reserves `symbolType = rMQR` |
| "QRX" serialization container (magic + symbol type + width + height + packed bits) | Designed rectangular in Phase 2; rMQR uses symbol type 2 |

Square-only assumptions that rMQR must generalize (the only shared code this work touches):

- `IModuleMatrixView` exposes `Size` / `CoreSize`; rMQR needs width and height.
- `QRImageLayout.CreateLayout(matrixSize, …)` computes a square content rect.
- `BinaryInterleaver` lives in `Internals.StandardQr`; rMQR is its second consumer (lift trigger per the symbology spec).

Oracles, all under the pinned-package / prebuilt-binary policy (no C++/Python builds):

| Direction | Oracle | Status |
|---|---|---|
| Decode our symbols | zxing-cpp reader (ZXingCpp 0.5.2, `BarcodeFormat.RMQRCode`) | Verified reading libzint-created rMQR (`probe-creator`, round-trip OK) |
| Encode fixtures, lineage 1 | libzint via ZXingCpp `BarcodeCreator(RMQRCode)` | Verified creating rMQR; `version=` option and ASCII-only payloads as with Micro QR |
| Encode fixtures, lineage 2 | qrtool 0.13.2 `--variant rmqr -v <H> <W>` (`--type ascii`, module-exact) | Verified 2026-08-15 (all 32 versions × M/H; parameter tables and the 5.1a corpus derive from it) |
| rmqrcode-python | - | Dropped (toolchain policy; two encoder lineages already available) |

## rMQR facts that shape the design

The symbol parameter tables (32 versions: dimensions, alignment columns, total / data codewords, RS block structure per ECC, count-indicator widths, capacities) live in the design record [rMQR Encoder](../specs/rmqr-encoder.md), verified against the qrtool oracle on 2026-08-15 before implementation; the [spec-to-code map](../specs/rmqr-spec-map.md) names the planned component for every spec clause. Phase 5.1 turns the verification into permanent tests BEFORE any other code relies on the tables (transcription risk, see Risks). Summary of what shapes the design:

- 32 versions: heights {7, 9, 11, 13, 15, 17} × widths {27, 43, 59, 77, 99, 139}, where width 27 exists only for heights 11 and 13. Version identity is (height, width); the format information carries a 5-bit version index (0-31), so a decoder learns the symbol dimensions from the format information alone.
- ECC levels M and H only.
- Format information: 6 data bits (ECC level bit above the 5-bit version index) BCH-extended to 18 bits, two copies (finder-adjacent, sub-finder-adjacent) each XOR-masked with its own constant (verified 128/128 against oracle symbols). Because the format depends only on (version, ECC), it is a static 64-entry table at encode time and a 64-candidate nearest match at decode time.
- Exactly ONE data mask, ((row ⁄ 2) + (col ⁄ 3)) mod 2 == 0. No mask evaluation, no mask scoring: the placer is a fixed permutation per version.
- Function patterns: 7×7 finder top-left with separators; 5×5 sub-finder bottom-right; timing patterns along all four edges; vertical timing columns at width-dependent positions with 3×3 alignment patterns at their top and bottom ends (none at width 27).
- Bit stream: 3-bit mode indicators (Numeric 001, Alphanumeric 010, Byte 011, Kanji 100, ECI 111), terminator 000, per-version character-count indicator widths, 0xEC/0x11 padding, standard-QR-style block interleaving, quiet zone 2 modules.
- Kanji is deferred exactly as for Micro QR (tables keep the column). ECI: the decoder parses ECI segments (as `QRBinaryDecoder` does); the encoder does not emit ECI in this plan (UTF-8 bytes in Byte mode, matching Micro QR), revisit on demand.

## Guiding decisions specific to rMQR

### Public API shape (finalized spec-first in Phase 5.0)

Naming follows the shipped `MicroQR*` family: `RmQRCodeGenerator`, `RmQRVersion` (32 named members `R7x43` … `R17x139`), `RmQREccLevel { M, H }`, `RmQRCodeData`, `RmQRCodeCalculatedSize`, `RmQRCodeDecoder`, `RmQRCodeDecodeInfo`, `RmQRCodeImageBuilder`, plus `QRCodeRenderer` / `SKCanvas.Render` overloads. Overload set mirrors Micro QR: string, span, zero-allocation span-destination + `GetRequiredBufferSize`.

Version selection is the one genuinely new axis (parent plan: "fit strategy is two-dimensional"). Recommended shape:

- `RmQRVersion? requestedVersion` for an exact size (throws if the payload does not fit, with the Phase 4 follow-up style actionable message: actual length, applicable maximum, remedy).
- `RmQRFitStrategy` enum for auto-fit: `MinimizeArea` (default, fewest modules; ties broken toward the wider symbol so height stays minimal), `MinimizeWidth`, `MinimizeHeight`.
- `RmQRHeight?` constraint (`H7` … `H17`): fixed height, auto width. This is the "R<h>xauto" mode of libzint and the common print-lane use case (fixed label height, variable length). Fit strategy applies within the constrained set.

Rejected: encoding the strategy inside `RmQRVersion` (mixes exact sizes with policies), and a free-form `(maxWidth, maxHeight)` pair (over-general, and every combination is either equivalent to a height constraint or non-representable as an rMQR size anyway).

### Data model

`RmQRCodeData` with `Width` / `Height` (quiet zone included), `CoreWidth` / `CoreHeight`, `Version`, `this[row, col]` (virtual quiet zone reads light), QRX symbol type 2, default quiet zone 2. Same bit-packed core storage as `MicroQRCodeData`; a serialization negative test pins that QRX type 1 and type 2 payloads reject each other and QRR streams are rejected.

### Shared-code generalizations (each guarded by Standard QR benchmark flatness)

- `IModuleMatrixView`: replace `Size` / `CoreSize` with `Width` / `Height` / `CoreWidth` / `CoreHeight` (interface is internal, no public break). Existing square views return the same value for both axes; the draw loops become width/height loops. Struct specialization keeps this free of virtual dispatch.
- `QRImageLayout.CreateLayout(matrixWidth, matrixHeight, …)`: content rect is `matrixWidth×pixel` by `matrixHeight×pixel` when a module pixel size is given. When only an explicit canvas size is given, the rMQR builder fits the symbol into the canvas with a UNIFORM module scale (letterbox, centered on whole pixels), because non-uniform stretch would distort a rectangular symbol; Standard / Micro behavior (content rect = canvas) is unchanged. Recorded in the builder XML docs.
- `BinaryInterleaver` moves to `Internals.BinaryEncoders` (mechanical, identical body, benchmark-guarded), or rMQR gets its own if the Standard QR version turns out to hard-code version-1-40 assumptions, decided in 5.4 by reading it, not by policy.

### Performance posture

Correct-and-clear first, then measured optimization, exactly the Micro QR sequence: reference per-module placer + naive bit-string references first, parity tests, then a fused fast path in a follow-up with kernel benchmarks in the private MicroBenchmarks repo and E2E in `SkiaSharp.QrCode.Benchmark`. Note for that follow-up: rows up to 139 modules no longer fit one ulong (Micro QR's packed-row and PEXT/PDEP tricks assumed ≤ 17), and there is no mask scoring at all, so the profile will differ; do not port Micro QR kernels blindly. Zero allocation on the span paths is a Phase 5 requirement, not a follow-up.

### Decoder architecture

- Matrix decoder mirrors `MicroQRMatrixDecoder`'s boundary: `RmQRFormatInformationDecoder` (both copies, 64 candidates, best Hamming within BCH distance, version-vs-dimensions cross-check) → `RmQRMatrixDecoder` (inverse placement + fixed unmask reusing the placer's own function-module predicate, deinterleave, per-block RS via `EccBinaryDecoder`, error count) → `RmQRBinaryDecoder` (3-bit modes, per-version count widths, ECI parse, Kanji → `UnsupportedContent`, terminator, `SegmentDecoders`).
- Check during 6.3 whether ISO/IEC 23941 reserves misdecode-protection codewords the way ISO/IEC 18004 Table 9 does for Micro QR; if so, mirror the explicit post-correction capacity cap and its false-positive test class (Phase 3 lesson: a naive RS-strength decoder over-corrects).
- Image decoder: rMQR is not dihedrally symmetric and its format information pins the dimensions, so the pipeline is: shared Otsu → shared 7×7 finder candidates → module size from the finder → sample the finder-adjacent format region at 4 right-angle orientations × transpose → decode format → dimensions known → locate the sub-finder at its expected corner (confirms orientation, gives the 4th corner for `PerspectiveTransform`) → grid sample → `RmQRMatrixDecoder` arbitrates → inverted retry. Wrong orientations die at the 64-candidate format check in microseconds (Phase 4b lesson). Envelope stated explicitly: clean + mild perspective (finder + sub-finder corners), extreme aspect ratios (R7x139) covered by tests, not implied.
- `QRCodeDecoder` stays Standard-only; rMQR scanning is explicitly typed (`RmQRCodeDecoder`), so default scanning perf is untouched.

## Implementation order

Vertical slice again: encoder (with rendering) → matrix decoder → image decoder. Each sub-phase is one reviewable PR (or a small series), test-first, spec map updated, progress log entry appended.

### Phase 5, rMQR encoder and rendering

**5.0 Spec-first (no `src/` change)**

- Done 2026-08-15 (before this phase formally opened): `specs/rmqr-spec-map.md` (planned component per spec clause) and `specs/rmqr-encoder.md` (public API, verified parameter tables, decisions, verification record) exist; qrtool `--variant rmqr` verified as an rMQR encoder oracle (dimensions, capacities, format information, bit streams for all 32 versions).
- Done 2026-08-15 (same day): public signatures frozen in `specs/rmqr-encoder.md` (member-for-member review against the Micro QR surface); `tools/QRInteropFixtures -- probe-rmqr` recorded the remaining oracle facts in `specs/qrcode-test-fixtures.md` (libzint `version=` mapping, zxing-cpp `Extra()` spelling, qrtool rMQR read by zxing-cpp, UTF-8 `Bytes`-vs-`Text` caveat).
- Carried into 5.1a: test folder `tests/SkiaSharp.QrCode.Tests/RmQr/` is created by the first test; the fixture manifest already carries `width` / `height`.

Exit: met (see Progress log).

**5.1a Fixture corpus (`Fixtures/RmQr/{zint-libzint,qrtool}`), moved ahead of the tables**

The 5.1b oracle tests read committed external-encoder matrices, so the corpus is built first (Micro QR built its corpus in Phase 3 because its tables were small enough to pin from spec examples; rMQR's 32-version tables are the transcription risk, so external symbols enter at the table stage). No `src/` change.

- `RmQRCorpus` case list: every version × M/H with a one-character payload per mode (numeric `1`, alphanumeric `A`, byte `a`: these pin the count-indicator widths from the bit stream) plus, per height, capacity-boundary and mid-length payloads in each mode; UTF-8 / Japanese payloads on the qrtool lineage only (libzint is ASCII-only, see the fixture record).
- `ZintRmQRFixtureGenerator` (`BarcodeCreator(RMQRCode)`, `Options = "version=N,ecLevel=X"`, N = version index + 1, module-exact `ToImage(Scale=1, AddQuietZones=false)`) and `QrtoolRmQRFixtureGenerator` (`--variant rmqr -v <H> <W> -l <m|h> --mode <mode> --type ascii`); manifests carry `symbolType = rMQR`, `width` / `height`, version name `R{H}x{W}`, ECC, mode, payload text + UTF-8 hex.
- `RmQRSanityGate`: every fixture must decode with zxing-cpp (`Formats = RMQRCode`) before it is written; compare `Bytes` to `payloadUtf8Hex` (not `Text`, see the UTF-8 caveat in the fixture record), `Extra("Version")` to `R{H}x{W}`, `Extra("EcLevel")` to the manifest.
- `regenerate` extended; `FixtureLoader` gains an rMQR loader (rectangular matrix parser); `RmQrFixtureTest` scaffolding loads the corpus (decode assertions land with 6.4).
- Fixture record (`specs/qrcode-test-fixtures.md`): corpus layout, case count, sanity-gate rule.

Exit: met, see Progress log (108 zint + 36 qrtool cases, all gate-verified; loader + shape tests green).

**5.1b Tables (`Internals/RmQr/RmQRConstants`)**

- Version table (height, width, alignment column positions, total codewords, RS block structure per ECC as `ECCInfo`, count-indicator widths per mode), 64-entry format word table, capacity table (with the Kanji column present, unused), transcribed from the verified tables in `specs/rmqr-encoder.md`.
- Tests, structural first: 32 entries with the exact (height, width) set; data + ECC == total codewords for all 64 combos; total codewords × 8 == free-module count computed by a naive independent function-pattern painter (this is the check that caught the R17x59 error during pre-verification); every format word equals naive BCH(18,6) + XOR mask; pairwise Hamming distance of the 64 words ≥ the BCH minimum distance; ECC per block within 7..30; capacities from the table reproduce the published capacity table.
- Oracle test (the 5.1a corpus, no toolchain in CI): for each version, one libzint symbol and one qrtool symbol have the expected width/height, and their two format regions decode (via a naive reader in the test) to the expected (version, ECC) after unmasking with our two constants, this pins the XOR masks and bit order from two independent lineages before our own placer exists. The one-character-payload symbols also pin the count-indicator widths (first codewords after inverse zigzag + unmask + deinterleave), repeating the pre-verification permanently.

Exit: met, see Progress log (1,150 table tests green, both TFMs).

**5.2 Data model**

- `RmQRCodeData`, `RmQRVersion`, `RmQREccLevel`, `RmQRHeight`, `RmQRFitStrategy`, QRX type 2 serialization; `RmQRConstants.SymbolTypeRmQR = 2` registered next to Micro QR's 1.
- Tests mirror `MicroQRCodeDataUnitTest` (indexer bounds, quiet zone virtual reads, serialization round trip for all 32 versions, header/type/dimension negatives, cross-type rejection).

Exit: met, see Progress log (`RmQRVersion` / `RmQREccLevel` landed in 5.1b; `RmQRHeight` / `RmQRFitStrategy` move to 5.3 where the fit logic that gives them meaning lives).

**5.3 Bit stream (`RmQRBinaryEncoder`)**

- Segment building via `TextAnalyzer` (single-mode segments as in Micro QR), 3-bit mode + per-version count widths, terminator, byte alignment, 0xEC/0x11 padding; UTF-8 fallback via the shared path used by Micro QR.
- Auto version selection implementing `RmQRFitStrategy` × `RmQRHeight`; capacity errors with actual length / applicable maximum / remedy in mode-appropriate units.
- Tests: exhaustive parity against an independent naive bit-string reference (all 64 version/ECC × every mode × every length up to capacity, min/max/random contents), fit-strategy selection tables (hand-derived expected version per strategy at boundary lengths, ties), height-constrained selection, illegal combination rejection, error-message content.

**5.4 RS + interleaving**

- Reuse `EccBinaryEncoder`; move `BinaryInterleaver` to shared (or write `RmQRInterleaver`, see decision above). Standard QR encode benchmarks must be flat if the move happens.
- Tests: interleaved codeword layout vs a naive reference for every block structure in the table (both single-group and two-group versions).

**5.5 Placement (`RmQRModulePlacer`, reference implementation)**

- Function pattern painter (finder, separators, sub-finder, edge timing, vertical timing columns, alignment patterns), both format copies from the table, zigzag placement over non-function modules, fixed mask applied on the fly. Per-module readable reference; fast path deferred to the follow-up.
- Tests: function-module map equals the naive painter from 5.1 for all 32 versions; full-pipeline extraction test (inverse zigzag + unmask + deinterleave + RS recompute) for all 64 version/ECC combos × {zero, 0xFF, random} payloads; format regions read back to the table words.

**5.6 Public generator API**

- `RmQRCodeGenerator.CreateRmQRCode(...)` (string / span / span-destination) + `GetRequiredBufferSize`; span paths 0 B.
- zxing-cpp spot check tool: `tools/QRInteropFixtures -- spot-check-rmqr` encodes every version × ECC × {numeric, alphanumeric, byte, UTF-8} with this library and requires 100% decode with matching version / ECC. This is the encoder MVT gate (only one decode lineage exists for rMQR, structural limit recorded in the fixture spec).
- Tests: public API round trips through the extraction path, span/class parity, argument validation; benchmark `RmQREncodeEndToEnd` (numeric R7x43, alphanumeric R11x59, byte R17x139, Span + class).

**5.7 Rendering**

- `IModuleMatrixView` width/height generalization; `QRImageLayout` rectangular content rect + letterbox rule; `RmQRCodeImageBuilder` on `QRCodeImageBuilderBase` (typed `WithErrorCorrection(RmQREccLevel)` / `WithVersion(RmQRVersion)` / `WithFitStrategy` / `WithHeight`, quiet zone 2, no icon / finder styling); `QRCodeRenderer.Render(…, RmQRCodeData, …)`; `SKCanvas.Render(RmQRCodeData, …)`.
- Tests: `QrImageBuilderApiParityTest` extended (canonicalize `RmQRCodeData` / `RmQREccLevel` / `RmQRVersion`; allowed-difference list gains the fit-strategy options), module-to-pixel parity for all 32 versions (every module center vs `RmQRCodeData`), letterbox layout cases (wide canvas / tall canvas / exact / too-small), SVG viewBox is rectangular, quiet zone defaults.
- Playground + BlazorWasm + ConsoleApp sample gain rMQR generation (symbology selector, version/height/strategy controls, rMQR-aware stats); NativeAOT/WASM CI covers the path.
- Docs: spec map (encoding + rendering sections), symbology status table, README (symbology table, examples, FAQ), `docs/data-capacity.md` rMQR table (64 rows).

Exit (Phase 5): encoder MVT satisfied (spot check 100%); Standard + Micro image/encode benchmarks flat; rMQR encode benchmark recorded; progress log entry.

### Phase 6, rMQR matrix decoder

**6.1 Fixture corpus**: moved to 5.1a (built before the tables); 6.2-6.4 consume it. Anything the decoder phases find missing (extra damage cases, more UTF-8 payloads) is added to the corpus here.

**6.2 Format decoder** (`RmQRFormatInformationDecoder`): both copies, 64 candidates, best Hamming within BCH distance, copies disagreeing → the closer valid one, version-vs-dimensions cross-check. Test: exhaustive vs naive nearest-candidate reference over the full 18-bit space (262,144 words, fast), copy-selection cases, contradiction rejection.

**6.3 Matrix + bit-stream decoders** (`RmQRMatrixDecoder`, `RmQRBinaryDecoder`): as designed above, stackalloc-only, misdecode-protection check if the spec has one. Tests: golden bit-stream vectors (hand-derived R7x43 numeric; any ISO/IEC 23941 annex example), malformed-stream negatives (bad mode, truncated count, ECI then Kanji), damage tests per RS block (within t per block, beyond, format copies damaged singly and both within/beyond distance), all-versions round trip through the encoder.

**6.4 Public decoder** (`RmQRCodeDecoder`: `RmQRCodeData` / module matrix with width + height / zero-allocation span overloads + `GetMaxDecodedLength`; `RmQRCodeDecodeInfo` with version, ECC, corrected-error count). Quiet-zone stripping: the finder corner is the top-left dark module and the sub-finder corner is bottom-right, so a uniform border gives core dimensions from the dark bounding box; the test suite includes quiet zones 0/1/2/4 and asymmetric padding.

- Tests: round trips all 32 × 2 × modes × quiet zones, span parity, cross-symbology rejection in all directions (rMQR vs Standard vs Micro), committed corpus decode (both lineages, payload bytes + version + ECC).
- Benchmark `RmQRDecodeEndToEnd`; Standard/Micro decode benchmarks flat.

Exit (Phase 6): decoder MVT matrix rows satisfied; corpus green; progress log entry.

### Phase 7, rMQR image detection

**7.1 Image decoder** (`RmQRImageDecoder`, `RmQRCodeDecoder.TryDecode(SKBitmap, …)` / `TryDecodeImage(luminance, …)`): pipeline as designed above.

- Tests: clean renders all 32 versions × 2 ECC; module pixel sizes 3-13; non-integer scale; translation; quiet zone 1/2/4; 90/180/270 rotation; mirror; inverted colors; JPEG q60 / low contrast / seeded noise; mild perspective (both finder-anchored corners); extreme aspect ratio set (R7x139, R17x27 does not exist, use R11x27 and R17x139) under every transform; negatives (Standard / Micro must not decode, blank, too small); committed fixture PNGs (both lineages) via the image path.
- Playground decode panel and generated-image self-check fall back to `RmQRCodeDecoder`; BlazorWasm sample likewise.
- Benchmark `RmQRImageEndToEnd` (render + PNG for R7x43 / R17x139, image decode span); Standard/Micro image benchmarks flat.

Exit (Phase 7): decoder MVT image rows and the representative degradation subset green; rMQR feature-complete; symbology status table says Shipped; progress log entry. Then the parent plan's Phase 8 (interop CI adds rMQR round trips both directions) and Phase 9 (physical device acceptance, rMQR subset per test strategy §11) gate the release.

### Follow-ups (after Phase 7, optional, benchmark-driven)

- Placer / bit-stream fast paths (private MicroBenchmarks kernel loop, disassembly-read, SIMD rounds in scope), ported back with parity tests exactly as the Micro QR follow-ups.
- ECI emission in the encoder; Kanji mode across symbologies (single decision, three symbologies).

## Dependency graph

```
5.0 spec ─ 5.1a fixtures ─┬─ 5.1b tables ─┬─ 5.3 bit stream ─┐
                          │               ├─ 5.4 RS/interleave ┼─ 5.5 placer ─ 5.6 generator ─ 5.7 rendering
                          └─ 5.2 data ────┘                     │
                                                                └─ 6.2 format dec ─ 6.3 matrix/bit dec ─ 6.4 public decoder ─ 7.1 image
```

## Cross-cutting

- Namespace rule: `Internals.RmQr` references shared namespaces only; never `Internals.StandardQr` or `Internals.MicroQR`. Anything rMQR needs from those is lifted to shared (second-consumer trigger) with a benchmark guard.
- Benchmark guards per phase: `QRCodeEncodeEndToEnd`, `QRCodeDecodeEndToEnd`, `QRCodeImageEndToEnd`, `MicroQREncodeEndToEnd`, `MicroQRDecodeEndToEnd`, `MicroQRImageEndToEnd` flat (ABBA-ordered runs where the delta is within layout noise). Allocations byte-identical.
- Test-first, spec-map updates, and progress-log entries are mandatory per phase (project rules).
- Test count expectation: order of +250 (Phase 5), +200 (Phase 6), +150 (Phase 7) on each TFM, dominated by all-32-versions × 2-ECC sweeps.

## Risks

- Table transcription without an in-repo copy of ISO/IEC 23941: mitigated by 5.1's structural invariants (free-module count, format BCH self-consistency) and the two-lineage oracle test before any dependent code exists. If libzint and qrtool disagree with each other on a version, zxing-cpp's decode of both arbitrates, then the spec text.
- Rendering generalization touches Standard QR draw loops: struct specialization should keep it free, but the phase exit requires the image benchmarks and golden-pixel tests to prove it, not assume it.
- Sub-finder detection at extreme aspect ratios and small module sizes: mitigated by learning the dimensions from the finder-side format information first, so the sub-finder is a confirmation at a known location rather than a search.
- libzint payload limits (ASCII-only, transliteration) apply to the zint lineage as with Micro QR; UTF-8 fixtures ride qrtool.
- Only one rMQR decode lineage exists (zxing-cpp); the encoder MVT therefore combines the spot check with the extraction tests and spec-derived vectors, and the interop CI cannot add a second reader later unless one appears. Recorded in the fixture spec as a structural limit.

## Progress log

### Phase 5.0, completed 2026-08-15

**Done**

- Spec-first documents: `specs/rmqr-spec-map.md` (planned component per ISO/IEC 23941 clause) and `specs/rmqr-encoder.md` (frozen public signatures, oracle-verified symbol parameter tables, decisions, verification record); docs index and this plan updated.
- Symbol parameters verified against the pinned qrtool oracle before any implementation: 32/32 dimensions, 192/192 capacities, 32/32 total-codeword counts (geometry), 96/96 count-indicator widths (bit-stream read-back), 128/128 format-information copies, mask / zigzag start / interleaving. Two recall errors were caught and corrected (R17x59 total codewords 90 → 88; three numeric count widths).
- `tools/QRInteropFixtures -- probe-rmqr` (new command): libzint `version=1..32` = height-major index + 1 (32/32), `33..38` = fixed-height auto-width; zxing-cpp `Extra("Version")` spells `"R7x43"`…, `Extra("EcLevel")` `"M"/"H"`, `Extra("DataMask")` `"4"`; 64/64 libzint symbols and 5/5 qrtool symbols (incl. Japanese UTF-8) read by zxing-cpp; UTF-8 without ECI must be compared on `Bytes`, not `Text`. Recorded in `specs/qrcode-test-fixtures.md`.

**Lessons learned**

- Capacity tables cannot pin count-indicator widths (byte-alignment slack); reading the width from an oracle bit stream can, and validates mask, zigzag start and interleaving in the same step. Multi-block versions must be deinterleaved before reading "the first bits".
- Cross-table invariants (geometry ↔ codewords ↔ ECC-per-block bounds) catch recall errors that per-table plausibility checks pass.
- zxing-cpp's rMQR reader labels the single mask as Standard QR pattern 4, a free consistency check on the mask formula.

**Benchmarks**

- Not applicable: no `src/` change (docs + a diagnostic command in `tools/`).

### Phase 5.1a, completed 2026-08-15

**Done**

- `tools/QRInteropFixtures`: `RmQRFixtureModel` (case definition, generator interface, `RmQRVersionTable` with the oracle-verified dimensions/capacities, tool-local by design), `RmQRCorpus` (108 zint-libzint cases: 32 versions × single-char N/A/B + 12 per-height capacity boundaries; 36 qrtool cases: 32 rotating-mode/ECC capacity boundaries + 4 UTF-8/Japanese), `ZintRmQRFixtureGenerator` (`version=index+1,ecLevel=`), `QrtoolRmQRFixtureGenerator` (`--variant rmqr --symbol-version H W`, payload via UTF-8 file), `RmQRSanityGate` (zxing-cpp; raw `Bytes` vs `payloadUtf8Hex`, `Extra("Version"/"EcLevel")`, records `DataMask`), wired into `regenerate`. Shared writer/renderer generalized to rectangular symbols; manifest gains an optional `versionName` (omitted when null, so Standard/Micro manifests are byte-identical to before, verified by regenerating them: no content diff).
- Corpus committed: `Fixtures/RmQr/{zint-libzint,qrtool}`, 144 fixtures × 3 files (~1.2 MB, PNG at 8 px/module), every one passed the gate on first generation.
- Tests: `FixtureLoader.ReadRectangularMatrix` (square `ReadMatrix` now delegates), `FixtureManifest.VersionName`; `RmQr/RmQrFixtureTest` scaffolding (both lineages present, every version in both lineages and at both ECC, one single-char libzint case per version × mode, UTF-8 present; per fixture: manifest ↔ matrix ↔ PNG consistency, `version` = index + 1, `maskPattern` = 4, table-independent structural invariants). Fixture tests across all three symbologies: 520 green (net8.0 + net10.0).

**Lessons learned**

- Regenerating touches every corpus, and a schema addition that serializes `null` rewrites all existing manifests; opt-out-when-null keeps unrelated corpora byte-stable and the review diff honest. Regenerate-then-`git diff --stat` on the untouched corpora is the cheap drift check.
- Different case lists per lineage is the right shape when the lineages have different strengths (libzint: ASCII sweep for table pinning; qrtool: UTF-8 and boundaries), rather than one list with per-generator skips.

**Benchmarks**

- Not applicable: no `src/` change (tool, committed fixtures, test infrastructure).

### Phase 5.1b, completed 2026-08-15

**Done**

- `src`: public `RmQRVersion` (32 members, value = ISO index + 1) and `RmQREccLevel` (M = 0, H = 1 = format ECC bit); `Internals/RmQr/RmQRConstants` (heights / widths / `TryGetVersion`, alignment columns per width, total codewords + remainder bits, data codewords and block counts per version × ECC exposed as the shared `ECCInfo` (shorter blocks first, uniform ECC per block), count-indicator widths N/A/B plus the spec-transcribed Kanji column, 3-bit mode indicator values, terminator length, `GetFormatBits` = BCH(18,6) over (ECC bit, version index) XOR the per-copy constant, QRX symbol type 2, quiet zone 2). Tables transcribed from the verified design record; no other code depends on them yet.
- Tests (test-first, +1,150 on net8.0 + net10.0; full suite 5,516, 0 failed): `RmQRNaiveReference` (independent painter, mask, zigzag walk, format-region reader, naive BCH, deinterleave), `RmQRConstantsUnitTest` (32 entries + inverse lookup + non-rMQR rejections; alignment columns vs width table and inside the symbol; data + ECC = total with block splits differing by one and ECC per block in 7..30; total × 8 + remainder = free-module count from the independent painter; published N/A/B capacities reproduced from data codewords + count widths for all 64 combos; count widths fit their capacities and are monotone; format words = naive BCH + XOR, pairwise distance ≥ 7, no cross-copy collision), `RmQRConstantsOracleTest` (all 144 corpus symbols: dimensions → version, BOTH format copies equal the table words, walked bit count = 8 × total + remainder; all 96 single-character libzint symbols: leading codewords after inverse zigzag + unmask + deinterleave yield the expected mode indicator, count-indicator width and payload bits).
- Docs: spec map rows for the tables now link to code and tests; design record status → in progress; the fixture record already describes the corpus the oracle tests read.

**Lessons learned**

- "ECC per block is even" was a wrong generalization from the multi-block versions: single-block R7x43-M / R7x59-M use 7 and 9. The test caught it in the first run; the invariant is "7 ≤ ECC per block ≤ 30", nothing more.
- Reading the count width from the oracle bit stream needs the block structure first (multi-block symbols interleave the leading bytes), so the oracle test exercises `GetEccInfo` for free; a wrong block count would surface as a scrambled mode indicator, not as a subtle capacity error.

**Benchmarks**

- Not applicable: table additions only, nothing on any hot path (no Standard / Micro QR file touched).

### Phase 5.2, completed 2026-08-15

**Done**

- `src`: public `RmQRCodeData` (frozen signature from the design record): rectangular bit-packed core (MSB-first, row-major over the core width), virtual quiet zone, `Width` / `Height` / `Version` / `this[row, col]`, "QRX" symbol type 2 serialization (`GetRawDataSize`, `GetRawData()`, `GetRawData(IBufferWriter<byte>)`), constructors (version + quiet zone; `byte[]` / `ReadOnlySpan<byte>` + quiet zone, padding bits canonicalized), internal zero-allocation accessors `GetCoreWidth` / `GetCoreHeight` / `GetCoreModule` / `GetCoreData` / `SetCoreData` mirroring `MicroQRCodeData` (the matrix decoder and placer consume these). Micro QR (type 1) and QRR containers rejected in both directions.
- Tests (test-first, +210 on net8.0 + net10.0; full suite 5,726, 0 failed): `RmQRCodeDataUnitTest`, exercising every version with synthetic cores and corpus symbols (no encoder exists yet), all constructor / header / dimension negatives (incl. transposed sizes), quiet-zone offset indexing, `IBufferWriter` parity, and a Release-only steady-state zero-allocation assertion over `SetCoreData` / `GetCoreData` / `GetCoreModule` / `GetRawData(IBufferWriter)`.
- Docs: spec map data-model rows link to code and tests.

**Lessons learned**

- With the corpus in place, a data type can be tested against real external symbols before its own encoder exists; the fixture loader's rectangular reader is the only test infrastructure the phase needed.

**Benchmarks**

- Not applicable: new type only, no shared or hot-path code touched (the packing loops are the Micro QR scalar shape; a fast path, if ever needed, belongs to the placer follow-up and would be measured there).
