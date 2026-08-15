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

Exit: met, see Progress log.

**5.4 RS + interleaving**

- Reuse `EccBinaryEncoder`; move `BinaryInterleaver` to shared (or write `RmQRInterleaver`, see decision above). Standard QR encode benchmarks must be flat if the move happens.
- Tests: interleaved codeword layout vs a naive reference for every block structure in the table (both single-group and two-group versions).

Exit: met, see Progress log (`BinaryInterleaver` lifted to shared, Standard QR encode benchmark flat).

**5.5 Placement (`RmQRModulePlacer`, reference implementation)**

- Function pattern painter (finder, separators, sub-finder, edge timing, vertical timing columns, alignment patterns), both format copies from the table, zigzag placement over non-function modules, fixed mask applied on the fly. Per-module readable reference; fast path deferred to the follow-up.
- Tests: function-module map equals the naive painter from 5.1 for all 32 versions; full-pipeline extraction test (inverse zigzag + unmask + deinterleave + RS recompute) for all 64 version/ECC combos × {zero, 0xFF, random} payloads; format regions read back to the table words.

Exit: met, see Progress log (module-exact against all 144 external symbols).

**5.6 Public generator API**

- `RmQRCodeGenerator.CreateRmQRCode(...)` (string / span / span-destination) + `GetRequiredBufferSize`; span paths 0 B.
- zxing-cpp spot check tool: `tools/QRInteropFixtures -- spot-check-rmqr` encodes every version × ECC × {numeric, alphanumeric, byte, UTF-8} with this library and requires 100% decode with matching version / ECC. This is the encoder MVT gate (only one decode lineage exists for rMQR, structural limit recorded in the fixture spec).
- Tests: public API round trips through the extraction path, span/class parity, argument validation; benchmark `RmQREncodeEndToEnd` (numeric R7x43, alphanumeric R11x59, byte R17x139, Span + class).
- **Decision to make here (found in 5.3): the default `RmQRFitStrategy`.** `MinimizeArea` is the design-record default, but it is not the "flattest symbol" users may expect: 12 digits at M fit R7x43 (301 modules) yet R11x27 (297) is selected, so the default picks a taller, narrower symbol whenever the area is smaller. Options: keep `MinimizeArea` (fewest modules, the printable-area argument) and document it prominently in README/FAQ with the R7x43-vs-R11x27 example; or switch the default to `MinimizeHeight` (the label-lane use case, always the flattest fitting symbol) and keep `MinimizeArea` opt-in. Decide before the public signature ships, record in the design record Decisions table, and cover the chosen default in `RmQRCodeGeneratorUnitTest` and the README example.

Exit: met, see Progress log (default strategy decided: MinimizeArea; spot check 256/256; E2E benchmark recorded).

**5.7 Rendering**

- `IModuleMatrixView` width/height generalization; `QRImageLayout` rectangular content rect + letterbox rule; `RmQRCodeImageBuilder` on `QRCodeImageBuilderBase` (typed `WithErrorCorrection(RmQREccLevel)` / `WithVersion(RmQRVersion)` / `WithFitStrategy` / `WithHeight`, quiet zone 2, no icon / finder styling); `QRCodeRenderer.Render(…, RmQRCodeData, …)`; `SKCanvas.Render(RmQRCodeData, …)`.
- Tests: `QrImageBuilderApiParityTest` extended (canonicalize `RmQRCodeData` / `RmQREccLevel` / `RmQRVersion`; allowed-difference list gains the fit-strategy options), module-to-pixel parity for all 32 versions (every module center vs `RmQRCodeData`), letterbox layout cases (wide canvas / tall canvas / exact / too-small), SVG viewBox is rectangular, quiet zone defaults.
- Playground + BlazorWasm + ConsoleApp sample gain rMQR generation (symbology selector, version/height/strategy controls, rMQR-aware stats); NativeAOT/WASM CI covers the path.
- Docs: spec map (encoding + rendering sections), symbology status table, README (symbology table, examples, FAQ), `docs/data-capacity.md` rMQR table (64 rows).

Exit (Phase 5): **met, see Progress log** (spot check 256/256 in 5.6; Standard + Micro image benchmarks flat in 5.7; rMQR encode benchmark recorded in 5.6; rMQR image benchmark deferred to 6/7 with the decode path).

### Phase 6, rMQR matrix decoder

**6.1 Fixture corpus**: moved to 5.1a (built before the tables); 6.2-6.4 consume it. Anything the decoder phases find missing (extra damage cases, more UTF-8 payloads) is added to the corpus here.

**6.2 Format decoder** (`RmQRFormatInformationDecoder`): both copies, 64 candidates, best Hamming within BCH distance, copies disagreeing → the closer valid one, version-vs-dimensions cross-check. Test: exhaustive vs naive nearest-candidate reference over the full 18-bit space (262,144 words, fast), copy-selection cases, contradiction rejection.

**6.3 Matrix + bit-stream decoders** (`RmQRMatrixDecoder`, `RmQRBinaryDecoder`): as designed above, stackalloc-only, misdecode-protection check if the spec has one. Tests: golden bit-stream vectors (hand-derived R7x43 numeric; any ISO/IEC 23941 annex example), malformed-stream negatives (bad mode, truncated count, ECI then Kanji), damage tests per RS block (within t per block, beyond, format copies damaged singly and both within/beyond distance), all-versions round trip through the encoder.

**6.4 Public decoder** (`RmQRCodeDecoder`: `RmQRCodeData` / module matrix with width + height / zero-allocation span overloads + `GetMaxDecodedLength`; `RmQRCodeDecodeInfo` with version, ECC, corrected-error count). Quiet-zone stripping: the finder corner is the top-left dark module and the sub-finder corner is bottom-right, so a uniform border gives core dimensions from the dark bounding box; the test suite includes quiet zones 0/1/2/4 and asymmetric padding.
- Corpus expectations: the qrtool lineage carries a documented tail defect (fixture record: last h − 10 placement modules never written, one ECC codeword low bits lost on versions ≥ 11 high), so the corpus decode assertions expect `ErrorsCorrected` = 0 for libzint fixtures and `ErrorsCorrected` = 1 for the affected qrtool fixtures (a free "one corrupted ECC codeword" robustness class); every payload must still decode.

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

### Phase 5.3, completed 2026-08-15

**Done**

- `src`: `Internals/RmQr/RmQRBinaryEncoder.EncodeDataCodewords(text, version, ecc, in TextAnalysisResult, destination)`: 3-bit mode, per-version count indicator, Numeric / Alphanumeric / Byte payload bits (Latin-1 narrow, UTF-8 transcode without ECI, fixed 160-byte stack budget with pool fallback), terminator shortened at capacity, byte alignment, 0xEC/0x11 pads; writes through the shared `BitWriter` into the caller's buffer (allocation-free on net8.0+/netstandard2.1; the netstandard2.0 UTF-8 path is the documented exception, as in Standard QR). Readable reference shape by design, the register-accumulator fast path is a benchmark-driven follow-up.
- `src`: `Internals/RmQr/RmQRVersionSelector` (`GetRequiredBits`, `GetMaxDataLength`, `Fits`, `IsBetter`, `Select`) implementing the design record's fit semantics: exact `requestedVersion` (must agree with `height` when both given), else best fitting version by `RmQRFitStrategy` within the optional `RmQRHeight`; actionable capacity errors (actual length, applicable maximum in mode units, the binding version, remedy incl. "allow a taller symbol" for height-constrained fits). Public enums `RmQRFitStrategy` (MinimizeArea default / MinimizeWidth / MinimizeHeight) and `RmQRHeight` (H7…H17, value = module height).
- Tests (test-first, +1,026 on net8.0 + net10.0; full suite 6,752, 0 failed): `RmQRBinaryEncoderUnitTest` (oracle golden `22 20 EC 11 EC 11`, terminator / alignment / empty / UTF-8 / Latin-1 references, and the encoder-side oracle over all 144 corpus symbols: our data codewords == the external symbol's deinterleaved data codewords, both lineages), `RmQRBinaryEncoderParityTest` (vs `RmQRNaiveReference.NaiveDataCodewords`: all 64 × 3 modes × every length to capacity × min/max/cyclic/random, full Latin-1, UTF-8 multi-byte + surrogates, all terminator classes), `RmQRVersionSelectorUnitTest` (inverse-capacity property for all 64 × 3, hand-derived fit tables per strategy, height constraint, requested-version paths, invalid enums, tie-break comparator incl. the R7x99 = R9x77 area tie, message content).
- Docs: spec map rows link to code and tests.

**Lessons learned**

- MinimizeArea is not "the version Standard QR users expect": 12 digits at M fit R7x43 (301 modules) but R11x27 (297) is smaller, so the default picks the taller, narrower symbol. The design record's tie rule (smaller height) only applies to genuine area ties (R7x99 = R9x77 = 693); users who want the flattest symbol need MinimizeHeight. Worth a README note when the public generator lands.
- With forced single-segment encoding, two conformant encoders must produce byte-identical data codewords, so the committed corpus doubles as an encoder-side oracle (144/144 matched on the first run); the zxing-cpp spot check in 5.6 then only has to prove placement, not the bit stream.

**Benchmarks**

- Not applicable yet: `src` additions are new internal components not reachable from any public path (no Standard / Micro QR file touched); the rMQR E2E benchmark lands with the public generator (5.6), and the kernel loop is deferred to the follow-up per the user's instruction.

### Phase 5.4, completed 2026-08-15

**Done**

- Decision (read, not policy): `BinaryInterleaver.InterleaveCodewords` never used its `version` parameter, only the `ECCInfo` block structure, and only `CalculateInterleavedSize` reached into `QRCodeConstants.GetRemainderBits`. Lifted the class to `Internals.BinaryEncoders` (shared), dropped the unused parameter and made the remainder-bit count an argument; `QRCodeGenerator` call sites changed mechanically (two lines), tests moved to `tests/Shared/`. Standard QR encode benchmark flat (see below).
- `src`: `Internals/RmQr/RmQRCodewordEncoder` (`GetFinalMessageSize`, `AssembleFinalMessage`): per-block Reed-Solomon via the shared `EccBinaryEncoder` (group 1 shorter blocks first, uniform ECC per block, fixed 156-byte stack scratch = the R17x139-H maximum), then the shared interleaver writes data + ECC + zeroed remainder tail into the caller's buffer.
- Tests (test-first, +546 on net8.0 + net10.0; full suite 7,298, 0 failed): `RmQRCodewordEncoderUnitTest` (final-message size for all 64 combos, data deinterleaves back via the naive reference and ECC equals the shared kernel per block on a dirty buffer, undersized-buffer negatives, and the encoder-side oracle over all 144 corpus symbols: our final message equals the interleaved stream walked out of every libzint symbol byte for byte); the shared interleaver keeps its parity + unit tests unchanged apart from the signature.

**Lessons learned**

- The encoder-side oracle found a real defect in the second lineage: qrtool 0.13.2 (qrcode2) never writes the last h − 10 placement modules (column 1, rows 8..h−3), so on versions ≥ 11 modules high its final ECC codeword loses the lowest (h − 10 − remainder) bits (12 of 36 corpus symbols; zxing-cpp had corrected them silently, so the sanity gate saw nothing). Arbitration: libzint matches byte for byte and the ISO codeword counts could not even fit into qrtool's layout, so the tables stand; the test tolerates exactly that difference for the qrtool lineage, the fixture record documents it, and 6.4 will expect `ErrorsCorrected` = 1 on those symbols. Recorded as a lesson in the fixture record: reader-corrected symbols pass a payload gate while still being wrong on the wire, so cross-lineage byte comparison, not decode success, is the oracle for encoder output.
- "Second consumer appeared" was again the right lift trigger, and reading the code settled the shared-vs-copy question in seconds: the shared version was already symbology-agnostic except for one constant lookup, which became a parameter.

**Benchmark delta (`QRCodeEncodeEndToEnd`, net10.0 Release, warmup 3 × 10 iterations, before = HEAD worktree, after = this change)**

| Benchmark | Before | After | Allocated |
|---|---|---|---|
| QR_Numeric_V1_L_Encode | 1,830.6 ns | 1,791.8 ns | 120 B / 120 B |
| QR_Alphanumeric_V1_M_Encode | 1,831.5 ns | 1,900.9 ns | 120 B / 120 B |
| QR_Byte_Url_V6_M_Encode | 3,768.6 ns | 3,688.4 ns | 280 B / 280 B |
| QR_Byte_V40_L_Encode | 130,143.7 ns | 133,198.1 ns | 3,984 B / 3,984 B |
| QR_Byte_V40_H_Encode | 127,418.5 ns | 126,997.4 ns | 3,808 B / 3,808 B |

All within ±4% (single-run layout noise, both directions), allocations byte-identical; the interleaver body is unchanged and the dropped parameter was dead.

### Phase 5.5, completed 2026-08-15

**Done**

- `src`: `Internals/RmQr/RmQRModulePlacer` (readable per-module reference, allocation-free, writes every module so callers need not zero the buffer): `IsFunctionModule` (the single predicate the decoder will reuse), `GetMaskBit`, `PlaceSymbol` = `PlaceFunctionModules` (edge timing, vertical timing + 3×3 alignment, corners, finder + separators, sub-finder) + `PlaceFormat` (both copies) + `PlaceData` (zigzag with the fixed mask, remainder bits light). Geometry documented in the class remarks.
- Tests (test-first, +996 on net8.0 + net10.0; full suite 8,294, 0 failed): `RmQRModulePlacerUnitTest` (predicate vs naive painter on every module of every version; structural invariants of every function pattern for all 32 versions on a dirty buffer; format copies read back; negatives; **module-exact oracle: our placement of the same payload reproduces all 108 libzint symbols module for module and all 36 qrtool symbols except the documented tail column**), `RmQRMatrixExtractionTest` (place → naive inverse walk → deinterleave → data + per-block ECC + zero remainder + payload-independent function modules, 64 × 4 payloads).

**Lessons learned**

- Paint order carries one real rule: on height 9 the finder separator row (7) contains the bottom-left corner cell (7,0), and the separator (light) wins, both external lineages agree, the module-exact oracle caught our first draft (corner painted last) on every h = 9 symbol. Recorded in the placer remarks and the structural test (`Dark(h-2,0) == (h != 9)`).
- With the encoder-side oracle at every stage (data codewords → final message → modules), the whole encode pipeline is now proven against two independent encoders before the public API or the zxing-cpp spot check exist; 5.6's spot check becomes a redundancy check, not the first proof.

**Benchmarks**

- Not applicable yet: internal component, not reachable from any public path; the rMQR E2E benchmark lands with 5.6 and the kernel/fast-path loop is the deferred follow-up.

### Phase 5.6, completed 2026-08-15

**Done**

- Decision, default fit strategy: **`MinimizeArea` stays the default.** `probe-rmqr` now measures both reference encoders' automatic choice with no version option: libzint and qrtool both pick R11x27 for 12 digits at M, R13x27 for 15, R11x77 for 100, exactly the fewest-modules rule, so the default keeps interoperability parity (a payload yields the same version everywhere) plus the printable-area argument. The surprise case (R11x27 (297) over the flatter R7x43 (301)) is documented in the generator XML docs and pinned by `RmQRCodeGeneratorUnitTest.DefaultFit_IsMinimizeArea_MatchingExternalEncoders`; users wanting the flattest symbol use `MinimizeHeight` or a fixed `RmQRHeight`. Design record Decisions table updated; the README example lands with the rendering surface (5.7).
- `src`: public `RmQRCodeGenerator` (frozen signatures: `CreateRmQRCode` string / span / span-destination, `GetRequiredBufferSize`) and `RmQRCodeCalculatedSize` (BufferSize / Width / Height / Version). Pipeline: `TextAnalyzer` → `RmQRVersionSelector` → `RmQRBinaryEncoder` → `RmQRCodewordEncoder` → `RmQRModulePlacer`; fixed stack budgets for data (152 B) and final message (233 B); the up-to-2,363-byte core is pooled (Standard QR policy) and never escapes; the span path with quiet zone 0 places straight into the destination.
- Tests (test-first, +144 on net8.0 + net10.0; full suite 8,438, 0 failed): `RmQRCodeGeneratorUnitTest` (default-strategy decision, class / span / `GetRequiredBufferSize` agreement for all 64 combos incl. quiet-zone offset, quiet zone 0 direct write, oracle symbol through the public path, UTF-8 without ECI vs the qrtool oracle shape, argument validation, span sizing hint, capacity messages for auto / fixed version / fixed height, empty text, Release-only zero allocation on the span path incl. UTF-8 and auto-fit).
- Encoder MVT gate: `tools/QRInteropFixtures -- spot-check-rmqr`: **256/256** symbols (32 versions × M/H × numeric / alphanumeric / byte / UTF-8 at capacity boundaries) decoded by zxing-cpp with matching raw bytes, `Version` and `EcLevel`.
- Benchmark `RmQREncodeEndToEnd` added (class + span variants, auto-fit, Standard QR v1 reference).

**Lessons learned**

- The default-policy question was an oracle question in disguise: measuring what the two reference encoders do automatically settled it in minutes and gave a stronger reason (cross-encoder version parity) than either aesthetic argument.
- The spot check's first "failure" was the tool comparing UTF-8 bytes for a Latin-1 payload (`é` is one ISO-8859-1 byte on the wire, which is what both we and zxing-cpp produce); external-decoder gates must encode expectations the way the encoder actually writes, per charset class.

**Benchmark (`RmQREncodeEndToEnd`, net10.0 Release, warmup 3 × 10 iterations, reference-shaped pipeline, no fast paths yet)**

| Benchmark | Mean | Allocated |
|---|---|---|
| RmQR_Numeric_R7x43_Encode | 1.05 µs | 112 B (result object) |
| RmQR_Alphanumeric_R11x59_Encode | 2.96 µs | 160 B |
| RmQR_Byte_R17x139_Encode | 14.4 µs | 368 B |
| RmQR_Numeric_R7x43_Encode (Span) | 1.12 µs | **0 B** |
| RmQR_Alphanumeric_R11x59_Encode (Span) | 3.03 µs | **0 B** |
| RmQR_Byte_R17x139_Encode (Span) | 14.8 µs | **0 B** |
| RmQR_Numeric_AutoFit_Encode (Span) | 1.26 µs | **0 B** |
| StandardQr_Numeric_V1_Encode (Span), reference | 2.42 µs | 0 B |

The per-module reference placer dominates (R17x139: 2,363 modules with a predicate call each); this is the baseline the placement / bit-stream fast-path follow-up is measured against. Standard / Micro QR benchmarks untouched (no shared file changed in this phase).

### Phase 5.7, completed 2026-08-15 (Phase 5 complete)

**Done**

- Shared rendering generalized (guarded by the image benchmarks below): `IModuleMatrixView` now exposes `Width` / `Height` / `CoreWidth` / `CoreHeight` (square views report both axes equal; new `RmQRMatrixView`), the run-merge and per-module draw loops iterate width × height; `QRImageLayout.CreateLayout(matrixWidth, matrixHeight, explicitSize, modulePixelSize, preserveAspectRatio, defaultSize)` (Standard / Micro keep the historical fill behavior, rMQR letterboxes on whole pixels; the square overload was removed); `QRCodeImageBuilderBase` hooks became `ResolveSymbol(out width, out height)` plus virtual `PreserveAspectRatio` / `GetDefaultCanvasSize` (both existing builders updated mechanically).
- `src`: `RmQRCodeImageBuilder` (frozen surface: typed `WithErrorCorrection` / `WithVersion` / `WithFitStrategy` / `WithHeight`, quiet zone 2, no icon / finder styling; static helpers whose `size` is the image width with the height following the symbol aspect ratio; default canvas 512 wide), `QRCodeRenderer.Render(canvas, area, RmQRCodeData, …)` (letterboxes into the area via `GetLetterboxedArea`, background over the whole area) and the two `SKCanvas.Render(RmQRCodeData, …)` extensions.
- Tests (+110 rendering tests; full suite 8,548, 0 failed): `RmQRCodeImageBuilderUnitTest` (module-to-pixel parity for all 32 versions × ECC, layout rules incl. letterbox in both orientations and whole-pixel centering, options and negatives, static helpers, SVG viewBox / crispEdges, renderer + extension agreement), `QrImageBuilderApiParityTest` now compares three builders (allowed differences: Standard-only `WithIcon` / `WithFinderPatternShape` / `WithEciMode`, rMQR-only `WithFitStrategy` / `WithHeight`).
- Playground: rMQR in the symbology selector (M/H, 32 versions, fit-strategy + fixed-height controls shown only for rMQR, quiet zone 2, finder / logo controls hidden, width-based image with aspect-ratio height, rMQR-aware stats and benchmark labels, share links carry the new fields); verified in the published Debug WASM build in-browser (R11x27 31×15 default, R7x43 with "Shortest", 512×120 image). BlazorWasm sample (symbology enum, controls, live SKCanvasView preview via the renderer overload, PNG/SVG export) and ConsoleApp patterns 27–28 (static one-liner; fixed height + styling, prints the R11x27-vs-R7x43 default-fit example) added.
- Docs: README (symbology table Encode ✅ / Decode planned, intro sample image, FAQ with the fit-strategy explanation, rMQR usage section), `docs/data-capacity.md` rMQR table (32 rows × M/H), spec map rendering rows link to code and tests, symbology status table.

**Lessons learned**

- One `preserveAspectRatio` flag plus a per-symbology default-canvas hook was enough to add rectangular layout without touching the square symbologies' behavior; the existing golden-pixel and builder tests stayed green unchanged, which is the real proof that "letterbox only for rMQR" is a pure addition.
- The static-helper sizing rule ("`size` is the width") needed a private width-only mode on the builder rather than a public `WithWidth`, so the fluent surface stays 1:1 with the other builders (the parity test enforces exactly that).
- Bulk-editing JavaScript with shell tools silently ate template literals (`${...}` → interpolated away); the in-browser check caught "rMQR Rx". Verify UI text in the running app, not just the diff.

**Benchmark delta (image paths, net10.0 Release, before = HEAD worktree, after = this change; allocations byte-identical everywhere)**

| Benchmark | Before | After |
|---|---|---|
| QrCodeImageEndToEnd Small_512px | 4.45 ms / 5.44 KB | 4.58 ms / 5.44 KB (re-measured, +3%) |
| QrCodeImageEndToEnd Small_2048px | 69.4 ms / 20.44 KB | 71.2 ms / 20.44 KB (+2.6%) |
| QrCodeImageEndToEnd Large_512px | 9.07 ms / 19.44 KB | 9.52 ms / 19.44 KB |
| QrCodeImageEndToEnd Large_2048px | 72.2 ms / 41.91 KB | 72.1 ms / 41.91 KB |
| MicroQRImageEndToEnd M2_512px | 4.91 ms / 5400 B | 4.59 ms / 5400 B |
| MicroQRImageEndToEnd M4_128px | 343 µs / 3856 B | 317 µs / 3856 B |
| MicroQRImageEndToEnd M4_ImageDecode_Span | 15.5 µs / 0 B | 15.0 µs / 0 B |

All within ms-scale PNG-encode noise (first-run Small_512 read +14% with a 0.9 ms error bar and re-measured at +3%); the draw-loop change is `data.Size` → `data.Width` / `data.Height` reads on struct views. rMQR image benchmarks (`RmQRImageEndToEnd`) land with the image decode path in Phase 7.
