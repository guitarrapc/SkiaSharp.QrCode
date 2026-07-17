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
- External-encoder fixtures from two independent lineages, neither requiring a C++/Python toolchain: libzint (via the pinned ZXingCpp `BarcodeCreator` — verified working by `probe-creator`) and Rust `qrtool` (prebuilt release binary, pinned by version + checksum).
- Fixture sanity gate: every generated fixture must decode with zxing-cpp before it is committed, so a broken generator cannot poison the corpus.
- Error-correction flip tests; negative tests (Micro presented as Standard and vice versa).

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
- Zint is not available via scoop, but a follow-up probe (`tools/QrInteropFixtures -- probe-creator`) confirmed the pinned ZXingCpp package can CREATE Micro QR and rMQR through its compiled-in libzint writer, with reader round-trips passing — so a zint-lineage encoder oracle is available with no extra toolchain. Recorded in the oracle matrix; Phase 3 fixtures use it plus Rust `qrtool` (prebuilt binary) as a second lineage.

**Deferred within Phase 2**

- Rendering integration (`QRCodeImageBuilder` accepting Micro QR): not started; consumers use the module matrix directly. Scheduled as its own follow-up.

**Lessons learned**

- One widely used open-source Micro QR encoder does not apply the half-codeword rule for one of the M3 ECC levels, while the major open-source decoder expects it for both — cross-checking encoder claims against an independent decoder's *reading* code caught what a single reference would have hidden. (M1/M3's final 4-bit data codeword: high nibble carries data for RS, and only that nibble is emitted into the matrix.)
- The Micro QR function-module region collapses to `row == 0 || col == 0 || (row ≤ 8 && col ≤ 8)`, so placement and masking need no blocked-module bitmask at all — verified by data-module counts (36/80/132/192).
- Mask evaluation only reads the two symbol edges, so scoring all four masks needs no trial matrices: apply the mask predicate on the fly to 2·(size−1) modules and XOR.
- End-to-end APIs must keep symbology-specific generator and image-builder entry points (`QRCodeGenerator` / `MicroQrCodeGenerator` / future `RmQrCodeGenerator`) rather than selecting a symbol through overloaded ECC types. Version domains, legal ECC combinations, auto-fit policy, quiet-zone defaults, ECI support, finder topology, and rMQR's rectangular geometry are all symbology-specific; ECC alone cannot express those constraints. Share matrix rendering infrastructure behind those explicit APIs, but do not generalize the encoding hot path or force Standard-only image options (three finder patterns, H-level icon guidance) onto Micro QR.

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

### Phase 2 follow-up — placement pipeline optimization, completed 2026-07-17

**Done**

- Ported the Standard QR ModulePlacer techniques to Micro QR placement as a fused
  fast path `MicroQrModulePlacer.PlaceSymbol` (new partial file
  `MicroQrModulePlacer.PlaceSymbol.cs`); the per-module stage methods remain as
  the readable reference. `MicroQrCodeGenerator.WriteCoreModules` now makes one
  placer call. Kernel-level result (private micro-benchmark loop, 14 variants,
  4 rounds): 3.1-4.0x over the per-module pipeline, zero allocations.
- Design: <= 192-bit stream prepacked into 3 ulongs; closed-form column-pair
  segments (no per-module function predicate, no remaining-bits guard — stream
  length == free modules by the ISO tables, validated up front); mask scoring
  via bit-packed edges + static per-(size, mask) tables; sizes 13/15/17 run
  entirely on packed rows (one ulong per row) with a single SWAR unpack, size 11
  stays byte-domain with the per-module mask apply (the unpack pass never
  amortizes on a 121-byte matrix — measured, not assumed).
- Tests: `MicroQrModulePlacerParityTest` (fused vs naive reference, byte-identical
  matrix + mask, all 8 version/ECC combos x {all-zero, all-0xFF, 3 random seeds},
  plus argument-validation negative cases). Full suite green: 2,776 tests
  (net8.0 + net10.0), 0 failed.
- Spec map updated (`specs/microqr-spec-map.md`): PlaceSymbol rows + parity test
  references.

**Lessons learned**

- Mask scoring — not data placement — dominated the original pipeline (4 masks x
  2 edges x a mask-condition switch per module): packing the two scored edges
  into ulongs was the single biggest step at every size.
- Refs/`Unsafe` bounds-check elimination, worth ~15% on Standard QR, was WITHIN
  NOISE here: 121-289-byte matrices are fully L1-resident, so checked byte
  stores never sat on the critical path. The safe span version shipped.
- Representation choice is size-dependent: packed rows win at 13/15/17, byte
  domain wins at 11. Prepack fixed costs that amortize at 192 bits regress 37%
  at 36 bits — never trust a prepack win measured only on large inputs.
- At 60-200 ns/op, cross-method code layout noise is 4-7% (measured with a
  byte-identical canary variant) — accept/refute decisions need same-run ratios
  plus cross-run consistency.

**Benchmark delta (MicroQrEncode E2E, net10.0 Release, 12 iterations; before =
78405fc, after = this change; StandardQr control unchanged at ~2.6 us)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| MicroQr_Numeric_M2_Encode (Span) | 475.2 ns | 181.2 ns | -62% (2.6x) |
| MicroQr_Alphanumeric_M3_Encode (Span) | 627.7 ns | 258.6 ns | -59% (2.4x) |
| MicroQr_Byte_M4_Encode (Span) | 752.0 ns | 305.2 ns | -59% (2.5x) |
| MicroQr_Numeric_M2_Encode | 565.8 ns | 294.3 ns | -48% |
| MicroQr_Alphanumeric_M3_Encode | 680.1 ns | 368.8 ns | -46% |
| MicroQr_Byte_M4_Encode | 835.7 ns | 421.7 ns | -50% |

Allocations unchanged (Span paths 0 B; class paths allocate the result object
only).

### Phase 2 follow-up (2) — placement SIMD phase, completed 2026-07-17

**Done**

- Reopened the micro-benchmark loop with SIMD/CPU-instruction variants (rounds
  5-7, 25 variants total; the scalar-only constraint applied to the initial
  Micro QR implementation, not to optimization). Kernel result vs the
  per-module baseline: 3.6-5.8x (M4-M 680 -> 118 ns), vs the scalar fast path
  another 16-27%.
- Ship shape (`MicroQrModulePlacer.PlaceSymbol`, single code path for ALL
  sizes — the size-11 byte-domain dispatch was deleted): bulk stream pack,
  2-row-unrolled placement (all segment row counts are even), packed-edge
  scoring, mask apply fused into the unpack, SSSE3 16-module expand with a
  scalar SWAR fallback selected at runtime (`Ssse3.IsSupported`, net8.0+;
  netstandard TFMs compile the scalar path only).
- Tests: parity suite doubled — the scalar fallback is exercised explicitly via
  the internal `PlaceSymbolScalar` entry point on SIMD-capable machines. Full
  suite green: 2,854 tests (net8.0 + net10.0), 0 failed.

**Lessons learned**

- SSSE3 broadcast+shuffle+cmpeq beat the GFNI one-instruction bit-expand for
  16-module unpack on Zen 4: GFNI's expand is free but preparing its matrix
  operand (replicating data bytes) is not. Judge SIMD tricks by operand-prep
  cost, not the headline instruction.
- Fixing the biggest term moves the wall: after vectorizing the unpack, the
  serial placement loop dominated and a plain 2-row unroll was worth more
  (-14..-22%) than the SIMD step itself; independently-winning changes stopped
  composing once the critical path moved.
- Micro-architecture wins erase structural dispatch: the size-11 byte path won
  by 25% in the scalar phase, tied after the vector unpack, lost after the
  unroll. Re-test every dispatch boundary after each representation change.
- Convergence was declared when two variants running the IDENTICAL inner
  method measured 13% apart in the same run — candidate deltas below the
  identical-code layout spread are unattributable.

**Benchmark delta (MicroQrEncode E2E, net10.0 Release, Span API; before =
78405fc, scalar = first follow-up, SIMD = this change; two launches averaged)**

| Benchmark | Before | Scalar port | SIMD port | Total |
|---|---|---|---|---|
| MicroQr_Numeric_M2 (Span) | 475.2 ns | 181.2 ns | ~179 ns | 2.7x |
| MicroQr_Alphanumeric_M3 (Span) | 627.7 ns | 258.6 ns | ~228 ns | 2.8x |
| MicroQr_Byte_M4 (Span) | 752.0 ns | 305.2 ns | ~279 ns | 2.7x |

Allocations unchanged (Span paths 0 B). StandardQr control stable across runs.

### Phase 2 follow-up (3) — binary encoder register-accumulator rewrite, completed 2026-07-17 (#341)

**Done**

- Rewrote `MicroQrBinaryEncoder.EncodeDataCodewords`: the whole ≤ 128-bit data
  codeword stream is accumulated MSB-first in two ulong registers (`BitWriter`
  is no longer on the Micro QR path). Mode + count header fused into one append;
  numeric SWAR 3-digit groups (one 64-bit load + multiply per group, 9 digits
  per 30-bit append); alphanumeric pair-of-pairs appends via an unchecked
  128-entry value table; byte mode SSE2 8-char narrow; terminator / byte
  alignment / M1-M3 half-codeword zeros by position arithmetic only; 0xEC/0x11
  padding OR-ed from a phase-selected 128-bit constant under prefix masks;
  hand-rolled UTF-8 fallback matching `Encoding.UTF8` semantics including
  lone-surrogate U+FFFD (also removes the netstandard2.0 string + array
  allocation).
- Tests: `MicroQrBinaryEncoderParityTest` (exhaustive vs an independent naive
  bit-string reference: all 8 version/ECC combos × every length up to capacity
  × min/max/random contents, full Latin-1 range, UTF-8 fallbacks including
  surrogate pairs and lone surrogates), `MicroQrBitAccumulatorUnitTest`
  (Append/Append64 at positions 0/64 and word-straddling writes). Spec map row
  updated. Full suite green: 3,010 tests (net8.0 + net10.0), 0 failed.

**Lessons learned**

- Address exposure is per-LOCAL, not per-call-path: one cold NoInlining callee
  taking `ref hi/lo/pos` forced every hot-path append through the stack even
  though that callee was never called on hot paths. Fix = give the cold UTF-8
  path its own accumulator (whole-encode split), not just AggressiveInlining
  on the hot writers.
- Ref-iteration (`Unsafe.Add(ref c, i + k)`) lost to span indexing in the
  numeric loop: each access emits `lea + movsxd` where indexed spans fold into
  scaled address modes, and the loop guard had already eliminated the bounds
  checks. Don't reach for ref-iteration reflexively.
- SWAR digit grouping reads one char beyond the group (8-byte load over 3
  digits) — loop guards must carry that headroom explicitly (`i + 9 < length`).
- BDN DisassemblyDiagnoser flakes ~50% on this box; tiering off +
  `DOTNET_JitDisasm` is the reliable fallback for reading codegen.

**Benchmark delta** — kernel (private micro-benchmark loop, MicroQrBinaryEncode):
1.8-2.5x (15-31 ns → 6.7-17 ns across M1-M4 payloads); E2E MicroQrEncode −4 to
−14% (placement dominates after the earlier follow-ups). Branch-final E2E state
(net10.0 Release, two launches averaged, 2026-07-17):

| Benchmark | Mean | Allocated |
|---|---|---|
| MicroQr_Numeric_M2_Encode (Span) | ~175 ns | 0 B |
| MicroQr_Alphanumeric_M3_Encode (Span) | ~230 ns | 0 B |
| MicroQr_Byte_M4_Encode (Span) | ~293 ns | 0 B |
| StandardQr_Numeric_V1_Encode (Span), control | ~2.1 µs | 0 B |

## Risks Beyond the Test Strategy Document

- Renderer assumptions: `IconShape`/finder styling assume three finder patterns; rectangular output changes image sizing APIs. Audit in Phase 0.
- Serialization compatibility: QRR v1 streams must keep round-tripping forever; v2 header must be rejected cleanly by old readers.
- Oracle gaps: if an rMQR encode oracle disagrees with zxing-cpp decode, specification examples arbitrate (ISO/IEC 23941 Annex examples).
- Scope creep via Kanji mode: Micro QR M3/M4 and rMQR support Kanji; deferring it is fine but capacity tables and auto-version selection must be written with the mode column present.
