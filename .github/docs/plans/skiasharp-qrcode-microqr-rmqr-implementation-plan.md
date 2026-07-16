# Micro QR / rMQR Implementation Plan for SkiaSharp.QrCode

## Purpose

This document defines the implementation order for adding Micro QR Code (ISO/IEC 18004) and rMQR Code (ISO/IEC 23941) support.

It builds on the test strategy in `skiasharp-qrcode-microqr-rmqr-test-strategy.md`, which defines HOW each piece is verified. This document defines WHAT is built, in WHICH order, and WHY that order.

## Guiding Decisions

### Separate entry points per symbology

The existing `QRCodeGenerator.CreateQrCode` surface does not extend cleanly:

- `requestedVersion` is an `int` in 1-40; Micro QR versions are M1-M4 and rMQR versions are R7x43-R17x139 (32 rectangular sizes).
- `ECCLevel` (L/M/Q/H) does not map: Micro QR M1 has error detection only, M2/M3 allow L/M, M4 allows L/M/Q; rMQR allows only M/H.
- Auto version selection, capacity tables, and mode restrictions differ per symbology.

Therefore each symbology gets its own generator entry (e.g. `MicroQrCodeGenerator`, `RmQrCodeGenerator`, or new method families on `QRCodeGenerator` — decided in Phase 0) with symbology-typed version and ECC parameters. `QRCodeGenerator.CreateQrCode` stays byte-for-byte unchanged.

### Split Standard-QR-specific classes from generic ones

Reusable as-is (leaf primitives, all symbologies share GF(256) with polynomial 0x11D):

- `GaloisField`, `EccBinaryEncoder` / `EccBinaryDecoder` (Reed-Solomon, incl. SIMD kernels)
- `BitWriter` / `BitReader`
- `LuminanceConverter`, Otsu threshold, `PerspectiveTransform`
- Rendering infrastructure (with rectangular-aware changes)

Symbology-specific (new code, must NOT be forced into the Standard QR pipeline):

- Capacity / ECC / interleaving tables
- Format information (Micro QR: one 15-bit copy, 4 mask patterns, different mask scoring; rMQR: 18-bit, two copies, version embedded in format)
- Mode / character-count indicator widths (Micro QR mode indicator is 0-3 bits depending on version)
- Function pattern layout and data placement (Micro QR: single finder; rMQR: finder + sub-finder, edge timing patterns, rectangular zigzag)
- Symbol detection in images

Structural approach: keep the Standard QR pipeline untouched (it is heavily perf-tuned and zero-alloc); add sibling internal namespaces (e.g. `Internals/MicroQr`, `Internals/RmQr`) that reuse the leaf primitives. Do not introduce a polymorphic abstraction over the hot path. Blast-radius check: existing Standard QR tests and benchmarks must stay green and flat through every phase.

### Data model must be generalized once, up front

`QRCodeData` is square-only, versions 1-40, and its "QRR" serialization rejects sizes below 21 — Micro QR (11-17 modules) and rMQR (rectangular) cannot reuse it. Phase 0 decides the shape (sibling types vs. a common symbol-data abstraction, serialization header v2 with symbol type + width + height). This decision is made once, anticipating rectangles, even though Micro QR alone would not need them.

### External oracle reality

- ZXing.Net (used in current in-CI cross tests) cannot decode Micro QR or rMQR. In-CI cross-verification is unavailable for the new symbologies.
- Committed fixtures from external encoders are therefore the primary conformance oracle in PR CI, exactly as the test strategy prescribes.
- Candidate oracles: zxing-cpp (decode: Micro QR + rMQR; the only broadly available OSS decoder), Zint (encode-only), qrcode rust crates (encode Micro QR), rmqrcode-python (encode rMQR), BoofCV (decode Micro QR). Phase 1 produces a verified capability matrix (which tool encodes/decodes which symbology, pinned versions) before any fixture is trusted.

## Implementation Order

Vertical slices per symbology: encoder first (releasable on its own), matrix decoder immediately after (validates the same tables independently and installs a round-trip regression net), image detection deferred to the end (hardest, riskiest, lowest initial value).

Micro QR before rMQR: square and small (4 versions, single finder), best oracle coverage, and it flushes out the data-model and serialization changes with the smaller step. rMQR then starts from a validated foundation and only adds the rectangular concerns.

### Phase 0 — API and data model spec

- Write `specs/` design doc: symbology model, generator entry points, decoder entry points (symbology restriction flags so Standard QR scanning perf is unaffected by default), data type shape, serialization format v2, Kanji-mode scoping decision (recommend: defer Kanji, document why).
- Mechanical prep refactor only if the spec demands it; zero behavior change, verified by existing tests + benchmark flatness.

Exit: spec reviewed; existing suite green; no benchmark regression.

### Phase 1 — Fixture infrastructure, proven on Standard QR

- `tools/` fixture generator producing `case.json` + `case.matrix.txt` + `case.png` per the test strategy, with pinned tool versions (container-based for reproducibility).
- Oracle capability matrix verified and recorded.
- Generate a Standard QR corpus first and assert the EXISTING implementation against it. This validates the harness itself against a trusted implementation before it is used to judge new code.
- Fixture-driven test infrastructure (loader, matrix comparer, manifest schema) lands in the test project.

Exit: Standard QR fixture tests green in PR CI; regeneration is one documented command.

### Phase 2 — Micro QR encoder

- Tables (capacity, ECC, format), M1-M4 encoding pipeline reusing BitWriter/RS kernels, new data type, square rendering support, QRR-v2 serialization.
- Matrix conformance tests against Phase 1 fixtures (all versions × legal ECC × Numeric/Alphanumeric/Byte, capacity boundaries, illegal-combination rejection).
- Spot-check: zxing-cpp decodes generated symbols (manual or interop CI, not PR CI).

Exit: test strategy §14 encoder MVT satisfied for Micro QR. Releasable.

### Phase 3 — Micro QR matrix decoder

- Module matrix → payload: format parsing, unmasking, codeword extraction, RS correction, bitstream decoding. Same internal boundary as `QRMatrixDecoder`.
- External-encoder fixtures (Zint / rust) as decoder input; error-correction flip tests; negative tests (Micro presented as Standard and vice versa).

Exit: decoder MVT (matrix-level rows) satisfied; round-trip regression net in place.

### Phase 4 — rMQR encoder

- Rectangular tables (32 sizes, ECC M/H), 18-bit format info, finder + sub-finder + edge timing placement, rectangular rendering, version auto-fit strategy (width-first / height-first preference exposed in API).
- Matrix conformance against fixtures (all 32 sizes at least once; boundary payloads).

Exit: encoder MVT satisfied for rMQR. Releasable.

### Phase 5 — rMQR matrix decoder

- As Phase 3, for rectangular matrices (width/height API).

Exit: decoder MVT (matrix-level) satisfied for rMQR.

### Phase 6 — Image detection and sampling

- 6a: Micro QR detection (single finder — different search strategy from three-finder Standard QR), sampling, clean + degraded PNG fixtures.
- 6b: rMQR detection (finder + sub-finder, extreme aspect ratios), sampling.
- Deterministic degradation tests per test strategy §7, representative subset only.
- Decoder entry defaults keep Standard QR-only scanning at current performance; new symbologies are opt-in or explicitly-typed.

Exit: decoder MVT image-level rows satisfied; degradation matrix green.

### Phase 7 — Interop CI (parallel track, starts after Phase 2)

- Scheduled/manual workflow: pinned zxing-cpp + encoders, live round-trips both directions, committed-fixture drift detection.

### Phase 8 — Physical device acceptance

- Per test strategy §11, as release acceptance for the combined feature set.

## Cross-cutting

- Test layout (decided in Phase 0): the single test assembly is organized
  symbology-first, mirroring `src` — `Shared/`, `StandardQr/`, `Rendering/`, with
  `MicroQr/`, `RmQr/`, and `Fixtures/` added by their phases. No per-symbology test
  projects: `InternalsVisibleTo` targets one assembly, the suite runs in ~20s, and
  TUnit filters select by class name regardless of folders. The one planned exception
  is live interop tests with external native dependencies (zxing-cpp etc.), which get
  their own project excluded from PR CI; committed-fixture tests stay in the main
  assembly.
- Test-first development applies to every phase (project rule).
- Each phase updates the relevant `specs/` docs with lessons learned (project rule).
- Playground (WASM) gains Micro QR / rMQR generation after Phase 2/4 as the living demo; NativeAOT/WASM CI must cover the new paths.
- Benchmarks: new symbology paths get end-to-end benchmarks; Standard QR benchmarks guard against regression at every phase.
- Progress logging (mandatory): when a phase completes, append an entry to the Progress log below recording what was done, lessons learned, and benchmark deltas — or an explicit statement of why benchmarks are not applicable (e.g. no `src/` change).

## Progress log

### Phase 0 — completed 2026-07-16

**Done**

- Symbology architecture spec: `specs/qrcode-symbologies.md` (shared vs per-symbology inventory, dependency rules, API/data-model direction, Kanji deferral).
- `src` reorganized: Standard-QR-specific internals moved to `Internals/StandardQr` (12 files); `EncodingModeExtensions` split out of the shared `EncodingMode`; character-class predicates extracted from `QRCodeConstants` into shared `CharacterSets`.
- Tests reorganized symbology-first in the single assembly: `Shared/` (9), `StandardQr/` (21), `Rendering/` (10); `QrCodeConstantsUnitTest` renamed to `Shared/CharacterSetsUnitTest`.
- Specs renamed symbology-first (`standardqr-spec-map.md`, `standardqr-decoder.md`), `docs_authoring_guidelines.md` created (doc-type templates, naming, linking policy), documentation index added at `.github/docs/README.md`, README gained Supported Symbologies + Micro QR/rMQR FAQ.

**Lessons learned**

- The namespace dependency rule immediately surfaced two hidden couplings: shared `TextAnalyzer` depended on character predicates living inside the Standard QR constants class, and `GetCountIndicatorLength` looked shared but encodes Standard QR version thresholds. Both recorded in `qrcode-symbologies.md`.
- The pre-existing `QrCodeConstantsUnitTest` turned out to test only the (now shared) character sets — test names drift from their subjects unless reorganizations re-check them.

**Benchmarks**

- Not run: changes were mechanical moves (namespace/type relocation of static members with identical bodies and inlining attributes); no signature or algorithm change. Full suite 2,370 tests green on net8.0 + net10.0 before and after.

### Phase 1 — completed 2026-07-16

**Done**

- `tools/QrInteropFixtures`: fixture generator with a plug-in `IFixtureGenerator` interface; first generator is ZXing.Net 0.16.11 in-process (via `ZXing.QrCode.Internal.Encoder` for the core matrix plus version/mode/mask metadata). Regeneration is one command: `dotnet run --project tools/QrInteropFixtures -- regenerate`.
- Committed Standard QR corpus: 21 deterministic cases (all modes × all ECC levels, v1-L alphanumeric capacity boundary, v10/v15/v25 mid sizes, v40-L at exactly 7089 digits, UTF-8/ECI Japanese + emoji), 63 files ≈ 178 KB.
- Fixture test infrastructure: `FixtureLoader` (manifest schema + matrix parser) and `StandardQrFixtureTest` decoding every fixture through both the matrix path and the PNG image path, asserting payload, version, ECC, and mask pattern (86 tests across both TFMs). Full suite: 2,456 green.
- Oracle capability matrix researched and recorded in `specs/qrcode-test-fixtures.md` (zxing-cpp reads Micro QR + rMQR; Zint and Rust qrtool encode them; ZXing.Net/zxing-cpp counted as one lineage).

**Lessons learned**

- Recorded in `specs/qrcode-test-fixtures.md`: ZXing's internal encoder exposes the chosen mask pattern, enabling mask-exact decode assertions; cross-encoder matrix equality is not a valid conformance test (mask/segmentation freedom), so committed fixtures assert the decode direction; the public `QRCodeWriter` path is lossy for matrix extraction.

**Benchmarks**

- Not applicable: no `src/` (production) code changed — Phase 1 added a tool, test infrastructure, and committed fixtures only. Verified by full suite runs on both TFMs.

### Phase 2 — completed 2026-07-17 (rendering integration deferred, see below)

**Done**

- Public API: `MicroQrCodeGenerator` (string/span overloads plus a zero-allocation span destination API and `GetRequiredBufferSize`), `MicroQrVersion`, `MicroQrEccLevel` (with `ErrorDetectionOnly` for M1), `MicroQrCodeData` with the new "QRX" serialization container (magic + symbol type + width + height + packed bits).
- Pipeline in `Internals/MicroQr`: constants (capacity/codeword/format tables), bit-stream encoder (mode/count indicators, terminator, 0xEC/0x11 padding, M1/M3 half-codeword rules), module placer (single finder, edge timing, zigzag placement, 4-mask edge scoring applied on the fly without trial matrices, format info BCH+0x4445). Shared kernels reused as-is: `EccBinaryEncoder` (generator polynomials for ECC counts 2/5/6/8/10/14 build on demand), `BitWriter`, `TextAnalyzer`, `CharacterSets`. No Standard QR file was modified.
- Tests (+232, total 2,688 green on net8.0 + net10.0): format info vs the 32-pattern ISO table plus naive BCH reference; M1 golden vectors and the ISO "01234567" M2-L example; naive bit-string references; version auto-selection boundaries for every mode × ECC; illegal-combination rejection (M1+L, M2+Q, EDO escalation, mode/version mismatches); matrix structure invariants; span/class API parity; QRX serialization round-trip; and a full-pipeline extraction test (inverse zigzag + unmask + ECC recompute) covering all 8 version/ECC combinations.
- Docs: `specs/microqr-spec-map.md`, symbology status table, docs index, README (symbology table + FAQ with example), Micro QR capacity table in `docs/data-capacity.md`.

**External verification (encoder MVT)**

- zxing-cpp decoded all 9 spot-check symbols (every version × ECC combination plus a UTF-8 payload) generated by this encoder: `dotnet run --project tools/QrInteropFixtures -- spot-check-microqr`. The oracle is the pinned [ZXingCpp](https://www.nuget.org/packages/ZXingCpp) 0.5.2 .NET wrapper (bundled native binaries) — no Python/C++ toolchain needed after all.
- Zint is not available via scoop; acquiring it (vcpkg / official installer / ZXingCpp's zint-backed `BarcodeCreator`) moves to Phase 3 fixture work.

**Deferred within Phase 2**

- Rendering integration (`QRCodeImageBuilder` accepting Micro QR): not started; consumers use the module matrix directly. Scheduled as its own follow-up.

**Lessons learned**

- One widely used open-source Micro QR encoder does not apply the half-codeword rule for one of the M3 ECC levels, while the major open-source decoder expects it for both — cross-checking encoder claims against an independent decoder's *reading* code caught what a single reference would have hidden. (M1/M3's final 4-bit data codeword: high nibble carries data for RS, and only that nibble is emitted into the matrix.)
- The Micro QR function-module region collapses to `row == 0 || col == 0 || (row ≤ 8 && col ≤ 8)`, so placement and masking need no blocked-module bitmask at all — verified by data-module counts (36/80/132/192).
- Mask evaluation only reads the two symbol edges, so scoring all four masks needs no trial matrices: apply the mask predicate on the fly to 2·(size−1) modules and XOR.

**Benchmarks (new path; Standard QR untouched — no regression possible, only file additions)**

| Benchmark (net10.0, Release) | Mean | Allocated |
|---|---|---|
| MicroQr_Numeric_M2_Encode | 497 ns | 88 B (result object) |
| MicroQr_Alphanumeric_M3_Encode | 629 ns | 96 B |
| MicroQr_Byte_M4_Encode | 807 ns | 104 B |
| MicroQr_Numeric_M2_Encode (Span) | 491 ns | **0 B** |
| MicroQr_Alphanumeric_M3_Encode (Span) | 526 ns | **0 B** |
| MicroQr_Byte_M4_Encode (Span) | 645 ns | **0 B** |
| StandardQr_Numeric_V1_Encode (Span), same payload, for scale | 2,336 ns | 0 B |

## Risks Beyond the Test Strategy Document

- Renderer assumptions: `IconShape`/finder styling assume three finder patterns; rectangular output changes image sizing APIs. Audit in Phase 0.
- Serialization compatibility: QRR v1 streams must keep round-tripping forever; v2 header must be rejected cleanly by old readers.
- Oracle gaps: if an rMQR encode oracle disagrees with zxing-cpp decode, specification examples arbitrate (ISO/IEC 23941 Annex examples).
- Scope creep via Kanji mode: Micro QR M3/M4 and rMQR support Kanji; deferring it is fine but capacity tables and auto-version selection must be written with the mode column present.
