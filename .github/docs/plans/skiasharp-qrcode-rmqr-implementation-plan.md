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
- Kanji is intentionally unsupported, matching Standard QR: callers encode Japanese text as
  UTF-8 in Byte mode instead. The decoder recognizes Kanji segments and returns
  `UnsupportedContent`; the tables keep the Kanji column only for specification completeness,
  not as a promise of later implementation. ECI is independent of Kanji: the decoder already
  parses it, and encoder emission of ISO-8859-1 / UTF-8 ECI is a required follow-up so Byte-mode
  text does not depend on a reader's UTF-8 heuristic.

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

Correct-and-clear first, then measured optimization, exactly the Micro QR sequence: reference per-module placer + naive bit-string references first, parity tests, then a fused fast path in a follow-up with kernel benchmarks and E2E in `SkiaSharp.QrCode.Benchmark`. Note for that follow-up: rows up to 139 modules no longer fit one ulong (Micro QR's packed-row and PEXT/PDEP tricks assumed ≤ 17), and there is no mask scoring at all, so the profile will differ; do not port Micro QR kernels blindly. Zero allocation on the span paths is a Phase 5 requirement, not a follow-up.

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

Exit (Phase 6): **met, see Progress log** (decoder MVT matrix rows: round trips, damage classes, 144-symbol corpus green; Standard QR decode benchmark flat).

### Phase 7, rMQR image detection

**7.1 Image decoder** (`RmQRImageDecoder`, `RmQRCodeDecoder.TryDecode(SKBitmap, …)` / `TryDecodeImage(luminance, …)`): pipeline as designed above.

- Tests: clean renders all 32 versions × 2 ECC; module pixel sizes 3-13; non-integer scale; translation; quiet zone 1/2/4; 90/180/270 rotation; mirror; inverted colors; JPEG q60 / low contrast / seeded noise; mild perspective (both finder-anchored corners); extreme aspect ratio set (R7x139, R17x27 does not exist, use R11x27 and R17x139) under every transform; negatives (Standard / Micro must not decode, blank, too small); committed fixture PNGs (both lineages) via the image path.
- Playground decode panel and generated-image self-check fall back to `RmQRCodeDecoder`; BlazorWasm sample likewise.
- Benchmark `RmQRImageEndToEnd` (render + PNG for R7x43 / R17x139, image decode span); Standard/Micro image benchmarks flat.

Exit (Phase 7): decoder MVT image rows and the representative degradation subset green; rMQR feature-complete; symbology status table says Shipped; progress log entry. Then the parent plan's Phase 8 (interop CI adds rMQR round trips both directions) and Phase 9 (physical device acceptance, rMQR subset per test strategy §11) gate the release.

### Follow-ups (after Phase 7, benchmark-driven)

The original x64 optimization rounds are complete. ARM64 is not at performance parity yet:
several shared kernels already dispatch to NEON, but the largest rMQR-specific matrix-decode
kernel and the bitmap luminance conversion still fall back to portable scalar code. The
priority below is an **evidence-based starting order**, not an ARM benchmark result: it uses
the x64 component profiles and current dispatch paths to identify the work most likely to
move ARM64 end to end. Phase N0 measures the real ARM shares before an intrinsic is accepted
or a later item is promoted.

#### Completed x64 encode/decode optimization record

These are already implemented and parity-pinned; the detailed benchmark records remain in
the chronological progress log below.

| Area | Completed work | Measured x64 result |
|---|---|---|
| Encode bit stream | Raw-local 64-bit writer; numeric SSSE3/SSE4.1, alphanumeric SSSE3/SSE4.1, and Latin-1 SSE2 segment writers | Kernel 1.3-7.5x; E2E only 1-7 %, confirming that placement was the larger lever |
| Encode placement | Cached per-version geometry/templates, fused expand + mask, AVX2/SSSE3 expansion, pair stores and irregular scatter | Span E2E 6-13x; R7x43/R11x59/R17x139 improved 83/88/92 % |
| Result packing / quiet zone | Shared AVX2/Vector128 bit packer and direct strided placement into the caller's quiet-zoned destination | Class encode improved 52-59 %; no intermediate core rental on the span path |
| Automatic fit | Best-first capacity tables and height bitmasks instead of scanning and comparing all 32 versions | Auto-fit span encode improved about 40 % after accounting for run drift |
| Matrix extraction | Cached walk/pair descriptors; AVX2 column-plane transpose + fast BMI2 PEXT/PDEP; branchless portable table walk elsewhere | Matrix-decode E2E 5.0-8.0x; image decode 14-15 % |
| Bitmap luminance | Exact BT.601 AVX2 conversion for opaque, transparent and premultiplied BGRA/RGBA/RGB888x | Conversion 14-30x; bitmap-decode E2E about 3.2x |
| Finder/image success path | SIMD dark masks, run-based scan, safe row stride with full-sweep retry, and Otsu reuse | Versus `main`: luminance-span decode 2.4-2.7x and bitmap decode 5.3-5.7x |
| Failure path | Vectorized inversion, sub-finder bounds rejection and row-wise early exit | Gradient failure 14 % faster and adversarial-noise failure 30 % faster versus `main` |

Architecture-neutral parts of those rounds already benefit ARM64: cached layouts, pair
stores/scatter, table-driven auto-fit, the portable extraction walk, safe finder stride and
retry, sub-finder guards, and Otsu reuse. The following shared primitives already have a
NEON/Vector128 tier and must be treated as controls rather than reimplemented:
`TextAnalyzer`, `EccBinaryEncoder`, `ModuleBitPacker`, `LuminanceInverter`, and finder/alignment
row-mask construction. In particular, the finder NEON fold was measured separately on Apple
M2 at 3.3-4.1x over the scalar walk; it is not an open rMQR ARM gap.

#### ARM64 / NEON priority

| Priority | Work | Why it is here | Promotion / stop rule |
|---|---|---|---|
| **N0 (done)** | ARM64 baseline and component attribution | There is no current rMQR ARM64 E2E record. Existing x64 numbers identify candidates but cannot rank ARM instruction costs or memory bandwidth. | Record encode, matrix decode, luminance-span image decode, bitmap decode, clean/error-corrected matrices, and both no-symbol cases on one pinned ARM64 machine before changing kernels. Add forced-portable vs automatic-dispatch kernel cases for every item below. |
| **N1 (shipped)** | NEON `LuminanceConverter` | ARM64 currently takes the per-pixel scalar BGRA/RGBA path. On x64 this conversion was 69 % of bitmap decode before AVX2 and the isolated kernel improved 14-30x, so this is the strongest real-input decode candidate and benefits all symbologies. | Ship only with byte-exact parity for all layouts/alpha modes and a material bitmap E2E win. If conversion is under 10 % of ARM bitmap decode, demote it behind N2. |
| **N2 (shipped)** | NEON rMQR codeword extraction | ARM64 cannot enter the AVX2+BMI2 bit-plane tier and uses the portable gather. Extraction was 70-91 % of matrix decode before the x64 tier; x64 matrix E2E improved 5-8x. This is the highest-priority rMQR-specific gap. | Compare at least a NEON column-plane builder plus scalar/SWAR bit compression against the current table walk; ARM has no PEXT/PDEP, so do not transliterate the x64 kernel. Keep a new tier only if it wins on small, narrow and largest symbols, not just R17x139. |
| **N3 (shipped)** | NEON RS syndrome computation in `EccBinaryDecoder` | Clean blocks always compute syndromes, and ARM64 lacked the x64 GFNI decoder tier. The gate asked for at least 5 % of ARM matrix decode; N0 showed it was essentially **all** of it (matrix decode is linear in syndrome iteration count at 3.07-3.15 ns/iteration across a 50x size range). | Passed by a wide margin. Shipped as `EccBinaryDecoder.Simd.Arm.cs`; results below. Berlekamp-Massey/Chien/Forney remain scalar, as the stop rule required. |
| **N4 (shipped)** | NEON rMQR placement expansion | The cached template and store/scatter design is already portable, but `ExpandBitsMasked` only has AVX2/SSSE3 vector tiers. The 16-module `TBL` + `CMTST` idiom already exists in the Micro QR placer and `ModuleBitPacker`, making this low-risk ARM work. | Port the proven idiom, add a named forced-NEON parity entry, and require an encode E2E win outside the ARM canary band. Do not rewrite the strided scatter unless profiling identifies it separately. |
| **N5 (shipped)** | Vector128/NEON rectangular grid sampling | rMQR `SampleGrid` was scalar while Standard QR already had a 128-bit row kernel. The gate asked for at least 5 % of ARM luminance-span decode: measured 5.8-7.0 % at R17x139 (5.8-7.0 us of 100 us), so it passed, narrowly. | Passed. Shipped as `SampleGridSimd128` + `SampleGridSimd128Affine`; results below. The premise that the Standard QR kernel could be shared was **wrong** — it multiplies by one reciprocal where rMQR divides twice, which is not bit-equivalent — so the exact-bytes requirement forced a separate implementation rather than a generalization. |
| **N6 (shipped, partial)** | Vector128 rMQR Latin-1 writer; numeric/alphanumeric measured and declined | ARM64 used the SWAR/table writers, but Latin-1's fallback was one accumulator update PER CHARACTER because the narrowing block was gated on `Sse2.IsSupported`. Post-N4 profiling: segment writing is 10.4 % of byte-mode encode E2E, 8.5 % for Latin-1 ECI, 5.4 % for alphanumeric and **0.3 % for numeric**. | Latin-1 passed and shipped (portable `Vector128.Narrow` tier, 9.2x on the writer). Alphanumeric won 19-28 % in the kernel but **did not transfer to E2E** and was reverted; numeric wins only on payloads the E2E set does not contain and loses on the one it does. Neither needed NEON, and neither cleared the acceptance threshold. UTF-8 stays in `Encoding.UTF8`. |

N1 and N2 may exchange order if N0 shows a workload dominated by matrix/luminance-span
input rather than `SKBitmap`; they are separate deliverables and must be benchmarked
separately. N3, N5 and N6 are explicitly conditional so that an easy-to-vectorize loop does
not outrank a measured bottleneck.

#### NEON work packages and acceptance gates

**N0, measurement contract**

- Use `net10.0` Release and the existing `RmQREncodeEndToEnd`,
  `RmQRDecodeEndToEnd`, and `RmQRImageEndToEnd` scenarios. Pin launch/warmup/iteration
  counts and record CPU, OS, runtime, power mode and allocation columns. Prefer ABBA ordering
  for before/after; include an unchanged NEON control such as finder scan or ECC encode.
- Add kernel cases that can force portable and NEON implementations in the same process.
  Cover R7x43, a width-27/narrow irregular layout, R11x59, and R17x139; do not infer the
  small-symbol result from the widest version.
- Add a matrix-decode case with correctable damage. The current clean E2E set exercises only
  syndrome generation and cannot rank the rest of the decoder failure/correction path.
- Capture JIT disassembly for each accepted kernel to prove that the intended AdvSimd
  instructions were emitted and that a helper call or bounds check did not replace the hot
  loop. Report kernel and E2E deltas; never promote from a kernel-only result.

**N1, exact luminance conversion**

- Add an AdvSimd-specific file and dispatch after the AVX2 check and before scalar. Process
  BGRA/RGBA/RGB888x in 128-bit blocks using table lookup/deinterleave plus widening
  multiply-add for the exact `77R + 150G + 29B` sum.
- Preserve the current alpha contracts: opaque/fully transparent straight alpha,
  premultiplied alpha for every value, and a correct partial-straight-alpha path. A scalar
  tail is acceptable; falling back for an entire common row is not accepted without an E2E
  comparison.
- Extend `LuminanceConverterParityTest` with a forced-NEON entry, poisoned destination tail,
  padded rows, widths around every vector boundary, all channel orders and alpha shapes.

**N2-N3, matrix decode**

- Give the ARM extractor its own named entry and parity test against
  `RmQRNaiveReference.ExtractInterleavedStream` for all 32 versions, all-light/all-dark
  non-canonical dark bytes, random grids, exact destination extent and overread bounds.
- Build 8 `ushort` column planes per NEON vector (forward and row-reversed together), then
  benchmark ARM-appropriate compression: scalar/SWAR compress of the planes, small
  mask-specific lookup tables, and a direct table walk. Select by measurement; ARM64 has no
  general PEXT/PDEP equivalent and the x64 pair descriptor is not automatically the right
  representation.
- For syndrome work, vectorize syndromes across lanes rather than codeword bytes, compare a
  bitsliced/shift-XOR GF multiply and table-lookup designs, and retain the scalar
  Berlekamp-Massey/Chien/Forney stages unless damaged-input profiling says otherwise.
  `EccBinaryDecoderKernelParityTest` must cover all ECC counts and codeword-length boundaries.

**N4-N6, encode and sampling**

- Reuse the existing Micro QR `TBL` + `CMTST` expansion shape for placement instead of
  inventing another bit order. Test every version/ECC, poisoned tails, strided destinations
  and quiet zones against `PlaceSymbolReference`.
- If sampling is promoted, extract the Standard QR 128-bit row kernel into a shared
  rectangular helper rather than copying it. Run both Standard QR and rMQR sampling parity
  suites and keep Standard/Micro image benchmarks flat.
- If segment writers are promoted, mirror `RmQRBinaryEncoderKernelParityTest` with a named
  forced-AdvSimd route across all accumulator phases and lengths. Byte-for-byte parity is
  insufficient when pending accumulator bits differ; compare the same logical writer state
  the x64 tests compare.

Every shipped NEON tier must preserve zero allocations, exact output/status parity, scalar
fallbacks for other targets, and x64 benchmark flatness. The default acceptance threshold is
an E2E improvement of at least 5 % with confidence intervals outside the unchanged-control
band; otherwise record the rejected kernel and keep the simpler portable path. Update the
rMQR spec map and append ARM64 before/after results here after each accepted work package.

#### ARM64 results: N0 baseline and N1 (completed 2026-08-19)

Machine: Apple M2 (8 cores), macOS 26.5.2, .NET SDK 10.0.301 / runtime 10.0.9, Arm64 RyuJIT,
`net10.0` Release, AC power. `HardwareIntrinsics=ArmBase+AdvSimd,AES,CRC32,DP,RDM,SHA1,SHA256
VectorSize=128` — note `DP` (UDOT), which N1 depends on. BenchmarkDotNet's
`DisassemblyDiagnoser` is Windows/Linux-only, so kernel selection was proved by
scenario-differencing (below) rather than by emitted assembly.

**N0, baseline.** `--warmupCount 3 --iterationCount 10 --launchCount 3`.

| Path | Scenario | Mean | Allocated |
|---|---|---:|---:|
| Encode (span) | R7x43 numeric / R11x59 alnum / R17x139 byte | 166.2 ns / 388.0 ns / 1,673.9 ns | 0 B |
| Encode (span) | R17x139 Latin-1 ECI / UTF-8 ECI / numeric auto-fit | 1,621.0 ns / 1,560.4 ns / 201.8 ns | 0 B |
| Matrix decode (span) | R7x43 / R11x59 / R17x139, clean | 279.6 ns / 2,367.6 ns / 14,326.4 ns | 0 B |
| Matrix decode (span) | R7x43 / R17x139, **correctable damage** | 554.9 ns / 27,158.7 ns | 0 B |
| Luminance-span image decode | R7x43 / R17x139 | 13.30 us / 87.64 us | 0 B |
| Bitmap decode | R7x43 / R17x139 | 52.37 us / 309.33 us | 152 B / 440 B |
| No-symbol failure | noise / gradient 1144x168 | 8,421.7 us / 233.0 us | 0 B |

Scale reference on the same machine: Standard QR v1 numeric is 1,984.5 ns to encode and
1,228.0 ns to decode, i.e. rMQR R7x43 encodes about 12x faster and decodes about 4x faster
than the smallest Standard QR.

Measurement note: an earlier pass at this table was taken while another build shared the
machine and reported R17x139 encode as 2,235 ns +/- 306 (14 % RatioSD). The re-run above is
+/- 17 ns (1 %). Treat any ARM64 row whose Error exceeds ~2 % as contaminated and re-run it;
do not compare across runs.

Two things the baseline settled:

- **Luminance conversion was 71-74 % of ARM bitmap decode** (bitmap minus luminance-span:
  39.07 us of 52.37, 221.69 us of 309.33; both ~1.33 ns/px, so it is the pixel loop and not
  bitmap overhead). N1's demotion rule was "under 10 % → demote behind N2"; it passed by a
  wide margin and N1 stayed first.
- **Correctable damage roughly doubles matrix decode** (279.6 → 554.9 ns = 1.98x,
  14.33 → 27.16 us = 1.90x). The
  clean-only set could not rank the correction path; `RmQR_*_Corrected_Decode` was added to
  `RmQRDecodeEndToEnd` to close that gap. This is the input N3 needs, and it says the
  Berlekamp-Massey/Chien/Forney stages are about half of a damaged decode.

**N1, NEON `LuminanceConverter`: shipped.** Kernel search ran seven rounds in the private
MicroBenchmarks repo (16 variants, byte-exact gate of 1,836 cases per variant per round);
the full log with refutations lives there. Shipped as
`Internals/ImageDecoders/LuminanceConverter.Simd.Arm.cs`, dispatched from `ConvertRgba`
after the AVX2 check and before scalar.

Design, and why it is not a transliteration of the AVX2 kernel:

- **UDOT, not shuffle + multiply-add.** `Dp.DotProduct` computes `77R + 150G + 29B + 0*A`
  for four whole pixels in one instruction with no deinterleave, and weighting alpha 0 makes
  Rgb888x's padding byte free. An `LD4`-based port of the x64 plane approach was measured and
  lost by 1.2-1.5x.
- **Alpha tested per row, not per block.** Rgb888x and Bgra-opaque run identical arithmetic
  and differ only by that test, which measured it at 27 % of the kernel (a cross-lane reduce
  plus a vector-to-GPR move every 16 px). Rows are converted optimistically while pixels are
  ANDed into a vector accumulator; one cross-lane test per row decides. The mode is sticky, so
  an image with alpha wastes one pass, not one per row.
- **Every alpha shape stays vectorized.** Fully transparent composites to white (luminance
  255) by replacement; partial straight alpha uses `c' = 255 - ceil((255 - c)*a / 255)`, exact
  in 16-bit lanes; premultiplied collapses to an add before the shift. Blocks are classified
  and the classification is sticky per row, which took both the transparent-background win and
  the partial-alpha win that first looked mutually exclusive.
- **No `unsafe`.** `LD4` has no ref-taking overload, so the composite builds planes from two
  UZP rounds instead — measured identical on the only scenario that runs it. The library's
  no-`AllowUnsafeBlocks` policy is unchanged.
- **Row modes are separate methods, and the composite is `NoInlining`.** Both are
  load-bearing, not style: fusing the row modes measured 2.3x slower on inputs where the fused
  code never ran, and inlining the composite cost 52 % on a path that never calls it. Code
  sitting in this loop costs as much as code that runs.

E2E (`RmQRImageEndToEnd`, identical benchmark binary, library swapped between runs):

| Scenario | before | after | change |
|---|---:|---:|---:|
| R7x43_BitmapDecode | 52.37 us | **16.33 us** | **3.21x** |
| R17x139_BitmapDecode | 309.33 us | **101.95 us** | **3.03x** |
| R7x43 / R17x139 ImageDecode_Span (control) | 13.30 / 87.64 us | 13.33 / 87.64 us | flat |
| R7x43_512px / R17x139_1024px render (control) | 1058.07 / 2748.07 us | 1057.92 / 2749.18 us | flat |
| NoSymbol noise / gradient (control) | 8421.72 / 232.99 us | 8436.37 / 232.72 us | +0.17 % / flat |

Every unchanged control is flat within 0.2 %. The conversion component alone improved
**13.0x (R7x43)** and **15.5x (R17x139)**, matching the kernel benchmark's 14-16x, and
conversion fell from 71-74 % to 14-18 % of bitmap decode. Allocations unchanged (the 152/440 B
are the decoded strings). Acceptance gate (≥ 5 % E2E, outside the control band): passed.

`LuminanceConverterParityTest` previously skipped entirely on ARM64. It now runs the vector
tier on whichever ISA the machine has, with widths straddling the NEON block and its
overlapping tail (16/17/20/24 — width 20 makes the final block redo 12 already-written
pixels), and the over-write test covers all four alpha shapes because the NEON tier has four
row modes with separate tail arithmetic. Full suite green: 5,685 tests, 0 failed.

Next per the queue was **N2 (NEON rMQR codeword extraction)**. It was withdrawn on an E2E
reading that later proved to have measured a pre-N3 library; re-measured on top of N3 it
passes its gate and is shipped (recorded after N3 below), so the queue order N2 then N3 held
after all — only the evidence arrived out of order.

**Correction to the N0 reading.** The sentence originally here claimed that "the correction
stages account for the whole 12.8 us difference while syndrome generation runs in both
cases", and used that to argue N3 was not the lever on damaged symbols. That is wrong:
`TryCorrect` recomputes the syndromes after applying corrections to guard against silent
miscorrection, so a corrected block runs the syndrome pass **twice**. Roughly 10 us of the
12.8 us gap is that second pass. N3 therefore pays on damaged blocks harder than on clean
ones, which is exactly what the measured result shows (6.47x corrected vs 4.10x clean at
R17x139).

#### ARM64 results: N3 (completed 2026-08-20)

Same machine and contract as N0/N1. Kernel search ran seven rounds in the private
MicroBenchmarks repo (24 variants; the final correctness gate is 28,704 comparisons per
run, covering every ECC count 1-30 x lengths straddling each unroll boundary x
all-zero/all-dark/random seeds x a poisoned destination). Full log with refutations lives
there as `MICRO_OPTIMIZATION_EccDecodeArm.md`.

**Why the gate passed on N0 data alone.** Syndrome inner-loop iterations versus the N0
matrix-decode measurements:

| Symbol | iterations (blocks x cw x ecc) | matrix decode | ns / iteration |
|---|---:|---:|---:|
| R7x43 M | 1 x 13 x 7 = 91 | 279.6 ns | 3.07 |
| R11x59 M | 1 x 47 x 16 = 752 | 2,367.6 ns | 3.15 |
| R17x139 M | 4 x 58 x 20 = 4,640 | 14,326.4 ns | 3.09 |

A constant of ~3.1 ns/iteration across a 50x size range means ARM matrix decode was
essentially this one kernel. On x64 the same item was under 1 % *after* its GFNI tier —
the ARM position was the pre-GFNI one.

**Design, and why it is not a port of the GFNI kernel.** GF2P8MULB multiplies every lane by
a per-lane constant in one instruction but is hardwired to the AES polynomial, so the x64
kernel spends its design on a field isomorphism to borrow it. NEON has no such instruction,
but PMULL/PMUL carry no fixed modulus, so ARM needs **no isomorphism at all** and instead
pays for the reduction mod 0x11D itself. The shipped step is:

```
acc' = Reduce(PMULL(acc, alpha^4i)) ^ T3[c0] ^ T2[c1] ^ T1[c2] ^ broadcast(c3)
```

- **Table reads for the data terms.** `c * alpha^(k*i)` depends only on the byte and the
  step, so it is read as a ready-made 32-byte vector: three loads + three XORs replace six
  PMULL + six EOR. The tables cost 24 KB, built lazily and published with a release store.
  A 3 KB nibble-split alternative was measured and lost by 20-78 %, so the footprint buys
  real work — but that verdict comes from a 128 KB L1D, which is why the scalar tier was
  also improved rather than left behind.
- **Reduction by table lookup.** A product's high half has degree <= 6, so splitting it into
  nibbles indexes two 16-entry tables of already-reduced contributions:
  `UZP -> AND -> TBL -> EOR` instead of `UZP -> PMULL -> UZP -> PMUL -> EOR`.
- **The reduction tables are hoisted into locals.** Load-bearing, not style: the JIT does not
  CSE `Vector128.Create` over a static array across the loop body, so leaving them inline
  makes every reduction pay two extra loads — up to 20 % of the kernel.
- **Blocks needing <= 16 syndromes drive one accumulator group.** Halving the vector work
  buys only ~20 % of the time, which is the clearest evidence the loop is bound by the
  `acc -> PMULL -> reduce -> EOR` chain: the second group ran mostly in the first's stall slots.
- **Scalar tier upgraded too.** `ComputeSyndromesScalar` now runs four syndromes per pass
  over the codeword (2.2-2.9x on large blocks, no ISA dependency). This is the path
  netstandard2.0/2.1 and any non-GFNI, non-AdvSimd CPU takes.

Kernel, final round, all variants in one run (`--launchCount 3`), versus the shipped scalar:
**11.2x (R7x43 M), 15.2x (R7x43 H), 32.5x (R11x59 M), 43.1x (R17x139 H), 35.0x (R17x139 M),
61.3x (Standard QR v40-L)**, zero allocations.

E2E (`RmQRDecodeEndToEnd`, identical benchmark binary, library stashed between runs,
`IterationCount=15 LaunchCount=3 WarmupCount=3`):

| Scenario | before | after | change |
|---|---:|---:|---:|
| R7x43 clean (span) | 322.4 ns | **202.3 ns** | **1.59x** |
| R11x59 clean (span) | 2,757.2 ns | **812.5 ns** | **3.39x** |
| R17x139 clean (span) | 16,759.3 ns | **4,087.1 ns** | **4.10x** |
| R7x43 corrected (span) | 632.6 ns | **399.8 ns** | **1.58x** |
| R17x139 corrected (span) | 31,732.1 ns | **4,904.9 ns** | **6.47x** |
| R17x139 clean (string) | 16,816.9 ns | **3,509.8 ns** | **4.79x** |
| Standard QR v1 clean (span) | 1,585.8 ns | **1,103.3 ns** | **1.44x** |
| R7x43 encode (span, control) | 208.4 ns | 194.2 ns | -6.8 % (drift) |

Allocations unchanged (48 B / 328 B are the decoded strings). Acceptance gate (>= 5 % E2E,
outside the unchanged-control band): passed — the control moved 6.8 %, every decode scenario
moved 30-85 %. Corrected decode improves more than clean, confirming the correction above:
the verification pass is a second syndrome computation.

**Measurement note, and a process change.** This machine drifts **+16 % to +50 % between runs
on byte-identical code** at kernel scale (R11x59 measured 56.70 ns and 84.95 ns for the same
variant in consecutive rounds). One mid-loop conclusion was drawn from a cross-run comparison
and had to be withdrawn: a 2-chain split appeared to win 40 % in one round and lose 12 % in
the next, and the mechanism inferred from that gap did not exist — measured in a single run,
the two reduction strategies land within a few percent at equal chain counts. Decision rounds
now require `--launchCount 3`, all candidates in one run, and a byte-identical **noise canary**
variant; the canary puts this harness's true resolution at 13.3 % at 10 ns and 0.1-4.8 % at
60-300 ns. Do not compare numbers across runs.

**Deliberately not shipped**, both recorded with numbers in the private log: a 2-chain split
(did not reproduce; inside the drift band) and a 4-chain split (real, -21.5 %, but only at
149 codewords — no rMQR block exceeds 68, and it loses badly at rMQR sizes). The latter is a
Standard-QR-only opportunity; the log carries a stride-16 redesign needing 160 B of baked
constants instead of the 8 KB table the measured version used.

#### ARM64 results: N4, NEON rMQR placement (completed 2026-08-20, after N2)

Kernel search ran six rounds in the private harness (`RmQrPlaceArmBenchmark`, 13 variants and
2 phase probes, byte-identical gate of 4,480 placements per round against the per-module
reference — all 32 versions × 2 ECC × all-zero / all-one / two pseudo-random / over-long
messages on a poisoned core). Apple M2, net10.0 Release.

**The work package named the wrong half.** N4 was written as "port the Micro QR `TBL` + `CMTST`
idiom to `ExpandBitsMasked`", on the reasoning that ARM64 had no vector tier there at all. Two
phase probes (template copy only; copy + expand) sized the phases before any kernel was written:

| Scenario | template copy | expand (scalar) | store pass |
|---|---|---|---|
| R7x43_M | 9 % | 43 % | 48 % |
| R11x59_M | 5 % | 46 % | 49 % |
| R13x99_H | 4 % | 41 % | 55 % |
| R17x139_M | 3 % | 38 % | 59 % |

The missing expand tier was real and worth 5.2x on its own phase, but the store pass was the
larger half at every size. That is the "profiling identifies it separately" condition this
document set for touching the scatter, so the remaining five rounds went there.

**Shipped (`RmQRModulePlacer.Simd.Arm.cs` + an AdvSimd tier in `ExpandBitsMasked`):**

- *Expand*: the planned `TBL` + `CMTST` idiom, 2 message bytes → 16 module bytes per step.
  `CMTST` (0xFF where `(a & b) != 0`) is the per-lane bit test x86 lacks, so SSSE3's AND +
  compare-equal pair is one instruction. Phase measurement at R17x139: 451 → 95 ns, 4.7x.
- *Store*: four consecutive clean column pairs — eight consecutive columns — are transposed in
  registers (`UZP1`/`UZP2` separates each pair's two column vectors, `TBL` flips the
  upward-walked ones into row order, a three-stage `ZIP` network puts symbol row *i* in 64-bit
  lane *i & 1* of vector *i / 2*), so one symbol row is one 8-byte store: **one store per eight
  modules instead of one per two**.
- *Segmentation*: the portable tier's per-pair "is this whole column pair clean?" test is
  replaced by per-row runs. A single function module anywhere in a pair used to send the whole
  pair to byte scatter; on R11x27 that is 56 % of the symbol, of which **91.8 % actually sits in
  stretches of rows where both columns are ordinary data**. Only 4-12 % of a symbol is
  genuinely isolated and still scattered a byte at a time.
- *Portable side effect*: the scalar expand tail became branch-free SWAR
  (`b * 0x8040201008040201 >> 7 & 0x0101…`, one source bit per product bit so no carries), one
  multiply and one 8-byte store per message byte instead of eight loads, shifts and byte
  stores. This is what netstandard2.0 and non-SIMD targets run for the whole message; on x64 it
  only covers the final odd byte after SSSE3.

Kernel, versus the shipped portable path, from the final confirmation run (all variants in
one process, 3 warmup / 15 iterations):

| Scenario | before | after | ratio | canary |
|---|---:|---:|---:|---:|
| R7x43_M | 104.3 ns | 53.0 ns | 0.51 | 0.96 |
| R11x27_M | 114.1 ns | 64.4 ns | 0.57 | **1.14** |
| R11x59_M | 228.4 ns | 100.1 ns | 0.44 | 1.00 |
| R13x99_H | 515.8 ns | 183.8 ns | 0.36 | 1.03 |
| R17x139_M | 984.6 ns | 287.4 ns | 0.29 | 1.00 |

R11x27's byte-identical canary drifted 14 % in that run, so read that row with a ±14 % band;
an independent run (canary 0.99-1.04) put the same five at 0.56 / 0.68 / 0.50 / 0.34 / 0.29.
The defensible claim across both is 3.4x at the largest symbol, 2.3-2.8x in the middle and
1.5-2.0x at the smallest. The winner leads every scenario, so no size switch is needed.

Phase split at R17x139 after the change: template copy 33 ns, NEON expand 95 ns, transpose
block stores 73 ns, row runs + isolated modules 86 ns.

Encode E2E through the public API (`RmQREncodeEndToEnd`, medians of four alternating process
launches per side, because a single pair moved the untouched Standard QR control by 17 %):

| Benchmark | before | after | delta |
|---|---|---|---|
| RmQR_Numeric_R7x43_Encode | 224.4 ns | 181.7 ns | −19.0 % |
| RmQR_Alphanumeric_R11x59_Encode | 476.4 ns | 370.4 ns | −22.2 % |
| RmQR_Byte_R17x139_Encode | 2,079.4 ns | 1,328.4 ns | −36.1 % |
| RmQR_Numeric_R7x43_Encode (Span) | 207.9 ns | 164.4 ns | −20.9 % |
| RmQR_Alphanumeric_R11x59_Encode (Span) | 481.3 ns | 333.5 ns | −30.7 % |
| RmQR_Byte_R17x139_Encode (Span) | 2,050.6 ns | 1,170.1 ns | −42.9 % |
| RmQR_Latin1_ECI_R17x139_Encode (Span) | 1,966.6 ns | 1,143.3 ns | −41.9 % |
| RmQR_UTF8_ECI_R17x139_Encode (Span) | 1,976.4 ns | 1,045.4 ns | −47.1 % |
| RmQR_Numeric_AutoFit_Encode (Span) | 255.3 ns | 211.5 ns | −17.2 % |
| StandardQr_Numeric_V1_Encode (Span), unchanged control | 2,582.1 ns | 2,466.6 ns | −4.5 % |

The control's −4.5 % is the residual cross-run drift, so the honest rMQR figures are roughly
15-43 %. The E2E deltas are consistent with the kernel deltas: placement is now about half of
the encode pipeline rather than nearly all of it, so a 71 % kernel saving at R17x139 shows up
as 43 % E2E, and the smallest symbol (44 % kernel saving, more fixed pipeline cost around it)
as 21 %. Allocations unchanged (class results are the returned objects; span paths 0 B). The
one-time per-version tables gain the block/run/single segmentation, built only where
`AdvSimd.Arm64.IsSupported` — a JIT constant, so x64 neither builds nor carries them.

**Two designs were measured and rejected, both about coverage:**

- *Generalized blocks* (a block is four adjacent pairs × their common data row range, not four
  fully clean pairs) raised in-block coverage from 77 % to 89 % at R17x139 and from 0 % to
  52 % at R11x27, and **lost at every size**. The `ZIP` network costs a fixed ~40 instructions
  per block whatever its height, so a 4-row block pays a 15-row block's price for a quarter of
  the modules and takes those modules away from the cheaper run path. Raising the minimum block
  height to 8 rows recovered a tie at the largest symbol and still lost everywhere else.
- *Branch-free split lists* (per-version clean-up / clean-down / irregular lists, removing both
  per-pair branches) lost to the plain kernel at R17x139. Splitting the pairs out of walk order
  destroys the bit array's read locality, and at 1,858 bytes that costs more than the branches
  saved. Branch count was not the right objective.

Also rejected: a wider expand step (4 message bytes per iteration) both via `CreateScalarUnsafe`
(loses — the GPR→SIMD `FMOV` is paid every iteration, where `LD1R` replicates straight from
memory) and via two independent `LD1R` broadcasts (ties — the expand is not load-bound).

The shipped kernel is deliberately the ref-based form: `UZP1`/`UZP2` instead of `LD2` and 64-bit
half-stores instead of `ST1 {v.d}[i]`, because the library does not set `AllowUnsafeBlocks` and
`LuminanceConverter.Simd.Arm.cs` already records that decision. The pointer form measured
somewhat better in the private harness; revisit only as a deliberate library-wide change.

`RmQRModulePlacerParityTest` gained a forced-ARM64 entry (`PlaceKernel.Neon`, mirroring the
matrix decoder's `ExtractKernel` selector) covering every version × ECC × message shape through
both the tight and the quiet-zoned strided destination, with the quiet-zone bytes asserted
untouched — the strided path derives its own row pitch, so a tier that is byte-exact at
stride == width can still be wrong at stride > width. Full suite green: 5,619 passed, 0 failed.

#### ARM64 results: N2, NEON rMQR codeword extraction (completed 2026-08-20, after N3)

**Shipped; acceptance gate passed.** Seven rounds in the private MicroBenchmarks repo
(17 variants, byte-exact gate of all 32 versions x 8 grid shapes per variant per round);
full log with refutations lives there as `MICRO_OPTIMIZATION_RmQrExtractArm.md`.

Design, and why it is not a transliteration of the AVX2+BMI2 kernel:

- **The deposit step is designed away, not emulated.** NEON has neither PEXT nor PDEP, so
  instead of one bit plane per column plus a deposit, one 32-bit lane holds a whole column
  PAIR: bit 2j+1 is the right column and bit 2j the left one, j counting data rows in walk
  order. The walk alternates between a pair's two columns on every row, so that word already
  *is* the pair's output field with the function modules still in it, and the pair operation
  collapses to a single PEXT-shaped compress with no deposit at all.
- **The compress is runs, and the runs are one flat table.** A pair's data mask is a few runs
  of consecutive bits (function modules come from rectangular blocks, not scattered modules):
  a pair averages 1.0-2.3 runs but the worst has 5-11, so unrolling every pair to the version's
  worst case measured **2.4x slower than the portable walk** on small symbols. One flat run
  stream for the whole symbol removed that entirely.
- **No plane buffer.** Walk order is descending pair index and a block holds four consecutive
  pairs, so blocks are transposed backwards and each block's pairs are consumed straight out of
  the vector that produced them. That deleted a 320-byte stackalloc whose zeroing alone was
  ~5 ns, i.e. ~10 % of a small symbol.
- **Step width follows the symbol width.** 32 columns per row-step while four whole blocks
  remain, 16 afterwards. A fixed 32-column step is 7.5 % faster at R17x139 but **18 % slower at
  R7x43**, where 43 columns round up to 64 — a granularity cliff, removed rather than accepted.
- **The row-reversed word is produced once per block** (RBIT + REV32 + one adjacent-bit swap)
  instead of maintaining a second accumulator through every row.
- **The drain branch was kept on purpose.** Making it branchless measured 13-22 % slower: run
  lengths are per-version constants, so the drain pattern is periodic and the predictor learns
  it. Data-dependent does not mean unpredictable.

Kernel, Apple M2, 3 process launches, versus the portable table walk ARM64 ran before:

| Scenario | bits | portable | pair-plane | ratio |
|---|---:|---:|---:|---:|
| R7x43 | 104 | 50.40 ns | 46.28 ns | 0.92 |
| R11x27 | 120 | 56.98 ns | 53.20 ns | 0.94 |
| R11x59 | 376 | 194.23 ns | 106.64 ns | 0.55 |
| R7x139 | 544 | 261.01 ns | 144.56 ns | 0.55 |
| R13x99 | 904 | 448.58 ns | 162.69 ns | 0.36 |
| R17x139 | 1856 | 864.38 ns | 258.33 ns | 0.30 |

R7x139 was added specifically to separate "small" from "short": a symbol with only 5 data rows
still wins 0.55x when it is wide, so the weak cases are about total bits, not height, and no
height guard is needed. Per-bit cost is a smooth 0.45 -> 0.14 ns curve over a ~35 ns fixed
floor; the portable walk is ~0.48 ns/bit with no fixed cost, so the two cross at ~100 bits.
That crossing, not a dispatch boundary, is why the two smallest symbols only win 6-8 %.

E2E (`RmQRDecodeEndToEnd`, identical benchmark binary, dispatch flag toggled between runs,
`--launchCount 3`, on top of N3):

| Scenario | before | after | change |
|---|---:|---:|---:|
| RmQR_Byte_R17x139 (Span) | 3,425.0 ns | **2,793.6 ns** | **-18.4 %** |
| RmQR_Byte_R17x139 (string) | 3,454.8 ns | 2,828.4 ns | -18.1 % |
| RmQR_Byte_R17x139_Corrected (Span) | 4,929.5 ns | 4,372.7 ns | -11.3 % |
| RmQR_Alphanumeric_R11x59 (Span) | 767.9 ns | **682.1 ns** | **-11.2 %** |
| RmQR_Numeric_R7x43 (Span) | 211.5 ns | 214.5 ns | +1.4 % (inside noise) |
| StandardQr_Numeric_V1 (control) | 1,106.0 ns | 1,120.0 ns | +1.3 % (flat) |

Acceptance gate (>= 5 % E2E, outside the control band): **passed** at R17x139 and R11x59.
Span paths stay at 0 B (the 48/328 B are the decoded strings). R7x43 is flat by Amdahl, not by
failure: the kernel difference there is 4 ns against a 211 ns decode. The E2E deltas match the
kernel deltas almost exactly (-631 vs -606 ns at R17x139, -86 vs -87 ns at R11x59), which is
what proves the tier is actually being taken.

**Why this was withdrawn once, and the process fix.** The first E2E pass reported extraction as
~6 % of matrix decode with a ~4.3 % ceiling and rejected the kernel. Its baseline was 16.5 us at
R17x139 where the correct figure is 3.4 us: another session was building into the same
repository, so that run measured a **pre-N3 library**. Two rules follow. First, the Amdahl
denominator moves as earlier packages land — N3 took the RS side down and lifted extraction
from ~6 % to ~25 % of matrix decode — so an E2E verdict is only valid against today's
dependencies, and a rejected kernel must be re-measured after any package that shrinks its
denominator. Second, a flat control is not sufficient evidence of a clean run: also check that
the E2E delta matches the kernel delta, because a contaminated run can measure the wrong binary
rather than merely a noisy one.

`RmQRExtractCodewordsParityTest` now covers three tiers through an `ExtractKernel` selector
(portable, x64 bit-plane, ARM64 pair-plane) across all 32 versions x 8 grid shapes with a
poisoned destination, and the overread-bound test runs for both vector transposes. Full suite
green: 5,691 tests, 0 failed.

**N4, N5 and N6 are all resolved; results below.** The ARM64 queue is empty: N6 was the
last item, and it shipped only its Latin-1 half — the alphanumeric and numeric writers were
measured against the post-N4 pipeline (as that gate required, since N4 had shrunk the encode
denominator by 17-43 %) and declined on the evidence. Reopen ARM work only with a new
profile naming a different mechanism.

Items deliberately below the NEON queue: Otsu histogramming (serial histogram updates and
already near its measured per-pixel floor), sub-finder/perspective search (branchy,
data-dependent and failure-path dominated), rendering/PNG (Skia/native-code dominated), and
the 1.3-8.3 ns version selector. Revisit only with a new ARM profile naming a different
mechanism.

Feature work remains separate from performance work:

- **Completed 2026-08-18: rMQR ECI emission.** Mirrors Standard QR's charset policy: ASCII needs no ECI;
  explicit or auto-detected ISO-8859-1 emits assignment 3; explicit or auto-detected UTF-8
  emits assignment 26. In rMQR this is an 11-bit prefix for the supported assignments
  (`111` mode + 8-bit designator) before the Byte segment. Add the ECI overhead to required-bit
  and version-selection calculations, expose `EciMode` without breaking existing positional
  calls, and keeps class/span/`GetRequiredBufferSize` results identical. Capacity boundaries,
  explicit/default selection, exact bit streams, external-reader interop and zero allocation are pinned.
- **Intentionally unsupported: Kanji mode.** It is a separate 13-bit Shift JIS-based data mode,
  not a prerequisite for ECI. As with Standard QR, encoding Japanese text uses Byte mode with
  UTF-8 ECI; incoming Kanji segments continue to return `UnsupportedContent`. Reconsider only
  as a cross-symbology policy change backed by concrete demand, not as part of rMQR completion.

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
- The static-helper sizing rule ("`size` is the width") was first kept as a private width-only mode on the builder so the fluent surface stayed 1:1 with the other builders; the 2026-08-16 review made it the public `WithWidth` (rMQR-only member in the parity allow-list, next to `WithFitStrategy` / `WithHeight`) because the Playground and the Blazor sample had to reimplement it, and their reimplementation (`WithSize` on the rounded height) hit the double-letterbox defect below.
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

### Phase 6 (6.2-6.4), completed 2026-08-15

**Done**

- 6.2 `Internals/RmQr/RmQRFormatInformationDecoder`: 64 candidates per side (finder / sub-finder XOR masks), `TryDecodeCopy` (≤ 3 bit errors, BCH(18,6) minimum distance ≥ 7) and `TryDecode` over both copies (closer valid copy wins, ties → finder side; the 2026-08-16 review made it dimension-aware, see the review entry below).
- 6.3 `Internals/RmQr/RmQRMatrixDecoder` (version from dimensions, both format copies with the version cross-check, inverse zigzag + fixed unmask through the placer's own predicate / mask, block deinterleave, per-block RS via the shared `EccBinaryDecoder`, fixed stack budgets pinned by test) and `Internals/RmQr/RmQRBinaryDecoder` (3-bit modes, per-version count widths, terminator `000`, ECI parsed and mapped, Kanji → `UnsupportedContent`, reserved modes → `InvalidBitstream`, payloads through the shared `SegmentDecoders`). The ECI designator reader was lifted from `QRBinaryDecoder` into `SegmentDecoders` (second consumer; Standard QR decode benchmark flat, see below).
- 6.4 public `RmQRCodeDecoder` (frozen surface: `RmQRCodeData` ×2, module matrix with width + height as string and zero-allocation span-destination overloads, `GetMaxDecodedLength`) and `RmQRCodeDecodeInfo` (Status / Version / EccLevel / ErrorsCorrected). Quiet-zone stripping by the dark bounding box (rMQR has dark modules at all four core corners and timing on every edge), so asymmetric borders work too. Design record `specs/rmqr-decoder.md` written.
- Decision: no misdecode-protection cap (full RS strength ⌊ecc/2⌋ per block, as the Standard QR decoder and zxing-cpp); the ISO/IEC 23941 text could not be consulted, recorded as an open decision in the decoder record and spec map.
- Tests (test-first, +726 on net8.0 + net10.0; full suite 9,274, 0 failed): `RmQRFormatInformationDecoderUnitTest` (both sides exhaustively over 2^18 vs naive nearest, arbitration classes), `RmQRBinaryDecoderUnitTest` (oracle golden, round trips, ECI UTF-8 / ISO-8859-1 / unsupported, Kanji, reserved modes, truncations, terminator vs empty segments), `RmQRCodeDecoderRoundTripTest` (all 32 × M/H × modes × quiet zones through every overload, asymmetric padding, stack budgets, `GetMaxDecodedLength`, non-rMQR rejection, Release-only zero allocation), `RmQRCodeDecoderRobustnessTest` (t codewords per block corrected in every block of all 64 combos, t + 1 in one block never decoding cleanly, format copies destroyed / both within / both beyond, format-vs-dimension contradiction, remainder bits), `RmQrFixtureTest.Decode_MatrixFixture` (all 144 external symbols: payload / version / ECC, `ErrorsCorrected` 0 for libzint and ≤ 1 for qrtool, plus a PNG-sampled re-decode).
- Benchmark `RmQRDecodeEndToEnd` added; README (Decode ✅ matrix, decode example), docs index, spec map, symbology status, fixture record updated.

**Lessons learned**

- Two hand-built "malformed stream" cases were initially wrong (a stream starting with `000` is a valid terminator; a 5-byte claim fits in 42 bits); malformed-input tests must be constructed against the exact framing rules, not by intuition, and both were caught by running them.
- The corpus's qrtool tail defect doubles as a robustness fixture (`ErrorsCorrected ≤ 1`), and the format-copy redundancy makes single-copy destruction a free positive class.

**Benchmark delta (`QRCodeDecodeEndToEnd`, net10.0 Release, warmup 3 × 10 iterations, before = HEAD worktree, after = this change; allocations identical)**

| Benchmark | Before | After |
|---|---|---|
| QR_Numeric_V1_L_Decode (Span) | 918 ns | 890 ns |
| QR_Alphanumeric_V1_M_Decode (Span) | 1,074 ns | 1,056 ns |
| QR_Byte_Url_V6_M_Decode (Span) | 5,012 ns | 4,978 ns |
| QR_Byte_V40_L_Decode (Span) | 129.1 µs | 132.8 µs |
| QR_Byte_V40_H_Decode (Span) | 101.0 µs | 98.2 µs |
| Image_Byte_Url_V6_M_Decode (Span) | 38.4 µs | 37.8 µs |
| MicroQR_Numeric_M2_Decode (Span) | 287 ns | 279 ns |

All within ±3% (the ECI reader is only reached on ECI segments; the lift changed nothing on the hot path).

**New rMQR decode baseline (`RmQRDecodeEndToEnd`, same job)**

| Benchmark | Mean | Allocated |
|---|---|---|
| RmQR_Numeric_R7x43_Decode (Span) | 833 ns | **0 B** |
| RmQR_Alphanumeric_R11x59_Decode (Span) | 2.88 µs | **0 B** |
| RmQR_Byte_R17x139_Decode (Span) | 14.6 µs | **0 B** |
| RmQR_Numeric_R7x43_Decode (string) | 812 ns | 48 B (result string) |
| RmQR_Byte_R17x139_Decode (string) | 15.8 µs | 328 B (result string) |
| StandardQr_Numeric_V1_Decode (Span), reference | 852 ns | 0 B |

The per-module predicate walk dominates (as on the encode side); it is the baseline for the shared placer / extractor fast-path follow-up.

### Phase 7 (7.1), completed 2026-08-16

**Done**

- `Internals/RmQr/RmQRImageDecoder`: shared Otsu + finder candidates → local frames around the finder (4 right angles × transpose for mirroring; then the angular finder-axis sweep for arbitrary rotation, shared) → **finder-side format copy read first** (18 modules next to the finder) → version → width × height → **sub-finder located** near its predicted corner (5×5 template on a half-module lattice with three column-axis leans, ring order, first perfect match wins, then center-module midpoint refinement) → affine attempts (rotation + isotropic scale from the sub-finder; anisotropic scale for non-square modules) → matrix decode → on a past-format failure, a bounded perspective search (projective coefficients ±12 % × row-axis shear ±20°, the sub-finder fixing Jacobian scale and rotation in closed form per candidate, gated by the sub-finder-side format copy and the edge timing patterns before any full sample) → one inverted retry. Rented module buffer (2,363 B max), attempt budget 256 full decodes per candidate, ≤ 8 candidates.
- Shared lift: `Internals/ImageDecoders/FinderAxisEstimator` (`RefineModuleSize`, `FindOrientationCandidates`, `MeasureAxis`, `DarkLightDarkRun`, `OrientationCandidate`) out of `MicroQRImageDecoder`, second consumer rule; Micro QR image benchmark flat (below).
- Public `RmQRCodeDecoder.TryDecode(SKBitmap, …)` ×2 and `TryDecodeImage(luminance, width, height, …)` ×2 (frozen signatures), span-destination path 0 B (Release-only test).
- Playground: decode panel and self-check fall back Standard → Micro → rMQR (`symbology: "rmqr"`, no mask field); verified in the published app (self-check ✓ on generated rMQR, upload decode ✓ via the file input). ConsoleApp pattern 29 (rMQR decode, matrix + image + cross-symbology rejection); BlazorWasm sample has no decode panel (nothing to add).
- Tests (test-first; +276 per TFM: `RmQRCodeDecoderImageTest` 117, `RmQRCodeDecoderPerspectiveTest` 15, `RmQrFixtureTest.Decode_PngFixture_*` 144; full suite 9,826 with 226 Release-only skips, 0 failed on net8.0 + net10.0): all 32 × M/H clean renders, module px 3-13 (R7x43, R17x139), non-integer scale (letterboxed 300 / 500 / 730 / 1000 px canvases), non-square 8×12 px modules at 0/90/180/270°, translation, quiet zone 1/2/4, extreme aspect (R7x139, R11x27, R13x27), right-angle rotations, arbitrary rotations incl. every integer degree for R7x43 and R13x77, mirror (R7x43, R17x139), inverted, JPEG q60 (R7x43, R17x139), low contrast, ±24 noise, luminance-span overloads incl. too-small negative, Standard/Micro images rejected and rMQR rejected by the other two decoders, blank / tiny / null / overflow negatives; keystone 2 % and 4 % top-shrunk and left-shrunk for R7x43, R11x77, R17x139, 30° + 2 % keystone (R7x43, R13x99), mirror + 2 % keystone; PNG corpus 144/144 through the image path (`ErrorsCorrected` 0 libzint / ≤ 1 qrtool).
- Docs: spec map (Image Detection rows linked, measured envelope), decoder record (image level, why / decisions / lessons), symbology status (image decode Done, Phase 7), fixture record, docs index, README (Decode ✅, image example, FAQ), this log.

**Lessons learned**

- Everything measured at the finder is only good to a few percent, and a few percent of 139 modules is several modules; the design that worked is format-first (know the size before sampling anything big) plus a precise far anchor (the sub-finder) that overrides every locally measured quantity for scale and rotation.
- Three concrete failures on the way to R17x139 at 4 % keystone, all found by a probe that fed the true homography into the sampler (which decoded fine, so the search was at fault): (1) the fixed sub-finder search radius was smaller than the affine prediction error at the far corner (now 6 + width/20 modules); (2) the column axis leans under a keystone (atan(shrink / height) ≈ 14° at 4 % on 17 rows) and the vertical run through a leaning finder measures the projected row spacing, so the leaning axis is measured/cos φ; (3) the far-end format-copy gate accepts any grid that is right near the pinned sub-finder, so wrong (shear, coefficient) combinations exhausted the full-decode budget before the right shear came up: a second gate on the edge timing patterns between the anchors (bent middle → alternation lost) made the search converge.
- Thirteen "decoder failures" on the first run were the generator rejecting a 14-alphanumeric payload for R7x43-M (capacity 7); version-parameterized image tests need per-version payloads.
- Sub-finder search order matters more than its bound: ring order with a stop at the first perfect match cut the clean-image decode from 77 µs to 20 µs (R7x43) and 262 µs to 136 µs (R17x139) with identical results, because the prediction is nearly always within a module or two.
- Styled symbols (rounded modules + gradient) below about 5 px/module fail at the finder runs (Playground: R15x99 at 512 px wide); the same styling decodes at 1024 px. Not an rMQR-specific limit, but wide symbols reach it sooner at a given canvas width.

**Benchmark delta (`MicroQRImageEndToEnd`, net10.0 Release, warmup 3 × 5 iterations, before = HEAD worktree, after = this change; allocations identical)**

| Benchmark | Before | After |
|---|---|---|
| M2_512px | 4,697 µs | 4,521 µs |
| M4_512px | 4,483 µs | 4,631 µs |
| M4_128px | 327 µs | 315 µs |
| M4_ImageDecode_Span | 14.96 µs | 13.93 µs |

PNG rows are within their run-to-run noise (±5 %); the image decode row (the only one touching the lifted code) is flat/slightly better. Standard QR image and decode paths do not use `FinderAxisEstimator` and no shared file they use changed, so no Standard QR guard run was needed.

**New rMQR image baseline (`RmQRImageEndToEnd`, same job)**

| Benchmark | Mean | Allocated |
|---|---|---|
| R7x43_512px | 1.4-1.7 ms | 4,160 B (PNG) |
| R17x139_1024px | 3.5-3.9 ms | 5,904 B (PNG) |
| R7x43_ImageDecode_Span (376 × 88 px) | 19.9 µs | **0 B** |
| R17x139_ImageDecode_Span (1,144 × 168 px) | 136 µs | **0 B** |

Otsu + the finder scan (proportional to pixels) and the sub-finder template search dominate; a coarse-to-fine template search and the shared placer / extractor fast path are the follow-ups.

### Adversarial review round, completed 2026-08-16

Three review rounds (independent reviewer agents per lens: correctness, performance, API usability, test coverage, spec/doc sync; every report-worthy claim voted on by three independent skeptical verifiers; fixes applied test-first) over the whole rMQR branch. Round 1 surfaced 10 validated findings, round 2 (over the round-1 fixes) 10 more, all low severity; round 3 (sign-off over the round-2 fixes) found one medium finding (a pre-existing status masking that the round-2 contract made visible, below) plus doc/sample nits.

**Done**

- Rendering: the width-only default (static helpers, no-size builder) letterboxed the aspect-derived canvas a second time, leaving 1-3 transparent (JPEG: black) columns on 12 of the 32 versions and a non-opaque PNG on all 32. `QRImageLayout` now fills the default canvas (the renderer paints the background over the whole image and draws the symbol at a uniform module scale inside), `RmQRCodeImageBuilder.WithWidth(int)` is public (rMQR-only in the parity allow-list; Playground and the Blazor sample use it instead of reimplementing the rounding with `WithSize`). Tests: every version at the default width is exact-width and opaque; `WithWidth` precedence vs `WithSize` / `WithModulePixelSize`.
- Matrix decoder: format-copy arbitration is dimension-aware (`RmQRFormatInformationDecoder.TryDecode(finder, sub, expectedVersion, …)`): a copy miscorrected toward another version's word (≥ 5 flips landing within 3 of it, words are 8 apart) can no longer veto the valid copy on a distance tie; the version-agnostic overload was removed. Tests: impostor on either side (6 flips: distance 2 to the impostor, 6 to the truth; both roles regress under the old rule), agreeing copies with different ECC (closer wins, tie → finder side), neither copy agreeing.
- Image decoder: `DestinationTooSmall` is terminal for the finder that produced it (perspective refinement, that finder's remaining frames and the inverted retry are skipped; other finder candidates still run, so a second symbol that fits is found regardless of order): a too-small caller buffer cost 250-500× a sized one (73 ms vs 0.2 ms, R13x99). The inverted pass honours the same terminal status. Round 3 found that `TrackBestFailure` ranked `DestinationTooSmall` level with `DataUncorrectable`, so on the perspective path (entered precisely after an affine attempt failed at RS around the same finder) the terminal outcome was masked and the caller saw `DataUncorrectable` (pre-existing at HEAD, verified in a detached worktree; the round-2 contract made it a broken promise): `DestinationTooSmall` now outranks every other failure, pinned by a keystone + tiny-destination test on R11x77 / R17x139. The perspective quadratic's constant term used |v|² where the Jacobian applies the sheared axis sv (|sv| = |v| / cos φ), biasing the solved anchor scale by up to ~0.65 % (~0.24 module at the anchor for R17x43 at 20°); fixed to |sv|². Tests: too-small status with a loose timing bound (`[NotInParallel]`), inverted parity, two-symbol image with an undersized destination in both layouts; the perspective suite stays green.
- Encoder: the UTF-8 stack budget branches on the analyzer's exact byte count (payloads of 54-150 UTF-8 bytes no longer take the pool path); the version selector computes the "largest capacity" error-message scan only on the failure path.
- Playground and Blazor sample: a fixed version disables and drops the fixed-height option (the pair used to reach the library's contradiction exception).
- Docs: rmqr-encoder.md status / "planned" wording, non-uniform quiet-zone stripping, `WithWidth` in the API block, rendering paragraph and geometry rule; rmqr-decoder.md Supported table (matrix: either copy; image: finder-side copy names the version), Decisions row, Lessons; spec map pipeline diagram, orchestration, §7.9 and layout rows; `RmQRMatrixDecoder` summary; README FAQ; base `WithSize` / `SaveToSvg` XML; `WithQuietZone` / `QrRequest.Size` XML.
- Accepted as documented follow-ups (verified, not fixed): image-level recovery from a damaged finder-side format copy (would need a far-end read through a frame that is only accurate near the finder); non-rMQR rejection cost (10-100 ms on finder-rich images, on par with the Micro QR decoder; the ~24 % finder-side format gate sends wrong frames into the unbudgeted sub-finder search); `RmQRCodeDataUnitTest.CoreAccessors_AreAllocationFree` is flaky under the RmQR-only test filter (pre-existing, allocation attribution under parallel test execution); the 64-word scan in the dimension-aware arbitration could check the version's 2 words (≤ 2 % of a clean decode, left as is); the |sv|² perspective fix is verified analytically (three independent verifiers) and numerically but has no discriminating test (reverting to |v|² keeps the keystone suite green, the bias is below the gates' tolerance), a synthetic sheared-frame probe of `TryPerspectiveVariants` would pin it.

**Lessons learned**

- Two independent letterbox stages compose into a defect neither has alone: the builder rounded the height and the layout re-fitted the rounded canvas. Any "derived canvas" must be filled, not fitted; a per-version test at the default size (all 32) would have caught it, the single R11x27 case happened to have a sub-pixel pad.
- Arbitration between redundant copies must use every constraint already known (here the dimension-derived version); "closer copy wins" alone let a miscorrected copy win a tie.
- A terminal-status short-circuit in a multi-candidate search must be scoped to the candidate that produced it, or it changes which symbol wins in a multi-symbol frame (verifier reproduction); the per-finder scope keeps the 250× saving and the old multi-symbol semantics.
- Timing assertions: `Stopwatch.ElapsedTicks` is not `TimeSpan` ticks off Windows (1 GHz vs 10 MHz); use `Elapsed`, run the test alone, and bound at 50× + 250 ms when the guarded regression is two orders of magnitude.
- The verifier step earned its cost: it corrected the premise of one claim (min inter-version format distance is 8, not 7, so a 4-flip impostor is impossible), calibrated three others down, and reproduced the multi-symbol regression in a round-1 fix.

**Benchmark delta (`RmQREncodeEndToEnd` / `RmQRDecodeEndToEnd` / `RmQRImageEndToEnd`, net10.0 Release, warmup 3 × 5 iterations, before = HEAD, after = review fixes)**

| Benchmark | Before | After | Reference row (untouched code), before → after |
|---|---|---|---|
| RmQR_Numeric_R7x43_Encode (Span) | 0.92 µs | 1.12 µs | StandardQr_Numeric_V1_Encode (Span) 1.89 → 2.25 µs |
| RmQR_Alphanumeric_R11x59_Encode (Span) | 2.62 µs | 3.16 µs | |
| RmQR_Byte_R17x139_Encode (Span) | 11.5 µs | 13.5 µs | |
| RmQR_Numeric_AutoFit_Encode (Span) | 1.02 µs | 1.24 µs | |
| RmQR_Numeric_R7x43_Decode (Span) | 871 ns | 919 ns | StandardQr_Numeric_V1_Decode (Span) 932 → 989 ns |
| RmQR_Alphanumeric_R11x59_Decode (Span) | 2.71 µs | 3.15 µs | |
| RmQR_Byte_R17x139_Decode (Span) | 15.4 µs | 16.6 µs | |
| R7x43_512px (PNG) | 1,371 µs / 4,160 B | 1,177 µs / 4,072 B | opaque surface: RGB PNG, smaller and faster to encode |
| R17x139_1024px (PNG) | 3,493 µs / 5,904 B | 3,178 µs / 5,616 B | |
| R7x43_ImageDecode_Span | 20.6 µs | 20.1 µs | |
| R17x139_ImageDecode_Span | 127 µs | 129 µs | |

Allocations identical on every row (span paths 0 B). The encode / decode "after" run landed on a noisier machine state (StdErr 5-30 % vs 1-3 % before): the untouched Standard QR reference rows moved by the same +6 % (decode) / +19 % (encode) as the rMQR rows, and the fixed-version encode rows execute byte-identical code before and after (the encode-path changes are the version-selector failure scan, bypassed by a requested version and strictly less work on auto fit, and the UTF-8 branch of WriteByte, which the ASCII benchmark payloads never take), so the rMQR deltas are run-to-run noise, not the review changes; the decoder change decodes the same two format copies as before. The PNG rows are a real improvement (the width-only default is now an opaque RGB surface). Image decode is flat.

### Follow-up: bit-stream fast path (`RmQRBinaryEncoder`), completed 2026-08-16

The first of the post-Phase-7 kernel rounds (kernel benchmark loop: 19 variants over 3 rounds, byte-identical gate of 35,851 encodes per variant against the verbatim baseline, disassembly-read, converged when round 3 fell inside the canary band).

**Done**

- `src`: `RmQRBinaryEncoder` rewritten around a raw-local writer (64-bit MSB-first accumulator, pending-bit count, byte position, `ref byte` destination threaded through inlined `Append` / `AppendWide` / `Append64`; no per-flush slice checks because every stored bit is real data inside the capacity that version selection guarantees). Numeric: 64-bit SWAR 3-digit groups, 9 digits per 30-bit append, x64 SSSE3/SSE4.1 tier 12 digits per `pmaddwd` / `phaddd` / `packusdw` / `pmaddwd` → 40-bit append. Alphanumeric: unchecked value table with 2 pairs per 22-bit append, x64 tier 8 chars per `pshufb`-classified values + `pmaddubsw`(45,1) + `pmaddwd`(2048,1) → 44-bit append. Byte: SSE2 narrow 8 chars per 64-bit append; UTF-8 in a separate `NoInlining` cold function with its own writer (Micro QR's address-exposure lesson), still `Encoding.UTF8` into the 160-byte stack budget. Terminator + alignment are bit-count arithmetic; pads are 8-byte 0xEC11 stores; the mode switch computes its own header. Vector tiers are `NET8_0_OR_GREATER` + capability gated; netstandard / non-x86 take the SWAR / table paths. Allocation contract unchanged (netstandard2.0 UTF-8 remains the documented exception).
- Tests (+40 on each TFM; full suite net10.0 5,007 / net8.0 4,995, 0 failed): `RmQRBinaryEncoderKernelParityTest` drives each internal segment writer with `vectorized` = true / false from the same pre-seeded writer state (13 header phases × every length up to R17x139-M capacity × min / max / cyclic / random contents; every alphanumeric symbol through every vector lane) and compares the logical bit stream (stored bytes + pending bits, because `AppendWide` may leave exactly 32 bits pending where `Append` flushes). The existing `RmQRBinaryEncoderParityTest` (naive reference, all 64 × 3 modes × every length) and the corpus-oracle unit tests pin the end-to-end stream unchanged.
- Refuted along the way (kept in the kernel findings log): `Encoding.Latin1` bulk narrowing (loses to a direct SSE2 pack once the input is known Latin-1), the hand-rolled UTF-8 encoder (loses to `Encoding.UTF8` at 150 bytes; Micro QR's ≤ 15-byte finding does not transfer), a 16-char byte block with hoisted shift (neutral), and single-switch / zero-skip fixed-cost trims (neutral, shipped only as the simpler shape).

**Lessons learned**

- The shared `BitWriter` ref struct IS fully promoted by the JIT; what it pays for is the `Span.Slice` range check on every 32-bit flush (~6 instructions). A `ref byte` writer with a proven capacity contract halves the writer cost at rMQR sizes — worth revisiting for the Standard QR encoder, whose acc64 round stopped at the struct.
- `pmaddwd` pairs are fixed at lanes (0,1),(2,3),…: a 3-digit group cannot straddle a pair, so "two groups per 8-char load" silently reverses every second group. The gate caught it before any measurement; the shipped shape is one group per load from four overlapping loads.
- Layout noise dominates at 10-50 ns when every variant is a different compilation of one large method (mode switch inside): the canary moved up to ±32 % in one round, and variants that changed only the numeric path moved the byte scenario by +47 %. Read the column of the mode a variant changed; treat cross-mode swings as noise; never accept a < 3 % delta from one run.
- Inlining hot writers is only a win once the writer body is lean: forcing the checked `GetAlphanumericValue` (two throw branches per char) inline regressed alnum 1.6-1.8x until the unchecked table replaced it.

**Benchmark delta (`RmQREncodeEndToEnd`, net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15, before = HEAD 62c0268, after = this change; kernel numbers from the kernel benchmark loop)**

| Benchmark | Before | After | Kernel before → after |
|---|---|---|---|
| RmQR_Numeric_R7x43_Encode (Span) | 1.125 µs | 1.047 µs | 16 → 12 ns |
| RmQR_Alphanumeric_R11x59_Encode (Span) | 2.843 µs | 2.821 µs | 55 → 16 ns |
| RmQR_Byte_R17x139_Encode (Span) | 14.52 µs | 14.05 µs | 177 → 23 ns |
| RmQR_Numeric_AutoFit_Encode (Span) | 1.157 µs | 1.119 µs | |
| StandardQr_Numeric_V1_Encode (Span) (untouched control) | 1.891 µs | 2.072 µs | |

Kernel: 1.3x (12 digits) to 7.5x (150 Latin-1 bytes), 2.6x on the largest numeric (361 digits: 199 → 80 ns), 5.6x on the largest alphanumeric (219 chars: 268 → 48 ns), 0 B everywhere. E2E: the encoder is 1-2 % of the encode pipeline (placement dominates: R17x139 spends ~14 µs painting 2,363 modules), so the E2E rows move by 1-7 %, at or below the run-to-run band (the untouched control drifted +9.6 %). The next lever for the encode E2E is the placer fast path listed under Follow-ups, not the bit stream.

### Follow-up: placer fast path (`RmQRModulePlacer`), completed 2026-08-16

Second post-Phase-7 kernel round (kernel benchmark loop: 13 variants over 3 rounds, byte-identical gate of 3,840 placements per variant against the verbatim reference — all 32 versions × 2 ECC × all-zero / all-one / random / over-long messages on a poisoned core — disassembly-read, converged when round 3 fell inside 3 %).

**Done**

- `src`: `RmQRModulePlacer.PlaceSymbol` is now the fast path; the per-module painters (`PlaceFunctionModules`, `PlaceFormat`, `PlaceData`) stay as `PlaceSymbolReference`, the source of truth that builds the tables and that the matrix decoder's `IsFunctionModule` / `GetMaskBit` still share. Per-version `Layout` built once (lazily, `Volatile` publish): a function template plus a per-ECC template with both format copies painted (memcpy replaces ~500 pattern stores + 36 format stores + two BCH words per call), the zigzag walk as `ushort` core indices with a mask byte per position, and the column-pair segmentation (a pair is "clean" when both columns are pure data on rows 1..h-2). Placement = template copy → one vector pass expanding the message bits to bytes fused with the mask XOR (AVX2 32 modules / SSSE3 16 per step under `NET8_0_OR_GREATER`, scalar 8-per-byte otherwise; remainder positions get the mask only) → store pass: clean pairs as one byte-swapped 16-bit store per row from the bit array, the finder / format / alignment / sub-finder-neighbour pairs as an index scatter. Scratch is the repo's fixed-budget policy: 512-byte stackalloc (every version ≤ 63 codewords) with an `ArrayPool` rental above it. Zero allocations after the one-time tables (≤ 8.5 KB per version, both ECC templates included; ~120 KB if every version and ECC were ever used).
- Tests (+40 on each TFM; full suite net10.0 5,073 / net8.0 5,061, 0 failed): `RmQRModulePlacerParityTest` (fast vs reference for all 32 × 2 ECC × six message shapes incl. the generator's buffer size and an over-long message, oversized core untouched beyond w×h, undersized-buffer contracts of both paths, ECC alternation on a shared cache). The existing structural / format read-back / extraction / module-exact oracle tests (every committed external symbol) now run through the fast path.
- Refuted along the way (kept in the kernel findings log): band stores of up to 4 pairs as one 8-byte store per row (4x fewer stores, neutral — the strided destination, not the store count, bounds the pass), and the pure index scatter once fixed costs were gone (loses 12-17 % to the pair stores on medium/large versions).

**Lessons learned**

- Rung 1 (hoist everything version-derived) was 6-7x on its own before any instruction-level work: the per-module `IsFunctionModule` predicate + `col / 3` were the placer. Every later rung (bit expansion, pair stores, fused XOR) added 1.2-1.7x each; the fixed-cost trims mattered only once the loop was cheap (a ~1.9 KB stack zeroing was 25 % of the smallest version).
- Strided byte scatter runs at ~1 store per cycle and neither wider stores per row nor AVX-512 scatter change that; the win is in touching fewer bytes per module (bit-per-byte expansion, no per-module shifts), not in wider stores.
- Keep the reference painter: it builds the tables (correct by construction), stays the decoder's predicate, and is the parity oracle — the fast path never re-derives geometry.

**Benchmark delta (`RmQREncodeEndToEnd`, net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15, before = HEAD 9b4a095, after = this change; kernel numbers from the kernel benchmark loop)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| RmQR_Numeric_R7x43_Encode | 1.191 µs / 112 B | 329 ns / 112 B | -72 % |
| RmQR_Alphanumeric_R11x59_Encode | 3.173 µs / 160 B | 614 ns / 160 B | -81 % |
| RmQR_Byte_R17x139_Encode | 14.67 µs / 368 B | 2.379 µs / 368 B | -84 % |
| RmQR_Numeric_R7x43_Encode (Span) | 1.061 µs | 179 ns | -83 % |
| RmQR_Alphanumeric_R11x59_Encode (Span) | 2.699 µs | 318 ns | -88 % |
| RmQR_Byte_R17x139_Encode (Span) | 13.65 µs | 1.054 µs | -92 % |
| RmQR_Numeric_AutoFit_Encode (Span) | 1.115 µs | 358 ns | -68 % |
| StandardQr_Numeric_V1_Encode (Span) (untouched control) | 2.116 µs | 2.182 µs | +3 % (drift) |

Kernel: 864 → 49 ns (R7x43), 2,304 → 123 ns (R11x59), 5,730 → 252 ns (R13x99-H), 12,721 → 473 ns (R17x139), 0 B; E2E 6-13x on the span paths because the placer was 80-90 % of the encode. Remaining encode-side levers, in order: automatic version selection (AutoFit 358 vs fixed 179 ns — the fit scan is now half of a small encode), the `RmQRCodeData` result object and its packing on the class API, and the quiet-zone copy of the span path.

### Follow-up: result-object packing and quiet-zone placement, completed 2026-08-16

The two encode-side levers left by the placer round, done test-first (parity + generator tests written and failing before the code).

**Done**

- `src`: `Internals/ModuleBitPacker` — shared byte-per-module ↔ MSB-first bit-packed conversion for the Micro QR and rMQR data models (`SetCoreData` / `GetCoreData` of both, replacing their per-module loops): pack = non-zero compare, in-group lane reversal, move-mask (AVX2 32 / Vector128 16 modules per step) with a SWAR 8-per-load scalar tail; unpack = byte broadcast + bit mask + compare (the placer's expand shape) with an unrolled scalar tail; portable scalar on netstandard. Pack writes every packed byte (padding bits zero), so the previous `Array.Clear` on re-pack is gone too.
- `src`: `RmQRModulePlacer.PlaceSymbol(destination, stride, …)` — strided variant writing the core straight into a wider matrix (row-wise template copies, pair stores with the caller's pitch, row/col-coded scatter for the irregular pairs; the packed-core path keeps its offset scatter). `RmQRCodeGenerator`'s span destination with a quiet zone now clears only the light borders and places into the strided window: no intermediate core rental, no row copies.
- Tests (+316 on each TFM; full suite net10.0 5,493 / net8.0 5,481, 0 failed): `ModuleBitPackerParityTest` (vs a naive reference: every length 0..80 + all rMQR core sizes + larger, 0/1 / any-non-zero / high-bit-only / all-dark / all-light / random contents, exact write extents, short-buffer contracts, pack↔unpack round trip); `RmQRModulePlacerStridedParityTest` (every version × ECC × three strides against the reference rows, gap bytes and tail untouched, stride / size contracts); `RmQRCodeGeneratorUnitTest.CreateSpan_QuietZones_MatchClassApiAndTouchOnlyRequiredBytes` (quiet zones 0/1/2/5, module-for-module equality with the class API, nothing written past the required size). `RmQRCodeDataUnitTest`'s SetCoreData/GetCoreData round trip and the Micro QR decoder round trip (`MicroQRCodeDecoderRoundTripTest`, which unpacks through `MicroQRCodeData.GetCoreData`) exercise both data models through the new packer.

**Benchmark delta (`RmQREncodeEndToEnd` / `MicroQREncodeEndToend` / `RmQRDecodeEndToEnd`, net10.0 Release, --launchCount 2 --warmupCount 3 --iterationCount 15, before = HEAD 92dcd0c, after = this change)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| RmQR_Numeric_R7x43_Encode (class) | 312 ns / 112 B | 149 ns / 112 B | -52 % |
| RmQR_Alphanumeric_R11x59_Encode (class) | 614 ns / 160 B | 269 ns / 160 B | -56 % |
| RmQR_Byte_R17x139_Encode (class) | 2,314 ns / 368 B | 942 ns / 368 B | -59 % |
| MicroQR_Numeric_M2 / Alnum_M3 / Byte_M4_Encode (class) | 237 / 284 / 324 ns | 135 / 166 / 179 ns | -43 / -42 / -45 % |
| RmQR_Numeric_R7x43_Encode (Span, quiet zone 2) | 173 ns | 164 ns | -5 % |
| RmQR_Alphanumeric_R11x59_Encode (Span) | 311 ns | 322 ns | +3 % (noise) |
| RmQR_Byte_R17x139_Encode (Span) | 984 ns | 1,023 ns | +4 % (noise) |
| RmQR_Numeric_R7x43 / Byte_R17x139_Decode (class, unpack) | 926 / 15,989 ns | 900 / 15,771 ns | -3 / -1 % |
| StandardQr_Numeric_V1_Encode (Span) (untouched control) | 943 ns | 961 ns | +2 % (drift) |

Allocations unchanged (the class results are the returned objects; span paths 0 B). The class-API pack was the whole gap between class and span paths (rMQR class results now cost less than the quiet-zoned span call; the Micro QR class API overtook its span path). The strided quiet-zone placement is time-neutral (the row/col-coded scatter for irregular pairs costs about what the removed pool rental and row copies did) and is kept for the contract: the generator's span destination path no longer rents an intermediate core buffer (the placer's own bit-scratch rental above 63 codewords, documented in the placer entry, is unchanged). Decode is flat (unpack was never its bottleneck).

### Follow-up: table-driven automatic version fit (`RmQRVersionSelector`), completed 2026-08-17

Third post-Phase-7 kernel round, one round only (kernel benchmark loop: 4 variants, gate of 93,492 selections per variant against the definitional scan for every mode × ECC × strategy × height filter × length).

**Done**

- `src`: the auto-fit path of `RmQRVersionSelector.Select` no longer evaluates `Fits` + `IsBetter` for all 32 versions per call. Three static tables (~1.3 KB, built at type init from `IsBetter` and `GetMaxDataLength`, so they cannot drift from the definitions): the versions in best-first order per strategy, each version's capacity per (mode, ECC) at that rank, and a per-strategy height bitmask; the fit is the first rank whose capacity holds the length and whose height is allowed. Kernel 27-108 ns → 1.3-8.3 ns (a constant-time SIMD compare/movemask variant measured ~1.8 ns but was not shipped: ≤ 6.5 ns on a ≥ 300 ns encode, and the scalar scan needs no intrinsics). Requested-version and failure-message paths unchanged.
- Tests (+3; full suite net10.0 5,496 / net8.0 5,484, 0 failed): `RmQRVersionSelectorUnitTest.Select_AutoFit_MatchesDefinitionalScan_EveryLengthStrategyHeight` — every mode × ECC × strategy × height filter × length 0..max+3 against the definitional "best fitting version" scan (Fits + IsBetter), including the nothing-fits exception; the existing hand-derived fit tables, tie-break and interop-default tests keep passing.

**Lessons learned**

- A benchmark harness that copies internals also copies its own enums: the harness numbered the modes 0/1/2 while the library's `EncodingMode` is 1/2/4, and the first port indexed the capacity table with the raw enum value — every table-driven port needs a definitional test in the library, because the harness gate cannot see a mapping bug that only exists in the port.
- With the placer at ~50-500 ns, second-order fixed costs (a 32-candidate fit scan, ~110 ns for numeric) become the largest remaining item; keep re-reading the E2E table after each round rather than the original profile.

**Benchmark delta (`RmQREncodeEndToEnd`, net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15, before = HEAD f6a3816, after = this change)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| RmQR_Numeric_AutoFit_Encode (Span) | 318.3 ns | 211.7 ns | -33 % |
| RmQR_Numeric_R7x43_Encode (Span) (fixed version, code-identical) | 148.8 ns | 169.7 ns | +14 % (noise) |
| RmQR_Alphanumeric_R11x59_Encode (Span) (fixed) | 289.4 ns | 328.2 ns | +13 % (noise) |
| RmQR_Byte_R17x139_Encode (Span) (fixed) | 927.8 ns | 1,044 ns | +13 % (noise) |
| StandardQr_Numeric_V1_Encode (Span) (untouched control) | 850.3 ns | 955.6 ns | +12 % (drift) |

The after run landed on a noisier machine state (StdDev 5-7 % vs < 1 % before): every fixed-version row and the untouched control moved by the same +12-14 %, so the true auto-fit gain is ~-40 % (the fit scan was ~110 ns of 318). Auto fit of 12 digits selects R11x27 (297 modules), so it is not directly comparable with the fixed R7x43 row.

---

### Follow-up: codeword extraction fast path (2026-08-17)

`RmQRMatrixDecoder.ExtractCodewords` was the last per-module walk left in the rMQR
pipeline, and profiling put it at 70-91 % of `DecodeMatrix` (Reed-Solomon correction
is under 1 %) and ~10-15 % of the image path. The decode direction now gets the same
treatment the placer got on the encode side.

**Done**

- Per-version extraction tables, lazily built from `RmQRModulePlacer.IsFunctionModule`
  and `GetMaskBit` so encode and decode cannot drift apart, published with a volatile
  write. Two forms: the walk order with the mask fused into the top bit of each entry,
  and a PEXT/PDEP descriptor per column pair. The walk is truncated to whole codewords
  at build time, so neither kernel needs a remainder check.
- Bit-plane kernel (`RmQRMatrixDecoder.Simd.cs`, x64 with AVX2 and fast BMI2): the byte
  grid is transposed once into per-column bit planes (16-bit lanes, 16 columns per
  vector step, a forward and a row-reversed plane in the same pass), then each column
  pair of the zigzag is emitted with one PEXT + PDEP per column and one XOR for the
  data mask. No module byte is read twice and no output bit is handled individually.
- Portable kernel for every other target: the walk-order table gathered into a register
  accumulator, one store per output byte, branchless.
- `stream.Clear()` dropped from `DecodeMatrix`: both kernels write every byte.
- The fast-PDEP CPUID probe (AMD is microcoded before Zen 3) moved out of
  `MicroQRModulePlacer.PlaceSymbol` into a shared `HardwareCapabilities`, now used by
  both symbologies.
- `RmQRExtractCodewordsParityTest`: both tiers against `RmQRNaiveReference`
  .`ExtractInterleavedStream` for all 32 versions over all-light, all-dark written as
  1 / 0xFF / 2, and pseudo-random grids, on a poisoned destination; plus a test that
  pins the transpose's deliberate overread to stay inside `width * height`.
  Full suite green on net8.0 and net10.0 (5,561 / 5,549 tests).

**Lessons learned**

- The per-module `IsFunctionModule` predicate alone was 40-70 % of the baseline, and
  its share grew with width because it loops over the alignment columns. Hoisting
  version-derived work into tables was worth more than every instruction-level trick
  that followed; the vector work added ~5x on top of the ~12x the tables gave.
- A fast path that only handles the regular case leaves its own guard as the
  bottleneck. Splitting column pairs into "clean" and "irregular" covered 82-94 % of
  the bits at width 139 but only 39-45 % at width 27 (3 of 13 pairs), so those versions
  paid the full transpose and still ran most bits through a per-bit loop, regressing
  against the plain table walk. PEXT and PDEP take arbitrary masks, so an irregular
  pair is the same two instructions with different constants; deleting the split was
  the largest single win after the tables (-33 % to -41 %) and removed the regression.
- Lane width beat vector width: 512-bit steps landed inside noise of 256-bit ones twice
  (Zen 4 double-pumps them), while narrowing the plane lanes from 32 to 16 bits at the
  same vector width was worth 15-18 %. A column needs h-2 <= 15 bits, so a `ushort`
  lane doubles the columns per step for free. No AVX-512 tier shipped.
- An overread beat a tail: rows 1..h-2 always have a row below them, so the transpose
  can run past the end of a row instead of peeling scalar tail columns. Worth 10-25 %,
  most on narrow symbols where 3 of 27 columns were the tail.
- At 40-600 ns per call the prologue is the hot loop: two `stackalloc` zero-inits and a
  `Span<uint>` argument that rematerialized on the stack were 20 % of the runtime on
  small symbols. `[SkipLocalsInit]` would still buy 1.5-10 % but needs
  `AllowUnsafeBlocks`, which the library does not set; left as a one-line opt-in.

**Benchmark delta (net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15, before = HEAD a62812a, after = this change)**

`RmQRDecodeEndToEnd`:

| Benchmark | Before | After | Delta |
|---|---|---|---|
| RmQR_Numeric_R7x43_Decode (Span) | 892.1 ns | 177.8 ns | **-80 % (5.0x)** |
| RmQR_Alphanumeric_R11x59_Decode (Span) | 3,024.6 ns | 554.8 ns | **-82 % (5.5x)** |
| RmQR_Byte_R17x139_Decode (Span) | 17,706.2 ns | 2,223.1 ns | **-87 % (8.0x)** |
| RmQR_Numeric_R7x43_Decode (string) | 953.4 ns | 203.5 ns | -79 % |
| RmQR_Byte_R17x139_Decode (string) | 17,540.4 ns | 2,494.7 ns | -86 % |
| StandardQr_Numeric_V1_Decode (Span) (untouched control) | 915.7 ns | 923.7 ns | +0.9 % (unchanged) |

`RmQRImageEndToEnd`:

| Benchmark | Before | After | Delta |
|---|---|---|---|
| R7x43_ImageDecode_Span | 19.93 us | 17.20 us | **-14 %** |
| R17x139_ImageDecode_Span | 114.70 us | 97.59 us | **-15 %** |

Span variants stay at 0 B allocated; the string overloads keep their existing 48 B /
328 B result allocations. The image path is capped by Amdahl: extraction is only
10-15 % of it, with the Otsu histogram and the finder scan dominating — those are the
next targets, along with `LuminanceConverter` (measured at 66 % of a full
`TryDecode(SKBitmap)` call, and shared by all three symbologies).

---

### Follow-up: luminance conversion fast path (2026-08-17)

With extraction fixed, `LuminanceConverter.ConvertRgba` was the largest single item in
the whole decode: differencing `TryDecode(SKBitmap)` against `TryDecodeImage(span)`
(which skips the conversion) put it at **69 %** of the bitmap path on both R7x43 and
R17x139. It is shared by all three symbologies.

**Done**

- AVX2 kernel (`LuminanceConverter.Simd.cs`), 32 pixels per iteration, bit-identical to
  the per-pixel loop. Each pixel is shuffled into the byte quad `[R, G, G, B]` and run
  through `pmaddubsw` against `[77, 51, 99, 29]` then `pmaddwd` with ones. The split of
  green into 51 + 99 is what makes it exact: `pmaddubsw` sums adjacent byte pairs into
  signed 16-bit lanes, so each pair weight must stay at or under 128, and the BT.601
  weights sum to exactly 256.
- Row remainders take 8-pixel blocks plus one overlapping block (which redoes a few
  pixels with identical values) instead of dropping to the scalar loop: worth 23-60 %,
  most on narrow symbols where the tail was 6 % of the pixels running 30x slower.
- Alpha handled without leaving the vector path in both shapes that occur:
  straight alpha replaces fully transparent pixels with white (exact, since
  `(c·0 + 255·255)/255 = 255` and white is exactly luminance 255), and premultiplied
  adds 255 − a to the luminance, which is exact for *every* alpha because the composite
  adds 256 · (255 − a) to a sum whose low 8 bits are then shifted away. The
  premultiplied path never falls back.
- One loop per alpha mode, dispatched once outside the row loop. Fusing them into a
  single method measured 2-3x *slower* on code size alone (5.7 KB against 1.4 KB).
- `LuminanceConverterParityTest`: both tiers against each other over all three pixel
  layouts, premultiplied and straight, four alpha shapes, widths straddling the 8 and
  32-pixel steps, padded and tight rows, plus a test pinning that the vector loads stay
  inside the caller's pixel span. Full suite green on net8.0 and net10.0.
- `RmQRImageEndToEnd` gained `R7x43_BitmapDecode` / `R17x139_BitmapDecode`: no existing
  benchmark went through the `SKBitmap` entry point, so the conversion was invisible.

**Lessons learned**

- A noise canary has to be the same *kind* of code as the variants it calibrates. A copy
  of the scalar baseline reported a 2-14 % noise floor while the vector variants on the
  same large scenarios were swinging 60 % between process launches - the baseline is
  compute-bound, the vector kernels are memory-bound. Two rounds of verdicts were
  artifacts of that, and one sent a whole round chasing a loop-rotation theory that the
  disassembly appeared to support. Adding a byte-identical copy of a *vector* variant
  made the real floor visible and retired both false findings.
- Identical disassembly is evidence that a difference is not in the code. Two loops that
  matched instruction for instruction were credited with a 34 % gap; that should have
  been read as "this cannot be a code effect".
- The textbook first move was the weakest one: specialising the runtime channel offsets
  bought 2-13 %, mostly inside noise, for +640 B of code, because the JIT was already
  hoisting the address arithmetic.
- The correctness gate turned a wrong optimisation into a better one. Whitening
  transparent pixels is exact for straight alpha but not for premultiplied buffers that
  violate c ≤ a; the gate rejected it, and asking why produced the identity that makes
  the premultiplied composite a single add with no fallback at all.

**Benchmark delta (net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| R7x43_BitmapDecode | 66.27 µs | 20.38 µs | **-69 % (3.25x)** |
| R17x139_BitmapDecode | 371.23 µs | 117.72 µs | **-68 % (3.15x)** |
| R7x43_ImageDecode_Span (control, does not call the converter) | 20.56 µs | 18.86 µs | -8 % (drift) |
| R17x139_ImageDecode_Span (control) | 115.06 µs | 99.76 µs | -13 % (drift) |

The controls moved, so the raw deltas are not clean: the before run sat on a noisier
machine (StdDev 8-14 % against 0.9-5 % after) and the untouched render scenarios in the
same report drifted 12-28 %. The drift-free reading is the within-run difference, since
bitmap decode is luminance decode plus the conversion: **45.71 → 1.52 µs (30x)** on
R7x43 and **256.17 → 17.96 µs (14.3x)** on R17x139, matching the kernel measurements
once `PeekPixels` and the tier dispatch are included. The converter's share of
`TryDecode(SKBitmap)` falls from 69 % to 7 % / 15 %.

**Open**

Arbitrary partial alpha on the straight-alpha branch still runs the scalar formula
(a transparent-background PNG with anti-aliased module edges has exactly that shape).
It is vectorizable exactly - for 0 ≤ x ≤ 65535, `x / 255 == ((x + 1) * 257) >> 16`,
which is one `pmulhuw` - at roughly 3x the ops of the opaque path, so still around
5-8x the scalar loop. Left open rather than declared converged.

Next in the image path, now that conversion is 7-15 %: the Otsu histogram and
`FinderPatternFinder`, whose scalar cross-checks measured 90 % of `FindCandidates`.

---

### Follow-up: finder candidate scan stride (2026-08-17)

With conversion down to 7-15 % of the bitmap path, `FinderPatternFinder.FindCandidates`
was ~75 % of `TryDecodeImage(span)` and ~64 % of `TryDecode(SKBitmap)`. It is the other
finder entry point: `TryFind` (Standard QR) already shipped the SIMD row bitmask **and**
a row stride with a complementary rescan, while `FindCandidates` — used by the Micro QR
and rMQR image decoders — kept the strideless full sweep. That was sized for Micro QR
(17 modules square); rMQR at 8 px/module makes it a 1144 × 168 sweep.

**Done**

- Row stride 6 for `FindCandidates`, with the complementary pass over the skipped rows
  triggered when **no candidate was confirmed on two or more rows**, rather than when
  none was found at all. That is the whole idea: the stride stops being a correctness
  parameter. A stride too coarse for the symbol's module size leaves the true finder at
  Count 1, the fallback runs, and the union is exactly a full sweep — so a bad stride
  costs time, never a detection. `TryFind` is untouched.
  > **Superseded by the review round below (2026-08-18).** The claim in this bullet is
  > false: `Count >= 2` is evaluated over the whole candidate list, so any other
  > finder-like pattern in the frame satisfies it on the real symbol's behalf and the
  > fallback never runs. Measured loss and the replacement are in that entry.
- `FindCandidatesFullSweep` exposed internally for the parity test.
- `FinderCandidatesStrideTest`: across 3-13 px/module and the narrowest, widest and
  smallest rMQR shapes, every candidate a full sweep confirms must survive the strided
  path within 1 px; plus a noise image where nothing is confirmed, so the two paths must
  agree exactly. Full suite green on net8.0 and net10.0 (5,573 / 5,561 tests), including
  the per-degree rotation sweep, the 144-symbol PNG corpus, perspective, JPEG q60,
  ±24 noise and low contrast.

**Lessons learned**

- A per-call cost measured on a synthetic worst case does not tell you what the real
  case is bound by. A 2 px stripe image put the run walk at ~17 cycles per dark run and
  ~8.5 per `NextBit`, which predicted a solid win from registerizing the walk; it bought
  4-10 %, inside the canary spread. The real image's walk is bound by data-dependent
  branches over unpredictable run lengths, which that rewrite does not change.
- Two changes that are each inside the noise do not add up to a signal: combining the
  integer ratio pre-filter with the registerized walk was *worse* than either alone on
  two scenarios.
- Row count beat every instruction-level idea by an order of magnitude, and once the
  stride landed the micro-optimizations contributed nothing measurable at all.
- Do not re-litigate a prior round's refutation without a new mechanism. The `TryFind`
  round had already rejected aggressive strides on envelope-safety grounds and this
  round reproduced that conclusion; what made the wider stride legitimate was not a
  better argument but a different fallback trigger that changes its failure mode.

**Benchmark delta (net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15)**

> **Superseded by the review round below (2026-08-18).** Every figure in this section,
> and the kernel gain quoted after it, was measured on stride 6 with the in-scan
> complementary pass. The shipped design is stride 4 with the widening moved to the
> image decoders; its numbers, measured against `main` rather than against a branch
> commit, are in that entry.

`RmQRImageEndToEnd`:

| Benchmark | Before | After | Delta |
|---|---|---|---|
| R7x43_ImageDecode_Span | 18.86 µs | 7.07 µs | **-63 % (2.7x)** |
| R17x139_ImageDecode_Span | 99.76 µs | 38.62 µs | **-61 % (2.6x)** |
| R7x43_BitmapDecode | 20.38 µs | 9.89 µs | **-51 % (2.1x)** |
| R17x139_BitmapDecode | 117.72 µs | 52.94 µs | **-55 % (2.2x)** |
| R7x43_512px / R17x139_1024px (renders, untouched) | 1,021 / 2,661 µs | 1,012 / 2,678 µs | -1 % / +1 % |

`MicroQRImageEndToEnd` (the scan is shared):

| Benchmark | Before | After | Delta |
|---|---|---|---|
| M4_ImageDecode_Span | 13.80 µs | 6.20 µs | **-55 % (2.2x)** |
| M2_512px / M4_512px (renders, untouched) | 4,486 / 4,480 µs | 4,471 / 4,503 µs | -0.3 % / +0.5 % |

Kernel gain was 5.3-6.7x; the span decode gained 2.6x, the Amdahl gap being the
remaining Otsu, grid sampling and matrix decode. Across the three decode rounds the
R17x139 span decode has gone **115 → 99.8 → 38.6 µs**.

**Open**

The inverted-retry path still redoes everything on a second buffer: the reflectance
retry inverts the luminance (a full pass, measured at 115 µs on 192k px) and rescans.
The dark bitmask of the inverted image is the exact complement of the first pass's, so
keeping the per-row masks (width × height bits, 24 KB for 192k px) would let the second
polarity skip both the inversion and the mask build. That is a change in
`RmQRImageDecoder`, not in the finder, and it is now the largest remaining item on the
failure path.

---

### Follow-up: the failed-decode path (2026-08-18)

With the success path down to 36-53 µs, the failure path had never been measured. Adding
`NoSymbol_*` scenarios to `RmQRImageEndToEnd` showed two regimes on 1144×168:

- an ordinary non-QR image (gradient) fails in **235 µs**, essentially all of it in Otsu
  and in the reflectance-retry inversion (the two were profiled separately, against
  different bases, so their individual shares are not quoted here);
- salt-and-pepper noise fails in **14.2 ms**, because false finder candidates appear.

Profiling localised the second one: `TryLocateSubFinder` costs **375 µs** per wrong frame
(radius 25 half-modules → 2,601 ring positions × 3 shear leans × a 5×5 template =
195,000 samples), and `TryFrame` calls it once per frame whose finder-side format copy
decodes — an 18-bit word matched against 64 valid ones within Hamming distance 3, which
random data passes roughly a quarter of the time.

**Done**

- `TryLocateSubFinder` bounding-box rejection: every sample sits at
  `predicted + (offU + i)·u + (offV + j)·sv` with |offU| ≤ radius/2 and |i| ≤ 2, and the
  12° lean bounds |svX| by |vX| + tan 12°·|vY|; if that box misses the image, every
  position scores 0 and the baseline scanned all 7,803 of them to return false.
- `TryLocateSubFinder` row-wise early exit: after each row of five samples, abandon the
  position when `score + remaining < SubFinderMinScore`. Outcome-safe because a partial
  score may become best-so-far but acceptance needs the floor, and any position reaching
  the floor outranks every partial one.
- `LuminanceInverter`: the reflectance-retry inversion vectorized (`255 - x` on a byte is
  the ones' complement, so it is one NOT per lane), lifted out of all three image
  decoders which each had the same scalar loop. 17-20× on the kernel.
- Otsu was **not** touched: it already had a round whose accepted trade-off was parity on
  noise, and the measurements here confirm that position rather than contradict it (the
  gradient case runs at 0.36 ns/px, close to the ~0.28 ns/px a naive per-pixel fill gets
  on random data, i.e. already at its floor).

**Lessons learned**

- A kernel benchmark scenario is a hypothesis about the input distribution, and it can be
  wrong in a way statistical rigour cannot catch. The kernel round attributed the win to
  the bounding box (20,000× on its wrong-frame scenarios) — but both of those scenarios
  predicted off-image positions, which the real caller's wrong frames mostly do not. End
  to end the box is worth 15 % and the early exit is worth the rest.
- Where an early exit is tested decides whether the fast path pays for it. Testing per
  sample cost **+12 %** on `R17x139_ImageDecode_Span`, reproduced across two runs against
  two independent baselines; testing once per row of five cost nothing and kept most of
  the failure win. The per-sample form would have given -77 % on the adversarial path
  instead of -67 %, which was not worth 12 % on the most common large decode.
- A ratio of thousands between adjacent benchmark scenarios is a bug report, not a fact:
  the 2,770× gap between the right and the wrong frame named work that scales with a
  search space in a case whose answer is known before searching.

**Benchmark delta (net10.0 Release, --launchCount 5 --warmupCount 3 --iterationCount 20; before at fb516f0)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| NoSymbol_Noise_1144x168 (adversarial failure) | 14,208.8 µs | 4,694.7 µs | **-67 % (3.0x)** |
| NoSymbol_Gradient_1144x168 (ordinary failure) | 234.6 µs | 166.6 µs | **-29 %** |
| R17x139_ImageDecode_Span | 38.7 µs | 37.5 µs | -3 % |
| R7x43_ImageDecode_Span | 7.63 µs | 7.59 µs | -1 % |
| R17x139_BitmapDecode | 52.8 µs | 47.7 µs | -10 % |
| R7x43_BitmapDecode | 9.90 µs | 9.09 µs | -8 % |

**Open**

The remaining 4.7 ms on the adversarial image is the 18-bit format gate's false-accept
rate: it lets roughly a quarter of random frames through to a sub-finder search. Two
levers, both behaviour-affecting and so deliberately not taken here: require Hamming
distance ≤ 1 for the image path's first format read, or budget sub-finder searches per
candidate the way full decodes are already budgeted by
`MaxDecodeAttemptsPerCandidate`. The perspective search has the same shape — its gates
are not counted against that budget, only its full decodes are.

---

### Follow-up: adversarial review of the decode branch (2026-08-18)

An adversarial review of the four decode optimizations before opening the PR. Three of
them survived unchanged. The finder-scan stride did not: its safety argument was wrong,
and a differential sweep against `main` measured the cost.

**The finding.** `FindCandidates` skipped rows and repaired a too-coarse stride with a
complementary pass over the skipped rows, triggered when no candidate had been confirmed
on two or more rows. `Count >= 2` is evaluated over the whole candidate list, so it is a
statement about the *image*, not about the symbol being looked for: a second QR code, a
printed logo, or salt-and-pepper noise confirms on its own and suppresses exactly the
pass the real symbol needed. Measured over 15,980 rendered images (rMQR R7x43-R17x139 and
Micro QR M1-M4, 2-13 px/module, 0-90°, with and without decoy finder patterns, plus
noise / blur / JPEG q60), against a build of `main`:

| corpus | regressions vs main | improvements |
|---|---|---|
| decoy-focused (6,000) | **69** | 26 |
| mixed sweep (3,980) | **15** | 7 |
| decoy-free control (6,000) | 3 | 0 |

Zero divergence without a competing pattern in frame, which is why the branch's own
tests — one symbol per image — never saw it.

**Done**

- The fallback moved out of the scan and into the image decoders. `FindCandidates` is now
  a plain strided scan with no fallback; `RmQRImageDecoder` and `MicroQRImageDecoder`
  re-run the whole decode through `FindCandidatesFullSweep` when the strided pass read
  nothing. That trigger — "did anything decode" — is the only one available that another
  pattern cannot answer in the symbol's place, and it makes detection a strict superset
  of a full sweep's. Re-measured: **0 regressions on all three corpora, +95 net gains**,
  and the retry never fires behind a first pass that already succeeded (0 of 15,980).
- Stride 6 → 4. Requiring the stride to land in every in-envelope band means measuring
  the band, and the 3-modules-tall figure holds only for an axis-aligned symbol: under
  rotation the run ratios drift off-centre and the surviving band's floor is 5 rows at
  3 px/module. This no longer carries the guarantee (the retry does), it decides how
  often the retry is paid — it runs on 0.3-1.1 % of the images that do decode, inside
  the envelope, and on images that fail under `main` too, where a full sweep is what a
  caller wants anyway.
- The retry's gate is `IsTerminal`, not `== Success`: `DestinationTooSmall` means the
  symbol *was* read, and a wider scan cannot change that. Gating on `Success` cost a
  second full pipeline (measured 2.1x) on a documented probe-for-required-size call.
- Otsu hoisted so both scans of one polarity share a threshold. Without it the ordinary
  failure path ran the histogram four times and regressed **+47 % against `main`**.
- `LuminanceInverter`: an argument check, a `Vector128` tier (ARM64 was silently scalar),
  and the overlapping final vector replaced by a scalar tail — the overlap re-read bytes
  the aligned loop had written, so it double-inverted whenever a caller aliased the spans.
- Bit-plane extraction only dispatches when `stream.Length` equals the version's codeword
  count (the kernel's output length is fixed by the version, the portable tier truncates
  to the span); `BuildColumnPlanes`'s unreachable scalar half deleted in favour of an
  asserted precondition; `PlaneStride` / `ExtractCodewordsScalar` made private.
- `HardwareCapabilities` moved wholly inside its TFM gate, Hygon added to the AMD-lineage
  PEXT test, and `MicroQRModulePlacer`'s duplicate alias removed.
- Tests: both parity tests were order-blind (`IsEquivalentTo` is a multiset compare in
  TUnit) — a byte-order permutation of the whole codeword stream passed green, and now
  fails 64/64. Hardware-gated tests `Skip.Test` instead of returning green on ARM CI.
  New `LuminanceInverterTest` (there was none), `RmQRSubFinderGuardTest`, and a rewritten
  `FinderCandidatesStrideTest` covering rotation with decoy patterns in frame.

**Lessons learned**

- A fallback is only as sound as the question that triggers it. Both finder entry points
  stride and both widen on failure, but only `TryFind` was ever safe, because "no
  consistent triple" is a question about the symbol it is looking for. Anything a flat
  candidate list can report is a property of the image, and the image contains other
  things. The fix was not a better predicate — it was moving the decision to the layer
  that knows what success means.
- Single-symbol test images cannot test a rule that quantifies over the image. Every test
  here rendered one symbol per frame, so a predicate that any second pattern could
  satisfy was invisible to all of them, in both symbologies, for the whole round.
- Measure the repair, not just the defect. The first fix — tighten the stride, trigger on
  an empty list — was reasoned carefully and made things **worse** (net −29 / −77 / −25
  vs −8 / −43 / −3), because an empty candidate list almost never occurs. Isolating the
  two changes one at a time showed the stride change was an improvement and the trigger
  change was the whole regression.
- An assertion helper that reads like equality may not be. `IsEquivalentTo` compares
  collections as multisets, so both SIMD parity tests were blind to permutation — the
  single most likely failure mode of a bit-plane transpose or a lane permute.
- Optimizing a two-pass structure means checking what the second pass repeats. The retry
  doubled the Otsu histogram, which is the largest single item on an ordinary non-QR
  image; hoisting it turned a +47 % regression into −16 %.

**Benchmark delta (net10.0 Release, --launchCount 3 --warmupCount 3 --iterationCount 15;
before = `main` at 7422ac9 with this branch's benchmark file, so the failure scenarios
are measurable on both sides)**

| Benchmark | main | branch | Delta |
|---|---|---|---|
| R7x43_ImageDecode_Span | 20.12 µs | 8.56 µs | **-57 % (2.4x)** |
| R17x139_ImageDecode_Span | 124.89 µs | 46.83 µs | **-62 % (2.7x)** |
| R7x43_BitmapDecode | 65.41 µs | 11.55 µs | **-82 % (5.7x)** |
| R17x139_BitmapDecode | 336.14 µs | 62.93 µs | **-81 % (5.3x)** |
| NoSymbol_Noise_1144x168 (adversarial failure) | 12,880.19 µs | 9,048.98 µs | **-30 %** |
| NoSymbol_Gradient_1144x168 (ordinary failure) | 216.08 µs | 186.65 µs | **-14 %** |

The success-path figures are 13-32 % above the pre-review branch (7.59 → 8.56, 37.5 →
46.83, 9.09 → 11.55, 47.7 → 62.93 µs; stride 4 scans 1.5x the rows of stride 6), and the
adversarial failure is above its pre-review 4.69 ms figure because a failing image now runs
two scan passes per polarity. Both were measured against the wrong baseline before: `main`
is what the PR changes, and against `main` every scenario improves. Absolute figures on
this machine vary a few percent between runs; the before and after columns above are from
runs of the same shape, and the deltas are far outside that spread.

**Later rounds of the same review**

The review ran to four rounds; rounds 3 and 4 found defects in round 2's own fixes.

- The retry gate was fixed in one place and not the other: the first pass tested
  `IsTerminal`, the swept pass still tested `== Success`, so when the *sweep* was the pass
  that read the symbol its `DestinationTooSmall` was discarded and the caller got the
  strided pass's `NotDetected`. Both gates now test terminal.
- `DestinationTooSmall` did not actually mean "the symbol was read", which is the premise
  the gate rests on. `SegmentDecoders` checked the caller's buffer *before* checking the
  character count against the remaining bits, so a stream whose count could not possibly
  fit reported a short buffer instead of a malformed symbol — and because callers treat
  that as terminal, it stopped the search for the real symbol. Byte mode already had the
  checks in the right order; numeric and alphanumeric now do too.
- `MicroQRImageDecoder`'s failure ranking never got the `DestinationTooSmall` promotion
  rMQR has. Sizes are tried 17 down to 11, so a wrong-size attempt that reached RS first
  masked it: M3-L reported `DataUncorrectable` for a short buffer while every other
  version reported `DestinationTooSmall`.
- Two "stays inside the span" tests could not detect an over-run, because both compared
  only the region the kernel is supposed to write. The luminance one now hands the
  converter a destination with a poisoned tail and asserts the tail is untouched; that
  catches a block-count rounding error in either AVX2 kernel, which nothing did before.

**Lessons learned (rounds 3-4)**

- A fix's premise deserves the same scrutiny as the code. "DestinationTooSmall means the
  symbol was read" was stated in a comment, used as the justification for a control-flow
  change, and was false — three call sites checked the buffer before the bitstream. The
  comment is now the specification of an ordering the segment decoders have to maintain.
- A test that asserts only the bytes a kernel should write cannot see the bytes it should
  not. Poison the tail and assert it, or the bounds claim in the summary is decoration.
- Fix one thing in two places or neither. Both retry gates were the same decision written
  twice; changing one of them produced a state that was wrong in a way neither the old nor
  the intended behaviour was.
- Do not share a working tree with a concurrent measurement. A verification agent's
  differential run picked up another agent's temporary mutation and reported 293/451/475
  regressions and the investigation's only two wrong-text results. The numbers were real
  and the mutation was real; only the attribution was wrong. It did establish something
  useful: an off-by-one *over*-charge in the numeric bit budget rejects the correct
  candidate and lets the decoder accept a wrong grid that passes ECC, so that constant now
  has its own equivalence-class test (payload lengths ending on each remainder branch, and
  M1 numeric at its exact 5-digit capacity).

**Open**

The strideless retry re-runs the per-candidate pipeline even when the sweep found no
candidate the strided pass had not — measured 2.0x on failing images whose two candidate
lists were identical. Comparing the two lists before re-running the pipeline would skip
it; the scan is far cheaper than the per-candidate work it guards. Not taken here because
any list-comparison tolerance is a detection heuristic, and this branch's detection
parity is currently measured at zero regressions.

Ranking `DestinationTooSmall` above the other failures (both symbologies now do) lets a
wrong-grid attempt that passes format + RS and reads a segment longer than the caller's
buffer report it on a frame that holds no symbol: 2 of 11,200 fuzzed noise frames, with a
1-char destination. No wrong text is ever returned, and with a realistically sized buffer
it cannot arise, so the more useful diagnostic for the common case wins. Revisit if a
caller reports growing its buffer for an image with nothing in it.

### rMQR ECI emission, completed 2026-08-18

**Done**

- Added non-breaking, explicitly named `CreateRmQRCodeWithEci` / `GetRequiredBufferSizeWithEci`
  APIs to every `RmQRCodeGenerator` class/span/sizing path. Keeping ECI out of the existing
  method's third argument preserves source compatibility for positional `default` calls.
  `Default` uses the shared Standard QR policy: ASCII has no ECI, Latin-1 emits assignment 3,
  and other Unicode emits assignment 26 with UTF-8 bytes. Explicit ISO-8859-1 rejects
  unrepresentable characters rather than silently narrowing them.
- `RmQRBinaryEncoder` emits the 11-bit `111` + 8-bit designator prefix. UTF-8 retains its
  separate non-inlined writer so the x64 hot writer's accumulator remains register-promoted.
  Kanji mode remains intentionally unsupported and independent of ECI.
- Version selection charges the 11-bit prefix in exact-fit errors, inverse capacities and
  automatic selection. The best-first selector remains table-driven: its capacity tables now
  include ECI absence/presence (about 2.5 KB total table data), rather than reverting to a
  per-call 32-version definitional scan.
- Test-first coverage pins exact ECI 3/26 codewords, ECI capacity boundaries, exhaustive
  table-vs-definitional selection for all versions/ECC/modes/ECI choices, public overload
  parity and round trips, invalid ECI/charset rejection, and allocation-free span output.
  Legacy qrtool UTF-8 fixtures without ECI remain decoder oracles but are explicitly excluded
  from encoder module-exact comparison because the new conforming stream must differ.
- `RmQRCodeImageBuilder.WithEciMode` exposes the same explicit control at the high-level image
  surface; the API parity guard requires ECI on Standard QR and rMQR but not Micro QR.
- External gate: zxing-cpp decoded all 318 generated symbols (all versions × ECC ×
  Numeric/Alphanumeric/Byte, plus 63 ISO-8859-1 ECI-3 and 63 UTF-8 ECI-26 symbols), matching
  text, raw bytes, version and ECC. The expected totals are asserted so an exception cannot
  silently remove ECI cases; only R7x43-H cannot contain ECI plus a one-byte payload.

**Verification**

- Full tests after the repair rounds: net10.0 5,621 total / 5,498 passed /
  123 capability-skipped / 0 failed; net8.0 5,609 total / 5,496 passed /
  113 capability-skipped / 0 failed.
- BenchmarkDotNet, net10.0 Release, x64 Ryzen 7 5800H, span destination, R17x139-M,
  all paths report zero managed allocation. A controlled no-ECI comparison used the same
  2-launch / 3-warmup / 10-iteration job: pre-ECI `5801279` measured 2.428 us and the repaired
  implementation 2.401 us (overlapping 99.9% confidence intervals; no detected regression).
  A later final run measured 2.025 us, illustrating the host's run-to-run noise rather than a
  claimed speedup. One-launch ECI path checks measured Latin-1 2.202 us and UTF-8 2.707 us.
  Payload work differs, so the ECI figures are absolute guards, not cross-payload deltas.

**Adversarial review and repair rounds**

- Round 1 found that adding `EciMode` as the third argument made existing positional
  `default` calls ambiguous, omitted builder control, and let the external gate silently
  skip ECI cases. The API was changed to the explicitly named `CreateRmQRCodeWithEci` /
  `GetRequiredBufferSizeWithEci` family, `WithEciMode` was added, and the gate now asserts
  318 total / 63 assignment-3 / 63 assignment-26 cases.
- Round 2 found a correctness bug introduced by the performance repair itself: the existing
  `Default` API's isolated fast path treated automatically detected Latin-1/UTF-8 as no ECI
  and selected versions without charging the 11-bit prefix. Only analysis results whose
  resolved ECI is actually `Default` now use the no-ECI encoder and selector; automatic
  ECI uses the ECI-aware paths. A two-byte Latin-1 capacity-boundary test, class/span exact
  module parity with a poisoned destination tail, and explicit builder/data rejection pin
  the repair.
- Round 3 re-ran the 318-symbol external oracle, both full target-framework suites, and the
  controlled performance comparison. No further actionable defect remained.

---

### Follow-up: N5, NEON rectangular grid sampling (2026-08-20)

**Done**

- `RmQRImageDecoder.SampleGrid` is now a dispatcher over three tiers: `SampleGridSimd128`
  (projective), `SampleGridSimd128Affine`, and `SampleGridScalar` (unchanged, kept as the
  parity reference and the fallback below Vector128). Every rMQR width is at least 27, so
  real symbols always take the vector path.
- Kernel: **2.4-3.8x** over the scalar loop on Apple M2, zero allocations
  (`MICRO_OPTIMIZATION_RmQrSampleArm.md` in the private benchmark repo). Four steps carried
  it: vectorizing the convert/clamp/gather (0.42-0.58x), an overlapping vector tail instead
  of a per-row scalar tail (3-8 %), dropping both divisions on affine frames (10-17 %), and
  replacing the stack spill of the index vector with constant-lane extraction (20-35 %,
  the largest single step).
- The affine tier is not a bet on typical input: `TryDecodeFrame` builds the isotropic and
  anisotropic frames with `perspectiveX = perspectiveY = 0`, so every attempt before the
  perspective search has an exactly unit denominator, where `x / 1f == x` drops both
  divisions without changing a sampled byte.
- `RmQRSampleGridParityTest`: all 32 versions x 3 seeds x 3 capture geometries (axis-aligned
  affine, rotated affine, projective), plus off-image sampling (the clamp) and degenerate
  frames (collapsed axes, a denominator crossing zero into Inf/NaN, a mirrored frame).
  38 tests; full suite 5,787 tests, 0 failed on net10.0.

**Lessons learned**

- **Measure the phase split before accepting the package's premise about which kernel to
  write.** This is now three for three across the NEON queue, each wrong in a different way.
  N4's text called the Micro QR `TBL` + `CMTST` idiom the low-risk port to make: the idiom did
  port and was worth 4.7x on its own phase, but that phase was only ~40 % of ARM placement and
  the store pass (48-59 %) produced the actual win. N5's text called the Standard QR sampler
  shareable: it rounds differently and could not be shared at all. N5 also assumed the shape
  that won there would transfer: it did not, because rMQR's rows are shorter. The package line
  is a hypothesis about where the time is and which code to write, and it deserves the same
  measurement discipline as the variants inside the loop.
- **A kernel that shares a function name across symbologies does not necessarily share an
  implementation.** This plan's own N5 line assumed the Standard QR row kernel was
  "bit-identical ... usable by ARM64". It is not: Standard QR computes `1/d` once and
  multiplies twice, rMQR divides each numerator, and those round differently. Under N5's
  exact-bytes rule the correct move was a separate kernel. Generalizing would have silently
  changed sampled pixels.
- **Bit-exactness is less restrictive than it sounds** once you separate what re-associates
  from what does not. Hoisting the loop-invariant *product* `a2x*gridY` is exact (the same
  float, computed once); folding `a2x*gridY + a3x` into one row constant is not. Skipping a
  division by exactly 1f is exact; replacing it with a reciprocal multiply is not.
- **The shape that won for Standard QR ARM lost here.** Two independent 4-lane chains per
  iteration gained 1.5-4 % on Standard QR and lost on rMQR, whose rows are 27-139 columns
  rather than 21-177. Going the other way lost too: 4 lanes cost 1-4 %, 16 lanes cost
  15-20 % (register pressure). Width is not monotonic and must be measured per caller.
- **After vectorizing, the bottleneck was memory traffic the code did not appear to have**:
  writing the index vector to a `stackalloc` and reading it straight back put
  store-to-load forwarding on the critical path of every gather. Constant-lane `GetElement`
  removed it for 20-35 %.
- **Byte-identical fallback paths double as free noise canaries.** Each late variant
  degenerates to an earlier one outside its specialization, so rows that must be identical
  reveal the run's noise floor. That is how a contaminated round was caught (18 % spread
  between identical code, against 3 % in a clean round) instead of being read as a result.
- **Widening a dispatch means widening the gate's input classes.** The first scene builder
  fixed `uY = 0`, so every affine case was axis-aligned and the `a12 != 0` rotated-affine
  path went unexercised; a rotated geometry was added before any number was recorded.
- **One before/after pair is not enough on this machine.** In the first pair
  `R7x43_BitmapDecode` read +12 % and looked like a regression; alternating a second pair
  showed it was drift (two runs of the *same* binary differed by 16 %).

**Benchmark delta (net10.0 Release, `--launchCount 4 --warmupCount 3 --iterationCount 15`,
before/after alternated A/B/A/B, Apple M2)**

| Benchmark | before A1 | after B1 | before A2 | after B2 | Delta |
|---|---|---|---|---|---|
| R7x43_ImageDecode_Span | 17.45 us | 15.60 us | 19.29 us | 15.15 us | **-16 %** |
| R17x139_ImageDecode_Span | 100.27 us | 92.64 us | 99.40 us | 79.88 us | **-14 %** |
| R17x139_BitmapDecode | 118.77 us | 106.34 us | 100.72 us | 94.97 us | -8 % |
| R7x43_BitmapDecode | 18.62 us | 20.78 us | 21.62 us | 17.47 us | -5 % |
| NoSymbol_Gradient_1144x168 (control, no `SampleGrid`) | 291.12 us | 296.60 us | 258.58 us | 261.88 us | flat |

All four span comparisons favour the change and the control is flat, but this machine's
run-to-run drift is comparable to the effect, so the direction is firm and the point
estimate is soft. Allocations unchanged (span paths 0 B).

**Open**

- **Absolute E2E numbers are 2x the values recorded earlier in this log** (R17x139 span
  ~100 us here against 37.5-46.8 us in the 2026-08-17/18 entries). Contamination by a
  concurrent benchmark was excluded, and the micro harness agrees with the historical
  per-module cost (rMQR scalar 2.98 ns/module against Standard QR scalar 2.06, the
  difference being one extra division), which points at machine state or a code difference
  between those recording points rather than at this change. Unresolved; before/after here
  was taken under identical conditions, so the comparison stands even though the absolutes
  do not line up with the older rows.
- **Proposed follow-up, deliberately not shipped in N5: axis-aligned sampling.** When a
  transform is affine *and* unrotated (`a12 == 0`) the sampled y is constant along a row, so
  the y vector, its conversion, its clamp and the row multiply collapse to one scalar per
  row. Measured 6-10 % on top of the shipped kernel for axis-aligned input and 0 % for
  rotated input. It is not rMQR-specific or ARM-specific — Standard QR, Micro QR, x64 and
  the scalar path would all take it — so it belongs in its own cross-symbology package
  rather than buried in an rMQR NEON item. Screen renders and square-on scans hit it;
  photographs do not.
- The gather is now the floor: 0.70 ns/module is about the cost of
  `umov`/`ldrb`/`cmp`/`cset`/`strb`, and NEON has no gather instruction. Further work on
  this loop needs a different data layout, not a better kernel.

---

### Follow-up: N6, rMQR segment writers (2026-08-20)

**Done**

- `RmQRBinaryEncoder.WriteLatin1` gains a portable `Vector128` tier: 16 characters
  narrowed per iteration (`Vector128.Narrow` = XTN + XTN2 on ARM64) plus an 8-character
  cleanup, feeding the existing 64-bit append. **9.2x on the writer** (109.2 ns → 11.9 ns
  for a 150-character payload) and **-11 % on byte-mode encode E2E**.
- The existing `Sse2` block is untouched and the new tier sits behind `else if`, so only
  targets with 128-bit vectors and no SSE2 (ARM64 NEON, WASM) enter it. x64 flatness is
  guaranteed by construction rather than by measurement, which matters because x64 cannot
  be measured on the ARM machine this work was done on.
- New `RmQRBinaryEncoderWriterReferenceTest`: every writer against an independently
  written one-group-at-a-time reference, for every length up to the largest rMQR capacity
  and every header phase (0-12 pending bits), compared on the logical bit stream.
  Full suite 5,826 tests, 0 failed on net10.0.

**Measured and deliberately not shipped**

- **Alphanumeric, 8 characters per 44-bit append.** Won 19 % in the isolated kernel at
  both payload sizes with clean canaries, then measured *worse* end to end (331.6 / 304.7
  ns before against 339.9 / 348.2 after). Reverted. Its Amdahl ceiling was 5.4 % anyway,
  below the acceptance threshold.
- **Numeric, 12 digits per 40-bit append.** -9 % at 361 digits, **+6 % at 12 digits** —
  and 12 digits is the only numeric payload in the E2E set, where the writer is 0.3 % of
  encode. A size-switched version could recover the large case, but the ceiling does not
  justify the branch.

**Lessons learned**

- **The package name predicted the wrong solution again** (three for three, now four).
  N6 was queued as "NEON segment writers"; the shipped win uses no NEON-specific
  instruction at all — one portable `Vector128.Narrow` plus 8x fewer accumulator updates.
  The two writers that *would* have needed real NEON kernels are exactly the two that
  turned out not to be worth optimizing.
- **What was slow was the writer-state update rate, not the character decoding.** Every
  measured gain in this round came from making one append cover more characters. That is
  worth checking before writing any decode-side vector kernel.
- **An isolated-kernel harness can delete the effect that decides the outcome.** Measuring
  each mode in its own method was necessary for attribution — a shared-method harness
  produced +68 % on an untouched path and had to be thrown away — but production keeps all
  three writers in one `switch` method, so enlarging one arm changes the whole method's
  codegen. The alphanumeric variant won in the harness that could attribute it and lost in
  the shape that ships. Both harnesses lie; a kernel win needs re-measuring in production
  shape before it is believed.
- **A five-minute attribution probe outranked every kernel in the round.** A variant that
  writes only the header and padding gives the writer's exact share of encode, and its
  0.3 % for numeric settled that mode without writing a single kernel.
- **Canaries have to be structurally identical code, not merely code you did not intend to
  change.** Round 2's "unchanged" rows moved 68-74 % because they were recompiled inside a
  different method body.

**Benchmark delta (net10.0 Release, `--launchCount 4 --warmupCount 3 --iterationCount 15`,
before/after alternated, Apple M2, span = allocation-free path)**

| Benchmark | before A1 | before A2 | after | Delta |
|---|---:|---:|---:|---|
| RmQR_Byte_R17x139 (150 chars) | 1,052.2 ns | 1,070.3 ns | **944.0 ns** | **-11 %** |
| RmQR_Latin1_ECI_R17x139 (112 chars) | 998.3 ns | 1,032.1 ns | **906.3 ns** | **-11 %** |
| RmQR_UTF8_ECI_R17x139 (control, different writer) | 913.9 ns | 1,064.0 ns | 903.4 ns | flat |
| RmQR_Numeric_R7x43 (control, unchanged writer) | 165.7 ns | 140.3 ns | 152.8 ns | within noise |
| StandardQr_Numeric_V1 (control, untouched) | 2,487.3 ns | 2,540.2 ns | 2,326.1 ns | -6 % (drift) |

The kernel delta predicts -9 % and observations ranged -6 % to -11 % across runs. The
untouched StandardQr control moved -6 % between runs on its own, so read the point
estimate with that width; the direction and order of magnitude are firm. Allocations
unchanged (span paths 0 B).

**Open**

- The alphanumeric batching win is real in isolation and unavailable in the current method
  shape. If encode is ever restructured so each mode compiles independently (separate
  non-inlined entry points per mode, chosen once by the caller), it becomes available
  again for free — worth revisiting then, not before.
- x64 is unmeasured for this change by construction (the new tier cannot execute there).
  If the SSE2 Latin-1 block is ever revisited, note that the 16-character shape measured
  better than the 8-character one on ARM and the same may hold with `PackUnsignedSaturate`.
