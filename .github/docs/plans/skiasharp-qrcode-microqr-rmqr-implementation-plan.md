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

Therefore each symbology gets its own generator entry (e.g. `MicroQRCodeGenerator`, `RmQrCodeGenerator`, or new method families on `QRCodeGenerator`, decided in Phase 0) with symbology-typed version and ECC parameters. `QRCodeGenerator.CreateQrCode` stays byte-for-byte unchanged.

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

Structural approach: keep the Standard QR pipeline untouched (it is heavily perf-tuned and zero-alloc); add sibling internal namespaces (e.g. `Internals/MicroQR`, `Internals/RmQr`) that reuse the leaf primitives. Do not introduce a polymorphic abstraction over the hot path. Blast-radius check: existing Standard QR tests and benchmarks must stay green and flat through every phase.

### Data model must be generalized once, up front

`QRCodeData` is square-only, versions 1-40, and its "QRR" serialization rejects sizes below 21, Micro QR (11-17 modules) and rMQR (rectangular) cannot reuse it. Phase 0 decides the shape (sibling types vs. a common symbol-data abstraction, serialization header v2 with symbol type + width + height). This decision is made once, anticipating rectangles, even though Micro QR alone would not need them.

### External oracle reality

- ZXing.Net (used in current in-CI cross tests) cannot decode Micro QR or rMQR. In-CI cross-verification is unavailable for the new symbologies.
- Committed fixtures from external encoders are therefore the primary conformance oracle in PR CI, exactly as the test strategy prescribes.
- Candidate oracles: zxing-cpp (decode: Micro QR + rMQR; the only broadly available OSS decoder), Zint (encode-only), qrcode rust crates (encode Micro QR), rmqrcode-python (encode rMQR), BoofCV (decode Micro QR). Phase 1 produces a verified capability matrix (which tool encodes/decodes which symbology, pinned versions) before any fixture is trusted.

## Implementation Order

Vertical slices per symbology: encoder first, matrix decoder immediately after (validates the same tables independently and installs a round-trip regression net), then image support (rendering + detection) to complete the symbology.

Micro QR before rMQR: square and small (4 versions, single finder), best oracle coverage, and it flushes out the data-model and serialization changes with the smaller step. rMQR then starts from a validated foundation and only adds the rectangular concerns.

Micro QR is completed END-TO-END (encode, render, matrix decode, image decode) before any rMQR phase starts: Micro QR ships as a stand-alone release, rMQR is not part of that release set, so nothing rMQR-shaped may gate it. Image detection within each symbology still comes last (hardest, riskiest), but Micro QR's no longer waits behind the rMQR encoder/decoder phases.

### Phase 0, API and data model spec

- Write `specs/` design doc: symbology model, generator entry points, decoder entry points (symbology restriction flags so Standard QR scanning perf is unaffected by default), data type shape, serialization format v2, Kanji-mode scoping decision (recommend: defer Kanji, document why).
- Mechanical prep refactor only if the spec demands it; zero behavior change, verified by existing tests + benchmark flatness.

Exit: spec reviewed; existing suite green; no benchmark regression.

### Phase 1, Fixture infrastructure, proven on Standard QR

- `tools/` fixture generator producing `case.json` + `case.matrix.txt` + `case.png` per the test strategy, with pinned tool versions (container-based for reproducibility).
- Oracle capability matrix verified and recorded.
- Generate a Standard QR corpus first and assert the EXISTING implementation against it. This validates the harness itself against a trusted implementation before it is used to judge new code.
- Fixture-driven test infrastructure (loader, matrix comparer, manifest schema) lands in the test project.

Exit: Standard QR fixture tests green in PR CI; regeneration is one documented command.

### Phase 2, Micro QR encoder

- Tables (capacity, ECC, format), M1-M4 encoding pipeline reusing BitWriter/RS kernels, new data type, square rendering support, QRR-v2 serialization.
- Matrix conformance tests against Phase 1 fixtures (all versions × legal ECC × Numeric/Alphanumeric/Byte, capacity boundaries, illegal-combination rejection).
- Spot-check: zxing-cpp decodes generated symbols (manual or interop CI, not PR CI).

Exit: test strategy §14 encoder MVT satisfied for Micro QR. Releasable.

### Phase 3, Micro QR matrix decoder

- Module matrix → payload: format parsing, unmasking, codeword extraction, RS correction, bitstream decoding. Same internal boundary as `QRMatrixDecoder`.
- External-encoder fixtures from two independent lineages, neither requiring a C++/Python toolchain: libzint (via the pinned ZXingCpp `BarcodeCreator`, verified working by `probe-creator`) and Rust `qrtool` (prebuilt release binary, pinned by version + checksum).
- Fixture sanity gate: every generated fixture must decode with zxing-cpp before it is committed, so a broken generator cannot poison the corpus.
- Error-correction flip tests; negative tests (Micro presented as Standard and vice versa).

Exit: decoder MVT (matrix-level rows) satisfied; round-trip regression net in place.

### Phase 4, Micro QR image support (rendering + image detection)

Completes the Micro QR feature set so it can ship without waiting for rMQR. Two sub-parts, rendering first (smaller, and it is the release-blocking Image API):

- 4a, Rendering integration (the Phase 2 deferral): the image-building surface (`QRCodeImageBuilder` / renderer / extension entry points) accepts `MicroQRCodeData`. Micro QR-correct defaults and restrictions per the Phase 2 lesson: 2-module quiet zone (not 4), no icon overlay / finder-styling options that assume three finder patterns or H-level masking headroom. Playground (WASM) gains Micro QR generation as the living demo; NativeAOT/WASM CI covers the path.
- 4b, Image detection (moved up from the former Phase 6a): single-finder search strategy (different from three-finder Standard QR), sampling, `MicroQRCodeDecoder` image overloads mirroring the Standard QR image path. Clean + degraded PNG fixtures; deterministic degradation tests per test strategy §7, representative subset only. Decoder entry defaults keep Standard QR-only scanning at current performance; Micro QR scanning is opt-in or explicitly-typed.

Exit: Micro QR is feature-complete (encode, render, matrix decode, image decode); decoder MVT image-level rows and degradation matrix green for Micro QR; Standard QR rendering/decoding benchmarks flat. **Micro QR releasable as a stand-alone release** (physical device acceptance per test strategy §11 runs for the Micro QR subset as release acceptance).

### Phase 5, rMQR encoder

- Rectangular tables (32 sizes, ECC M/H), 18-bit format info, finder + sub-finder + edge timing placement, rectangular rendering, version auto-fit strategy (width-first / height-first preference exposed in API).
- Matrix conformance against fixtures (all 32 sizes at least once; boundary payloads).

Exit: encoder MVT satisfied for rMQR.

### Phase 6, rMQR matrix decoder

- As Phase 3, for rectangular matrices (width/height API).

Exit: decoder MVT (matrix-level) satisfied for rMQR.

### Phase 7, rMQR image detection and sampling

- rMQR detection (finder + sub-finder, extreme aspect ratios), sampling; rectangular rendering integration in the image-building surface if not already landed with Phase 5.
- Deterministic degradation tests per test strategy §7, representative subset only.
- Decoder entry defaults keep Standard QR-only scanning at current performance; rMQR is opt-in or explicitly-typed.

Exit: decoder MVT image-level rows satisfied; degradation matrix green for rMQR.

### Phase 8, Interop CI (parallel track, starts after Phase 2)

- Scheduled/manual workflow: pinned zxing-cpp + encoders, live round-trips both directions, committed-fixture drift detection.

### Phase 9, Physical device acceptance

- Per test strategy §11, run per release: the Micro QR subset gates the Micro QR stand-alone release (Phase 4 exit); the rMQR / combined set gates the rMQR release (after Phase 7).

## Cross-cutting

- Test layout (decided in Phase 0): the single test assembly is organized
  symbology-first, mirroring `src`, `Shared/`, `StandardQr/`, `Rendering/`, with
  `MicroQR/`, `RmQr/`, and `Fixtures/` added by their phases. No per-symbology test
  projects: `InternalsVisibleTo` targets one assembly, the suite runs in ~20s, and
  TUnit filters select by class name regardless of folders. The one planned exception
  is live interop tests with external native dependencies (zxing-cpp etc.), which get
  their own project excluded from PR CI; committed-fixture tests stay in the main
  assembly.
- Test-first development applies to every phase (project rule).
- Each phase updates the relevant `specs/` docs with lessons learned (project rule).
- Playground (WASM) gains Micro QR generation in Phase 4a and rMQR generation after Phase 5 as the living demo; NativeAOT/WASM CI must cover the new paths.
- Benchmarks: new symbology paths get end-to-end benchmarks; Standard QR benchmarks guard against regression at every phase.
- Progress logging (mandatory): when a phase completes, append an entry to the Progress log below recording what was done, lessons learned, and benchmark deltas, or an explicit statement of why benchmarks are not applicable (e.g. no `src/` change).

## Progress log

### Phase 0, completed 2026-07-16

**Done**

- Symbology architecture spec: `specs/qrcode-symbologies.md` (shared vs per-symbology inventory, dependency rules, API/data-model direction, Kanji deferral).
- `src` reorganized: Standard-QR-specific internals moved to `Internals/StandardQr` (12 files); `EncodingModeExtensions` split out of the shared `EncodingMode`; character-class predicates extracted from `QRCodeConstants` into shared `CharacterSets`.
- Tests reorganized symbology-first in the single assembly: `Shared/` (9), `StandardQr/` (21), `Rendering/` (10); `QrCodeConstantsUnitTest` renamed to `Shared/CharacterSetsUnitTest`.
- Specs renamed symbology-first (`standardqr-spec-map.md`, `standardqr-decoder.md`), `docs_authoring_guidelines.md` created (doc-type templates, naming, linking policy), documentation index added at `.github/docs/README.md`, README gained Supported Symbologies + Micro QR/rMQR FAQ.

**Lessons learned**

- The namespace dependency rule immediately surfaced two hidden couplings: shared `TextAnalyzer` depended on character predicates living inside the Standard QR constants class, and `GetCountIndicatorLength` looked shared but encodes Standard QR version thresholds. Both recorded in `qrcode-symbologies.md`.
- The pre-existing `QrCodeConstantsUnitTest` turned out to test only the (now shared) character sets, test names drift from their subjects unless reorganizations re-check them.

**Benchmarks**

- Not run: changes were mechanical moves (namespace/type relocation of static members with identical bodies and inlining attributes); no signature or algorithm change. Full suite 2,370 tests green on net8.0 + net10.0 before and after.

### Phase 1, completed 2026-07-16

**Done**

- `tools/QRInteropFixtures`: fixture generator with a plug-in `IFixtureGenerator` interface; first generator is ZXing.Net 0.16.11 in-process (via `ZXing.QrCode.Internal.Encoder` for the core matrix plus version/mode/mask metadata). Regeneration is one command: `dotnet run --project tools/QRInteropFixtures -- regenerate`.
- Committed Standard QR corpus: 21 deterministic cases (all modes × all ECC levels, v1-L alphanumeric capacity boundary, v10/v15/v25 mid sizes, v40-L at exactly 7089 digits, UTF-8/ECI Japanese + emoji), 63 files ≈ 178 KB.
- Fixture test infrastructure: `FixtureLoader` (manifest schema + matrix parser) and `StandardQrFixtureTest` decoding every fixture through both the matrix path and the PNG image path, asserting payload, version, ECC, and mask pattern (86 tests across both TFMs). Full suite: 2,456 green.
- Oracle capability matrix researched and recorded in `specs/qrcode-test-fixtures.md` (zxing-cpp reads Micro QR + rMQR; Zint and Rust qrtool encode them; ZXing.Net/zxing-cpp counted as one lineage).

**Lessons learned**

- Recorded in `specs/qrcode-test-fixtures.md`: ZXing's internal encoder exposes the chosen mask pattern, enabling mask-exact decode assertions; cross-encoder matrix equality is not a valid conformance test (mask/segmentation freedom), so committed fixtures assert the decode direction; the public `QRCodeWriter` path is lossy for matrix extraction.

**Benchmarks**

- Not applicable: no `src/` (production) code changed, Phase 1 added a tool, test infrastructure, and committed fixtures only. Verified by full suite runs on both TFMs.

### Phase 2, completed 2026-07-17 (rendering integration deferred, see below)

**Done**

- Public API: `MicroQRCodeGenerator` (string/span overloads plus a zero-allocation span destination API and `GetRequiredBufferSize`), `MicroQRVersion`, `MicroQREccLevel` (with `ErrorDetectionOnly` for M1), `MicroQRCodeData` with the new "QRX" serialization container (magic + symbol type + width + height + packed bits).
- Pipeline in `Internals/MicroQR`: constants (capacity/codeword/format tables), bit-stream encoder (mode/count indicators, terminator, 0xEC/0x11 padding, M1/M3 half-codeword rules), module placer (single finder, edge timing, zigzag placement, 4-mask edge scoring applied on the fly without trial matrices, format info BCH+0x4445). Shared kernels reused as-is: `EccBinaryEncoder` (generator polynomials for ECC counts 2/5/6/8/10/14 build on demand), `BitWriter`, `TextAnalyzer`, `CharacterSets`. No Standard QR file was modified.
- Tests (+232, total 2,688 green on net8.0 + net10.0): format info vs the 32-pattern ISO table plus naive BCH reference; M1 golden vectors and the ISO "01234567" M2-L example; naive bit-string references; version auto-selection boundaries for every mode × ECC; illegal-combination rejection (M1+L, M2+Q, EDO escalation, mode/version mismatches); matrix structure invariants; span/class API parity; QRX serialization round-trip; and a full-pipeline extraction test (inverse zigzag + unmask + ECC recompute) covering all 8 version/ECC combinations.
- Docs: `specs/microqr-spec-map.md`, symbology status table, docs index, README (symbology table + FAQ with example), Micro QR capacity table in `docs/data-capacity.md`.

**External verification (encoder MVT)**

- zxing-cpp decoded all 9 spot-check symbols (every version × ECC combination plus a UTF-8 payload) generated by this encoder: `dotnet run --project tools/QRInteropFixtures -- spot-check-microqr`. The oracle is the pinned [ZXingCpp](https://www.nuget.org/packages/ZXingCpp) 0.5.2 .NET wrapper (bundled native binaries), no Python/C++ toolchain needed after all.
- Zint is not available via scoop, but a follow-up probe (`tools/QRInteropFixtures -- probe-creator`) confirmed the pinned ZXingCpp package can CREATE Micro QR and rMQR through its compiled-in libzint writer, with reader round-trips passing, so a zint-lineage encoder oracle is available with no extra toolchain. Recorded in the oracle matrix; Phase 3 fixtures use it plus Rust `qrtool` (prebuilt binary) as a second lineage.

**Deferred within Phase 2**

- Rendering integration (`QRCodeImageBuilder` accepting Micro QR): not started; consumers use the module matrix directly. Now scheduled as Phase 4a.

**Lessons learned**

- One widely used open-source Micro QR encoder does not apply the half-codeword rule for one of the M3 ECC levels, while the major open-source decoder expects it for both, cross-checking encoder claims against an independent decoder's *reading* code caught what a single reference would have hidden. (M1/M3's final 4-bit data codeword: high nibble carries data for RS, and only that nibble is emitted into the matrix.)
- The Micro QR function-module region collapses to `row == 0 || col == 0 || (row ≤ 8 && col ≤ 8)`, so placement and masking need no blocked-module bitmask at all, verified by data-module counts (36/80/132/192).
- Mask evaluation only reads the two symbol edges, so scoring all four masks needs no trial matrices: apply the mask predicate on the fly to 2·(size−1) modules and XOR.
- End-to-end APIs must keep symbology-specific generator and image-builder entry points (`QRCodeGenerator` / `MicroQRCodeGenerator` / future `RmQrCodeGenerator`) rather than selecting a symbol through overloaded ECC types. Version domains, legal ECC combinations, auto-fit policy, quiet-zone defaults, ECI support, finder topology, and rMQR's rectangular geometry are all symbology-specific; ECC alone cannot express those constraints. Share matrix rendering infrastructure behind those explicit APIs, but do not generalize the encoding hot path or force Standard-only image options (three finder patterns, H-level icon guidance) onto Micro QR.

**Benchmarks (new path; Standard QR untouched, no regression possible, only file additions)**

| Benchmark (net10.0, Release) | Mean | Allocated |
|---|---|---|
| MicroQR_Numeric_M2_Encode | 497 ns | 88 B (result object) |
| MicroQR_Alphanumeric_M3_Encode | 629 ns | 96 B |
| MicroQR_Byte_M4_Encode | 807 ns | 104 B |
| MicroQR_Numeric_M2_Encode (Span) | 491 ns | **0 B** |
| MicroQR_Alphanumeric_M3_Encode (Span) | 526 ns | **0 B** |
| MicroQR_Byte_M4_Encode (Span) | 645 ns | **0 B** |
| StandardQr_Numeric_V1_Encode (Span), same payload, for scale | 2,336 ns | 0 B |

### Phase 2 follow-up, placement pipeline optimization, completed 2026-07-17

**Done**

- Ported the Standard QR ModulePlacer techniques to Micro QR placement as a fused
  fast path `MicroQRModulePlacer.PlaceSymbol` (new partial file
  `MicroQRModulePlacer.PlaceSymbol.cs`); the per-module stage methods remain as
  the readable reference. `MicroQRCodeGenerator.WriteCoreModules` now makes one
  placer call. Kernel-level result (private micro-benchmark loop, 14 variants,
  4 rounds): 3.1-4.0x over the per-module pipeline, zero allocations.
- Design: <= 192-bit stream prepacked into 3 ulongs; closed-form column-pair
  segments (no per-module function predicate, no remaining-bits guard, stream
  length == free modules by the ISO tables, validated up front); mask scoring
  via bit-packed edges + static per-(size, mask) tables; sizes 13/15/17 run
  entirely on packed rows (one ulong per row) with a single SWAR unpack, size 11
  stays byte-domain with the per-module mask apply (the unpack pass never
  amortizes on a 121-byte matrix, measured, not assumed).
- Tests: `MicroQRModulePlacerParityTest` (fused vs naive reference, byte-identical
  matrix + mask, all 8 version/ECC combos x {all-zero, all-0xFF, 3 random seeds},
  plus argument-validation negative cases). Full suite green: 2,776 tests
  (net8.0 + net10.0), 0 failed.
- Spec map updated (`specs/microqr-spec-map.md`): PlaceSymbol rows + parity test
  references.

**Lessons learned**

- Mask scoring, not data placement, dominated the original pipeline (4 masks x
  2 edges x a mask-condition switch per module): packing the two scored edges
  into ulongs was the single biggest step at every size.
- Refs/`Unsafe` bounds-check elimination, worth ~15% on Standard QR, was WITHIN
  NOISE here: 121-289-byte matrices are fully L1-resident, so checked byte
  stores never sat on the critical path. The safe span version shipped.
- Representation choice is size-dependent: packed rows win at 13/15/17, byte
  domain wins at 11. Prepack fixed costs that amortize at 192 bits regress 37%
  at 36 bits, never trust a prepack win measured only on large inputs.
- At 60-200 ns/op, cross-method code layout noise is 4-7% (measured with a
  byte-identical canary variant), accept/refute decisions need same-run ratios
  plus cross-run consistency.

**Benchmark delta (MicroQREncode E2E, net10.0 Release, 12 iterations; before =
78405fc, after = this change; StandardQr control unchanged at ~2.6 us)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| MicroQR_Numeric_M2_Encode (Span) | 475.2 ns | 181.2 ns | -62% (2.6x) |
| MicroQR_Alphanumeric_M3_Encode (Span) | 627.7 ns | 258.6 ns | -59% (2.4x) |
| MicroQR_Byte_M4_Encode (Span) | 752.0 ns | 305.2 ns | -59% (2.5x) |
| MicroQR_Numeric_M2_Encode | 565.8 ns | 294.3 ns | -48% |
| MicroQR_Alphanumeric_M3_Encode | 680.1 ns | 368.8 ns | -46% |
| MicroQR_Byte_M4_Encode | 835.7 ns | 421.7 ns | -50% |

Allocations unchanged (Span paths 0 B; class paths allocate the result object
only).

### Phase 2 follow-up (2), placement SIMD phase, completed 2026-07-17

**Done**

- Reopened the micro-benchmark loop with SIMD/CPU-instruction variants (rounds
  5-7, 25 variants total; the scalar-only constraint applied to the initial
  Micro QR implementation, not to optimization). Kernel result vs the
  per-module baseline: 3.6-5.8x (M4-M 680 -> 118 ns), vs the scalar fast path
  another 16-27%.
- Ship shape (`MicroQRModulePlacer.PlaceSymbol`, single code path for ALL
  sizes, the size-11 byte-domain dispatch was deleted): bulk stream pack,
  2-row-unrolled placement (all segment row counts are even), packed-edge
  scoring, mask apply fused into the unpack, SSSE3 16-module expand with a
  scalar SWAR fallback selected at runtime (`Ssse3.IsSupported`, net8.0+;
  netstandard TFMs compile the scalar path only).
- Tests: parity suite doubled, the scalar fallback is exercised explicitly via
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
  method measured 13% apart in the same run, candidate deltas below the
  identical-code layout spread are unattributable.

**Benchmark delta (MicroQREncode E2E, net10.0 Release, Span API; before =
78405fc, scalar = first follow-up, SIMD = this change; two launches averaged)**

| Benchmark | Before | Scalar port | SIMD port | Total |
|---|---|---|---|---|
| MicroQR_Numeric_M2 (Span) | 475.2 ns | 181.2 ns | ~179 ns | 2.7x |
| MicroQR_Alphanumeric_M3 (Span) | 627.7 ns | 258.6 ns | ~228 ns | 2.8x |
| MicroQR_Byte_M4 (Span) | 752.0 ns | 305.2 ns | ~279 ns | 2.7x |

Allocations unchanged (Span paths 0 B). StandardQr control stable across runs.

### Phase 2 follow-up (3), binary encoder register-accumulator rewrite, completed 2026-07-17 (#341)

**Done**

- Rewrote `MicroQRBinaryEncoder.EncodeDataCodewords`: the whole ≤ 128-bit data
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
- Tests: `MicroQRBinaryEncoderParityTest` (exhaustive vs an independent naive
  bit-string reference: all 8 version/ECC combos × every length up to capacity
  × min/max/random contents, full Latin-1 range, UTF-8 fallbacks including
  surrogate pairs and lone surrogates), `MicroQRBitAccumulatorUnitTest`
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
  digits), loop guards must carry that headroom explicitly (`i + 9 < length`).
- BDN DisassemblyDiagnoser flakes ~50% on this box; tiering off +
  `DOTNET_JitDisasm` is the reliable fallback for reading codegen.

**Benchmark delta**, kernel (private micro-benchmark loop, MicroQRBinaryEncode):
1.8-2.5x (15-31 ns → 6.7-17 ns across M1-M4 payloads); E2E MicroQREncode −4 to
−14% (placement dominates after the earlier follow-ups). Branch-final E2E state
(net10.0 Release, two launches averaged, 2026-07-17):

| Benchmark | Mean | Allocated |
|---|---|---|
| MicroQR_Numeric_M2_Encode (Span) | ~175 ns | 0 B |
| MicroQR_Alphanumeric_M3_Encode (Span) | ~230 ns | 0 B |
| MicroQR_Byte_M4_Encode (Span) | ~293 ns | 0 B |
| StandardQr_Numeric_V1_Encode (Span), control | ~2.1 µs | 0 B |

### Phase 2 follow-up (4), placement PEXT/PDEP phase, completed 2026-07-17

**Done**

- Reopened the placement micro-benchmark loop (rounds 8-11, variants V26-V37 in
  the private findings log). The round-7 audit's "the placement recurrence is
  genuinely serial" was wrong one level up: the zigzag is a FIXED bit
  permutation per size, so each row's data bits are 3x (PEXT gather + PDEP
  scatter) from static per-(size, row) masks, branch-free, no cross-row
  dependency. Composed with a per-size stream word count dispatch (M1 = 1 word,
  M2 = 2), a 32-module AVX2 unpack, a bit-reversal-table format-row insert and
  a 32-entry format word table. Kernel same-run result vs the SSSE3 pipeline:
  M1 -5%, M2 -10%, M3 -22%, M4 -29% (M4-M 117.5 -> 86.1 ns; vs the per-module
  baseline 8.4x). Refuted along the way: SIMD edge extraction (V28), bulk-shift
  ECC merge (V29 and again as V35's enabler), cross-row tail-window store (V35).
- Ship shape: a new top runtime tier `PlaceCoreBmi2` above the vector pipeline,
  selected by `Avx2.IsSupported && Bmi2.X64.IsSupported` plus a one-time CPUID
  vendor/family check (PDEP/PEXT are microcoded on AMD before Zen 3, family
  0x19 gate; Intel always fast). The mid tier (`PlaceCoreVector`) now serves
  both SSSE3 and ARM64 NEON (`AdvSimd.Arm64`) with a shared pipeline, only
  the 16-module bit-expand idiom differs (NEON kernel vs scalar on ARM64:
  -21..-24%, 4.8-5.9x vs the per-module baseline). The vector and scalar tiers
  also gained the format word table + reversal-table row-8 insert (shared
  BuildPackedRows).
- Tests: parity suite extended with named-entry coverage for every tier
  (`PlaceSymbolBmi2`, `PlaceSymbolSsse3`, `PlaceSymbolAdvSimd`,
  `PlaceSymbolScalar`); full suite green on net8.0 + net10.0 (1,746/1,758
  passed, rest skipped-by-design); zxing-cpp spot-check re-run with the BMI2
  kernel active: 9/9 decoded.

**Lessons learned**

- "The recurrence is serial" must be scoped: the DEPENDENCE was in the
  incumbent walk, not the data flow, a static permutation table dissolved it
  (M4 placement -39% in one variant). Ask which claim the audit actually proved.
- Guaranteed-zero PEXT/PDEP pairs are not free (loads + BMI ops still issue):
  per-size word specialization recovered -14% at M1.
- Flag-parameterized shared cores poison attribution both ways (specialization
  tax vs dead-branch tax); in-family flag toggles are the only layout-free
  reads, specialized copies the only fair cross-variant reads.
- Cross-run layout swings reached 19% on a 50 ns kernel and ±11% on the 2 µs
  E2E control, same-run ratios and ABBA-ordered E2E runs are mandatory at
  this scale.
- De-fusing a fused pass to enable a downstream trick can cost more than the
  trick saves (V35): price the enabling plumbing, not just the headline change.

**Benchmark delta (MicroQREncode E2E, net10.0 Release, Span API; ABBA-ordered
runs x (3 launches x 15 iterations), before/after averaged over 2 runs each)**

| Benchmark | Before | After | Delta |
|---|---|---|---|
| MicroQR_Numeric_M2 (Span) | ~155 ns | ~155 ns | ±0% |
| MicroQR_Alphanumeric_M3 (Span) | ~211 ns | ~183 ns | -13% |
| MicroQR_Byte_M4 (Span) | ~309 ns | ~200 ns | -35% |

Allocations unchanged (Span paths 0 B). The StandardQr control swung ±11%
across all four runs (machine-level noise floor); the M3/M4 wins exceed that
spread in every pairing, M2's placer share is too small to surface.

### Phase 3, completed 2026-07-17

**Done**

- Public API: `MicroQRCodeDecoder` (`MicroQRCodeData` / module-matrix / zero-allocation
  span overloads plus `GetMaxDecodedLength`), `MicroQRCodeDecodeInfo` (status shared
  with Standard QR, Micro-typed version/ECC, mask 0-3, corrected-error count).
  Uniform quiet-zone stripping via the finder corner (the Standard QR dark-bounding-box
  trick does not work, see lessons).
- Pipeline in `Internals/MicroQR`, same internal boundary as `QRMatrixDecoder`:
  `MicroQRFormatInformationDecoder` (single 15-bit copy, 32-candidate Hamming match,
  ≤ 3 bit errors, format-version × matrix-size cross-check),
  `MicroQRMatrixDecoder` (inverse zigzag + on-the-fly unmask reusing the encoder's own
  `IsFunctionModule`/`GetMaskBit`, single RS block, no deinterleave, stackalloc-only),
  `MicroQRBinaryDecoder` (mode/count framing, terminator = zero-count Numeric segment,
  stream bounded by dataBitCount for the M1/M3 half codeword, Kanji → UnsupportedContent),
  and a new `MicroQRConstants` ISO Table 9 error-correction-capacity table (2t + p = ecc)
  enforced after RS correction. Segment payload decoding (numeric/alphanumeric/byte
  groups, UTF-8/Latin-1 heuristics) extracted from `QRBinaryDecoder` into shared
  `Internals/BinaryDecoders/SegmentDecoders`, the only Standard QR file touched,
  mechanically, verified flat by benchmark.
- External-encoder fixtures, two independent lineages, exactly as planned:
  `Fixtures/MicroQR/zint-libzint/` (17 cases; pinned ZXingCpp `BarcodeCreator`,
  `version=`/`ecLevel=` options honored, module-exact `ToImage(Scale=1, AddQuietZones=false)`)
  and `Fixtures/MicroQR/qrtool/` (18 cases; qrtool 0.13.2 prebuilt binary pinned by
  version + SHA-256 via `tools/QRInteropFixtures/get-qrtool.ps1`, module-exact
  `--type ascii` output). Every fixture passed the zxing-cpp sanity gate
  (decode + version/ECC cross-check) before being written; the gate's reader supplies
  the manifest mask pattern (`Extra("DataMask")`), externally sourced, and our decoder
  agrees with it on all 35 fixtures.
- Tests (+350, total 3,360 on net8.0 + net10.0, 0 failed, 20 pre-existing
  environment-conditional skips): format decoder exhaustively vs a naive
  nearest-candidate reference over the full 15-bit space; bitstream golden vectors
  (M1 hand-derived, ISO "01234567" M2-L) + encoder round-trips + malformed-stream
  negatives; public-API round trips (all versions × ECC × modes × quiet zones, span
  parity); damage tests per equivalence class of the capacity check, within t,
  the t < errors ≤ ⌊ecc/2⌋ misdecode-protection class (RS could correct, spec says
  reject), beyond RS range, M1 detection-only, format damage within/beyond BCH
  distance, format-vs-size contradiction; cross-symbology rejection both directions;
  committed-fixture corpus decode.
- Docs: microqr-spec-map decoding section + lessons, fixture record (corpus, sanity
  gate, oracle matrix: qrtool now verified, libzint UTF-8 limits), symbology status
  table, README (decoder example + FAQ).

**Lessons learned**

- The ECC codeword counts hide misdecode-protection codewords p (ISO Table 9): a
  decoder wired to full RS strength silently corrects ⌊ecc/2⌋ errors where the spec
  allows t (2 vs 1 on M2-L). The capacity cap must be an explicit post-correction
  check, and its false-positive class (RS succeeds, spec forbids) needs its own tests —
  a naive port of the Standard QR decode loop would have shipped this wrong.
- The Micro QR terminator is exactly a Numeric mode indicator plus an all-zero count
  field ((v−1) + (v+2) = 2v+1 bits), so "zero-count Numeric segment ends the stream"
  decodes terminators, including capacity-truncated ones, with no special scanning.
- Quiet-zone stripping cannot reuse the Standard QR dark-bounding-box trick: with a
  single finder the right/bottom edges are data modules with no darkness guarantee
  (mask scoring only prefers dark edges). The top-left dark module is the finder
  corner; a uniform border gives the core size from there.
- Encoder oracles constrain payload freedom in surprising ways: libzint (via the
  ZXingCpp wrapper) hard-rejects UTF-8 Micro QR input and transliterated a Latin-1
  "naïve café" round trip to "naive cafe", the per-fixture decode gate caught this
  before it could poison the corpus, which is precisely why the gate exists. UTF-8
  coverage rides the qrtool lineage.
- zxing-cpp's reader exposes `Extra("Version"/"EcLevel"/"DataMask")`: fixture manifests
  can carry externally-sourced mask metadata even when the producing encoder reports
  nothing, and reader-sourced values additionally prove the bits are on the wire.

**Benchmark delta**, new decode path (MicroQRDecode E2E, net10.0 Release, 8 iterations;
encode numbers for the same payloads shown for comparison):

| Benchmark | Decode | Encode (same payload) | Allocated |
|---|---|---|---|
| MicroQR_Numeric_M2 (Span) | 374 ns | ~175 ns | 0 B |
| MicroQR_Alphanumeric_M3 (Span) | 511 ns | ~230 ns | 0 B |
| MicroQR_Byte_M4 (Span) | 684 ns | ~293 ns | 0 B |
| MicroQR_Numeric_M2 (string) | 382 ns |, | 48 B (result string) |
| StandardQr_Numeric_V1_Decode (Span), control | 1,148 ns |, | 0 B |

Standard QR decode guard (QrCodeDecodeEndToEnd, before = 359c304 via worktree,
after = this change): all scenarios within noise or faster (Matrix_Short_M
1.247 → 1.281 µs at 15 iterations, +2.7% with overlapping StdDev; Url/Long_L/Long_H/Image
all measured faster on the after run), allocations 0 B on both sides, the
`SegmentDecoders` extraction did not regress the Standard QR path.

### Phase 4, completed 2026-07-17

**Done (4a, rendering integration)**

- Public API: `MicroQRCodeImageBuilder` (fluent + static helpers mirroring
  `QRCodeImageBuilder`: PNG/JPEG/WEBP bytes/stream/IBufferWriter, SVG with
  viewBox/crispEdges injection; Micro-typed `WithErrorCorrection(MicroQREccLevel)` /
  `WithVersion(MicroQRVersion)`; quiet zone default 2 per spec; no icon overlay or
  finder-styling options, single finder, no ECC headroom),
  `QRCodeRenderer.Render(…, MicroQRCodeData, …)` low-level overload, and
  `SKCanvas.Render(MicroQRCodeData, …)` extensions.
- Sharing: draw loops generalized over an internal `IModuleMatrixView` struct view
  (generic specialization, no virtual dispatch; Standard QR call sites unchanged),
  canvas layout math extracted to `QrImageLayout`, both builders delegate.
- Tests (+94): `MicroQRCodeImageBuilderUnitTest`, full-matrix module-to-pixel
  parity (every module center vs `MicroQRCodeData`, all 8 version/ECC combos,
  custom colors, circle shapes), quiet zone defaults/overrides, layout
  (pad/center/too-small/odd padding), SVG structure (viewBox, crispEdges on/off),
  static helper signatures, validation negatives, renderer/extension entry points.

**Done (4b, image detection)**

- Public API: `MicroQRCodeDecoder.TryDecode(SKBitmap, …)` and
  `TryDecodeImage(luminance, …)` (string and zero-allocation span-destination
  overloads). `QRCodeDecoder` remains Standard QR-only, Micro QR scanning is
  explicitly-typed, so default scanning perf is untouched.
- Pipeline (`Internals/MicroQR/MicroQRImageDecoder`): shared Otsu threshold →
  shared 1:1:3:1:1 finder scan collecting ALL cross-checked candidates (new
  `FinderPatternFinder.FindCandidates`) → module size from dark-light-dark runs
  through the single finder center → axis-aligned grid sampling trying
  sizes M4..M1 × 4 right-angle orientations × transpose (full dihedral coverage
  for mirrored captures) → `MicroQRMatrixDecoder` arbitrates (format/RS/Table 9
  capacity kill wrong grids) → inverted retry for reflectance reversal.
- Detection primitives lifted to `Internals.ImageDecoders` (`Binarizer`,
  `FinderPatternFinder`) exactly on the spec's second-consumer trigger; the
  namespace dependency rule holds (`Internals.MicroQR` no longer references
  `Internals.StandardQr`).
- Tests (+130, full suite 3,866 on net8.0 + net10.0, 0 failed, 100
  skipped-by-design): clean renders all versions × ECC; module pixel sizes 3-13;
  non-integer scale; translation; quiet zone 1/4; 90/180/270 rotation; mirror;
  inverted colors; JPEG q60 / low contrast / seeded additive noise; luminance +
  span-destination parity; negatives (Standard↔Micro cross-rejection, blank,
  too-small, null); committed fixture corpus (35 PNGs, both lineages) through
  the image path.
- Playground: symbology selector (QR / Micro QR) with symbology-driven ECC/version
  selects, quiet-zone default switching, finder/logo controls hidden for Micro QR,
  Micro-aware stats and benchmark panel; decode panel and the generated-image
  self-check now fall back to the Micro QR decoder. Verified in-browser on the
  published WASM build (generation + ✓ self-check decode round-trip). BlazorWasm
  sample gained the same symbology switch (live SKCanvasView preview + PNG/SVG
  export via `CreateMicroBuilder`).
- Docs: microqr-spec-map (Image Rendering + Image Detection sections, supported
  envelope), qrcode-symbologies (status table, lifted primitives, scope
  decisions), fixture record, README (symbology table, image API + scanning
  examples, FAQ).

**Lessons learned**

- Trying every (size × orientation × transpose) grid and letting the matrix
  decoder arbitrate beats bespoke orientation detection at Micro QR scale: wrong
  grids die at the 32-candidate format Hamming check in ~µs, so 32 attempts cost
  less than one robust orientation estimator, and the negative tests (Standard
  QR must not decode) come out free.
- The supported envelope must be stated, not implied: a single finder cannot
  anchor small-angle rotation or perspective recovery (three finders can), so the
  spec map and API docs say so explicitly instead of letting users infer tiers
  from Standard QR.
- An author CSS rule (`.field { display: flex }`) silently overrides the UA
  stylesheet's `[hidden] { display: none }`, the Playground's attribute-based
  hiding never worked for field rows (latent for `#corner-row` too). Fixed
  globally with `[hidden] { display: none !important; }`.
- Styled symbols (rounded modules < 100%, gradients) break the 1:1:3:1:1 run
  continuity and go undetected, same failure mode as Standard QR, but Micro QR
  has no ECC headroom to spare, so the Playground's "could not read this styling"
  self-check message is the norm for decorated Micro QR, not the exception.

**Benchmark delta (net10.0 Release, before = ff64136 via worktree, after = this change)**

Standard QR guards, flat, allocations identical:

| Benchmark | Before | After |
|---|---|---|
| QrCodeImageEndToEnd Small_512px | 4.67 ms / 5.44 KB | 4.49 ms / 5.44 KB |
| QrCodeImageEndToEnd Large_2048px | 89.3 ms / 41.9 KB | 80.8 ms / 41.9 KB |
| QrCodeDecodeEndToEnd Matrix_Short_M | 1.06 µs / 0 B | 1.06 µs / 0 B |
| QrCodeDecodeEndToEnd Image_Url_M | 37.4 µs / 0 B | 38.4 µs / 0 B |

New Micro QR image paths (`MicroQRImageEndToEnd`):

| Benchmark | Mean | Allocated |
|---|---|---|
| M2_512px (render + PNG encode) | ~4.5 ms | 5.3 KB |
| M4_512px | ~4.6 ms | 5.5 KB |
| M4_128px | ~316 µs | 3.8 KB |
| M4_ImageDecode_Span (136×136 px) | 17.1 µs | **0 B** |

Render times are PNG-encode dominated (Standard QR Small_512px is the same
~4.5 ms); the builder itself adds no measurable overhead.

### Phase 4 follow-up, capacity error messages, completed 2026-07-18

**Done**

- `MicroQRCodeGenerator` capacity errors now state the actual length, the
  applicable maximum, and the remedy, in mode-appropriate units (digits /
  characters / encoded bytes): e.g. "Content is too long for Micro QR: 46 bytes
  in Byte mode, but ECC level M fits at most 13 bytes (M4). Shorten the content,
  lower the ECC level, or use Standard QR (QRCodeGenerator) for longer content."
  The maximum comes from a new `GetMaxDataLength` helper (closed-form inverse of
  `GetRequiredBits` against the Table 7 bit capacity; error-path only). The
  mode-unsupported-at-ECC case (e.g. Alphanumeric + ErrorDetectionOnly) gets its
  own constraint-oriented message instead of a misleading "too long".
- Playground and BlazorWasm display these messages verbatim; both rephrase the
  API-oriented remedy ("use Standard QR (QRCodeGenerator)") to the page's actual
  control ("switch Symbology to QR Code"). Verified in the published WASM build.
- Tests (+4): message content per path (auto too-long byte/numeric with computed
  maxima, fixed-version too-long, mode-unsupported constraint). Full suite
  3,918, 0 failed. Exception types unchanged (`ArgumentException`).

**Benchmarks**

- Not applicable: error-path-only change (message composition on throw); no hot
  path touched.

### Phase 4 follow-up (2), image builder base class, completed 2026-07-18

**Done**

- `QRCodeImageBuilderBase<TSelf>` (self-referential generic): the fluent options
  every symbology shares (`WithSize` / `WithModulePixelSize` / `WithFormat` /
  `WithQuietZone` / `WithColors` / `WithModuleShape` / `WithGradient`) and the
  complete output surface (`SaveTo` ×2, `SaveToSvg` ×2, `ToSvgString`,
  `ToByteArray`, `ToImage`, `ToBitmap`, plus the raster/SVG pipeline with the
  opaque-surface and crispEdges logic) now exist exactly once.
  `QRCodeImageBuilder` / `MicroQRCodeImageBuilder` keep constructors, their
  symbology-typed options, static helpers, and three `private protected` hooks
  (`ResolveSymbol`, `RenderSymbol`, `UseCrispEdgesCore`). Quiet-zone defaults
  (4 / 2) moved from parameter defaults into builder initial state;
  `WithQuietZone` no longer declares a default argument.
- `QrImageBuilderApiParityTest`: reflection over both public surfaces asserts
  1:1 correspondence with symbology types canonicalized (QRCodeData ⇔
  MicroQRCodeData, ECCLevel ⇔ MicroQREccLevel, int version ⇔ MicroQRVersion)
  modulo the documented Standard-only options (WithIcon, WithFinderPatternShape,
  WithEciMode), guards the statics the base class cannot share. Passed against
  the pre-refactor surfaces first (they were already symmetric), so it now locks
  the contract; the rMQR builder joins the same base and the same test.
- Source compatible; binary breaking (members moved to the base), recorded in
  `docs/migration.md`. Playground / BlazorWasm / ConsoleApp compile unchanged.
- Full suite 3,930 on net8.0 + net10.0, 0 failed (golden-pixel and SVG builder
  tests unchanged, behavior preserved).

**Lessons learned**

- The parity test passing before the refactor was the useful signal, not a
  wasted red: hand-mirrored surfaces were symmetric today, and the test is what
  keeps that true when the third symbology lands.
- The one hook shape that avoids double encoding is "resolve once, hand back an
  opaque handle" (`object ResolveSymbol(out int matrixSize)` + `RenderSymbol`);
  splitting into separate size/render hooks would re-encode the symbol per
  output call, and a second generic parameter would leak the data type into the
  public base signature for no user benefit.

**Benchmark delta (QrCodeImageEndToEnd + MicroQRImageEndToEnd, net10.0 Release,
before = pre-refactor tree, after = this change)**

All scenarios within single-iteration noise, allocations byte-identical
(Standard 5.44/20.44/19.44/41.91 KB; Micro 5400/5576/3856 B, decode 0 B):
Small_512px 4.49→4.63 ms, Large_2048px 80.8→82.3 ms, M2_512px 4.52→4.46 ms,
M4_128px 314→315 µs (an intermediate 366 µs reading did not reproduce),
M4_ImageDecode_Span 17.1→16.0 µs. The per-image virtual hook dispatch is
invisible under ms-scale PNG encoding.

## Risks Beyond the Test Strategy Document

- Renderer assumptions: `IconShape`/finder styling assume three finder patterns; rectangular output changes image sizing APIs. Audit in Phase 0.
- Serialization compatibility: QRR v1 streams must keep round-tripping forever; v2 header must be rejected cleanly by old readers.
- Oracle gaps: if an rMQR encode oracle disagrees with zxing-cpp decode, specification examples arbitrate (ISO/IEC 23941 Annex examples).
- Scope creep via Kanji mode: Micro QR M3/M4 and rMQR support Kanji; deferring it is fine but capacity tables and auto-version selection must be written with the mode column present.
