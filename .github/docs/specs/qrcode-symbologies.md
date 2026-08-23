# QR Symbology Architecture

Design record for supporting multiple QR symbologies, Standard QR (ISO/IEC 18004), Micro QR (ISO/IEC 18004), and rMQR (ISO/IEC 23941), in one library. This document defines how the codebase is organized so each symbology can be added without destabilizing the others. Implementation details live in code comments next to the code; the implementation order lives in [the implementation plan](../plans/skiasharp-qrcode-microqr-rmqr-implementation-plan.md).

---

## What

### Symbology status

| Symbology | Standard | Encode | Matrix decode | Image decode |
|---|---|---|---|---|
| Standard QR (versions 1–40) | ISO/IEC 18004 | Shipped | Shipped | Shipped (Tier 1–2) |
| Micro QR (M1–M4) | ISO/IEC 18004 | Shipped | Shipped | Shipped (Tier 1–2, conservative measured envelope) |
| rMQR (R7x43–R17x139) | ISO/IEC 23941 | Encode + render done (Phase 5, encoder MVT met; not yet released) | Done (Phase 6, decoder MVT matrix rows met; not yet released) | Done (Phase 7, Tier 1–2, measured envelope incl. keystone up to 4 % on R17x139; not yet released) |

### Document set

Specs are one set of files per symbology plus this cross-cutting record, see the [documentation index](../README.md) for the file list. Structure and naming rules live in [docs_authoring_guidelines.md](../docs_authoring_guidelines.md); new symbologies copy the section skeleton of the Standard QR document of the same type.

### Internal organization

Internals are split into shared primitives and per-symbology pipelines.

**Shared primitives** (`Internals`, `Internals.BinaryEncoders`, `Internals.BinaryDecoders`, `Internals.ImageDecoders`), knowledge that is identical across all three symbologies:

| Component | Why it is shared |
|---|---|
| `GaloisField`, `Polynom`, `EccBinaryEncoder`, `EccBinaryDecoder` | All three symbologies use Reed-Solomon over GF(256) with the same primitive polynomial (0x11D) |
| `BitWriter`, `BitReader` | Bit-stream packing is symbology-independent |
| `ModuleBitPacker` | Byte-per-module ↔ MSB-first bit-packed conversion of the Micro QR and rMQR data models (`SetCoreData` / `GetCoreData`) is the same operation for both; Standard QR's `QRCodeData` keeps its own (frozen) storage kernel |
| `ECCInfo` | RS block structure (data codewords, ECC per block, up to two block groups) describes all three symbologies |
| `BinaryInterleaver` | Block interleaving of data then ECC codewords depends only on the `ECCInfo` block structure; Standard QR and rMQR interleave identically (Micro QR has one block). Lifted from `Internals.StandardQr` to `Internals.BinaryEncoders` when rMQR became the second consumer (rMQR Phase 5.4); the only symbology-specific input, the remainder-bit count, is passed in by the caller |
| `EncodingMode`, `TextAnalyzer`, `CharacterSets` | Mode alphabet definitions (Numeric / Alphanumeric / Byte character classes, alphanumeric encoding values) are shared; only indicator widths and legality differ per symbology |
| `SegmentDecoders` | Segment payload bit groups (numeric 10/7/4, alphanumeric 11/6, byte 8·count), the byte-charset heuristics (UTF-8 validation, BOM, Latin-1 widening) and the ECI designator reader (lifted from `QRBinaryDecoder` when rMQR became its second consumer, Phase 6) are identical across symbologies; the mode/count indicator framing that differs stays in each symbology's bitstream decoder (lifted out of `QRBinaryDecoder` in Phase 3 when the second consumer appeared) |
| `LuminanceConverter`, `PerspectiveTransform` | Image preprocessing and geometry are symbology-independent |
| `Point`, `Rectangle` | Plain geometry types |

**Per-symbology pipelines** (`Internals.StandardQr`, later `Internals.MicroQR`, `Internals.RmQr`), knowledge specific to one symbol format:

- Capacity / ECC / interleaving tables and version selection
- Mode indicator and character-count indicator widths
- Format (and version) information encoding and decoding
- Function pattern layout, data module placement, mask patterns and mask scoring
- Symbol detection and sampling in images

Dependency rule: shared code never references a symbology namespace; symbology namespaces never reference each other. Each symbology pipeline composes the shared primitives. Existing Standard QR code moves under `Internals.StandardQr` unchanged, no behavioral or algorithmic modification accompanies the move.

Two detection primitives were lifted from `Internals.StandardQr` to the shared `Internals.ImageDecoders` namespace when Micro QR image detection (Phase 4b) became their second consumer, exactly the trigger this document prescribed:

- `Binarizer.ComputeOtsuThreshold`, generic binarization (moved out of `QRImageDecoder`)
- `FinderPatternFinder`, the 1:1:3:1:1 run-ratio scan and cross-checks; Micro QR and rMQR use the same finder pattern shape (single finder instead of three) via `FindCandidates` (all cross-checked candidates), while Standard QR keeps its best-three selection in `TryFind`
- `FinderAxisEstimator`, single-finder local module scale and axis recovery (axis-aligned dark-light-dark runs, angular sweep), lifted from the Micro QR image decoder when rMQR image detection (Phase 7) became its second consumer; the Micro QR image benchmark stayed flat

### Public API direction

Each symbology gets its own generator entry point with symbology-typed version and error-correction parameters. `QRCodeGenerator.CreateQrCode` and its overloads remain unchanged, Standard QR users see no difference.

Decoding: matrix-level entry points are symbology-explicit (matrix size alone distinguishes Micro QR 11–17 from Standard 21–177, but rectangular input needs width/height). Image-level decoding keeps Standard-QR-only scanning as the default; additional symbologies are opt-in so the existing detection hot path keeps its performance characteristics.

Image builders: one builder per symbology, all deriving from `QRCodeImageBuilderBase<TSelf>` (self-referential generic, so fluent chains keep the concrete type). The base carries every shared option and the complete raster/SVG output surface; a symbology builder adds only its typed options (ECC/version) and connects its data model through three `private protected` hooks. Two guards keep the surfaces from drifting: the base class makes output-method omissions structurally impossible, and `QrImageBuilderApiParityTest` (reflection over the public surfaces with a documented allowed-difference list) catches asymmetry in what cannot be shared, the symbology-typed static helpers. The rMQR builder extends the same base and the same parity test.

Exact API names and shapes are finalized per-symbology at implementation time, spec-first, following the API-driven development principle in [DESIGN.md](../DESIGN.md).

### Data model direction

`QRCodeData` stays Standard-QR-only. New symbologies get their own data types, with rectangular dimensions (width ≠ height) decided once when the first new type is introduced.

Serialization: the `QRR` format is frozen as-is for Standard QR (header + 1-byte size, sizes 21–177, square). New symbologies use a new serialization header carrying symbol type, width, and height. Old readers reject new-format streams cleanly (the existing header/size validation already guarantees this).

## Why

### Why separate entry points instead of extending `CreateQrCode`

The existing parameters do not generalize:

| Parameter | Standard QR | Micro QR | rMQR |
|---|---|---|---|
| Version | `int` 1–40 | M1–M4 | R7x43–R17x139 (32 rectangular sizes) |
| ECC level | L/M/Q/H | M1: detection only; M2–M3: L/M; M4: L/M/Q | M/H only |
| Auto-selection | Smallest version | Smallest version, mode legality varies per version | Fit strategy is two-dimensional (width-first vs height-first) |

Overloading one method family with union-typed parameters would make illegal combinations representable and push validation to runtime. Separate entry points make each symbology's constraints visible in the type system.

### Why sibling namespaces instead of a polymorphic abstraction

The Standard QR pipeline is heavily performance-tuned (zero-allocation steady state, SIMD kernels, stackalloc buffers, aggressive inlining). A shared abstraction over the pipeline stages (virtual dispatch, interface indirection, or generic strategy types) would put abstraction cost on the hot path and couple all symbologies to one pipeline shape, even though their stages genuinely differ (e.g. rMQR has no mask selection, Micro QR has no interleaving for most versions, format information differs in size, location, and BCH code).

Sibling namespaces bound the blast radius instead: a Micro QR change cannot touch Standard QR code paths. The regression guard is structural (namespace dependency rule) plus empirical (Standard QR benchmarks must stay flat through every phase).

### Why `QRCodeData` is not generalized

`QRCodeData` is a shipped public type whose contract is square, 21–177 modules, versions 1–40, with a serialization format that encodes exactly that. Generalizing it would either break the serialization contract or turn every member into a symbology-conditional. Sibling data types keep the shipped contract byte-for-byte stable and let rectangular geometry be designed without compatibility constraints.

### Why Kanji mode is read but never written

Kanji is asymmetric on purpose: all three decoders read it, no generator emits it.

Reading it is an interoperability obligation. Japanese-market encoders do emit Kanji mode, and a
decoder that rejects those symbols cannot read them at all, which is a hole no caller can work
around. Writing it is a different decision: UTF-8 Byte mode already carries Japanese text, and
emitting Kanji would change the default output of shipped generators. Decoding changes no output,
so it ships on its own.

The consequence is a deliberate round-trip asymmetry: `Decode(Encode(x)) == x` holds, but
`Encode(Decode(y))` does not reproduce a Kanji symbol `y`. The capacity tables therefore keep the
Kanji column for the decoder's count-indicator widths, not as a commitment to encode.

**The mapping is JIS X 0208, not CP932.** The two disagree on seven Shift_JIS cells (0x815F,
0x8160, 0x8161, 0x817C, 0x8191, 0x8192, 0x81CA: reverse solidus, wave dash, double vertical line,
minus sign, and the cent / pound / not signs), and CP932 additionally assigns 1,144 cells outside
the standard's repertoire, most visibly the NEC row 13 circled digits. Choosing CP932 would have
mangled exactly the characters Japanese payloads use in URLs and price strings. The shared
`ShiftJisKanjiTable` holds the 6,879-cell JIS X 0208 repertoire and nothing else; cells outside it
are reported rather than replaced, so a corrupt symbol never becomes a plausible wrong answer. The
table costs 16 KB of RVA data, shared by all three symbologies, with no allocation and no static
constructor. Full derivation and oracle evidence: [Kanji mode decode plan](../plans/kanji-mode-decode-plan.md).

Still unsupported and still reported as `UnsupportedContent`: ECI 20 (Shift_JIS) byte-mode
segments, which need the wider CP932 single-byte plus double-byte range, and (Standard QR) FNC1
and Structured Append.

### Allocation contract

The span-destination overloads are documented as allocating nothing **per call**, not as
never touching the heap. Every symbology lazily builds immutable lookup tables on first use
and caches them for the process: per-version placement and extraction layouts, and — on
ARM64, where the syndrome kernel reads its data terms out of a table rather than computing
them — a 24 KB alpha-step table shared by all three symbologies. They are built once, keyed
by version, published with a release store, and bounded (about 100 KB if every rMQR version
were exercised). A benchmark that measures allocation must warm up first, or it attributes
that one-time build to the call that happened to trigger it.

### SIMD tier inventory

Shared primitives that already have both an x64 and an ARM64/Vector128 tier, and must be
treated as controls rather than reimplemented when a new symbology or kernel arrives:
`TextAnalyzer`, `EccBinaryEncoder`, `EccBinaryDecoder` (syndrome pass), `ModuleBitPacker`,
`LuminanceConverter`, `LuminanceInverter`, and finder/alignment row-mask construction.
Architecture-neutral work already benefits every target: cached per-version layouts, pair
stores and index scatter, table-driven auto-fit, the portable extraction walk, the safe
finder stride with full-sweep retry, sub-finder guards, and Otsu reuse.

The ARM64 optimization queue is closed. Four components were measured and deliberately left
below it, with reasons: Otsu histogramming (serial histogram updates, already near its
measured per-pixel floor), sub-finder and perspective search (branchy, data-dependent and
failure-path dominated), rendering and PNG encode (Skia/native-code dominated), and the
1.3-8.3 ns version selector. Reopen ARM work only with a new profile naming a different
mechanism.

## Scope decisions

| Decision | Choice | Revisit when |
|---|---|---|
| Kanji mode (all symbologies) | Decode only, JIS X 0208 mapping; encoders keep emitting UTF-8 Byte mode (with ECI where the symbology supports it) | Encoding: a policy change backed by concrete demand, since it alters shipped generator output |
| ECI 20 (Shift_JIS) byte segments | Unsupported; reported as `UnsupportedContent` | Demand for symbols that pair ECI 20 with Byte mode; needs the full CP932 range, roughly twice the Kanji table |
| Image detection default | Standard QR only (`QRCodeDecoder`); Micro QR and rMQR scanning are their own explicitly-typed entries (`MicroQRCodeDecoder`, `RmQRCodeDecoder`); the Playground tries the three in that order | - |
| Shared detection primitives (Otsu, run-ratio scan) | Lifted to `Internals.ImageDecoders` (Phase 4b, second consumer appeared) | - |
| `QRCodeData` | Frozen for Standard QR | Never (compatibility contract) |

## Lessons learned

- ZXing.Net (the in-CI cross-validation oracle for Standard QR) cannot decode Micro QR or rMQR, so in-CI cross-verification is unavailable for the new symbologies. Committed external fixtures are the primary conformance oracle instead, see the [test strategy](../plans/skiasharp-qrcode-microqr-rmqr-test-strategy.md).
- `EncodingModeExtensions.GetCountIndicatorLength` looked shared but encodes Standard QR's version thresholds (10/27); Micro QR and rMQR define their own indicator-width tables. The enum is shared; the width logic is per-symbology.
- The character-class predicates and alphanumeric encoding values (`IsNumeric`, `IsAlphanumeric`, `GetAlphanumericValue`, `IsValidISO88591`) lived inside the Standard QR constants class, so `TextAnalyzer` (shared) silently depended on the Standard QR table class. Applying the namespace dependency rule surfaced this immediately; the predicates now live in shared `CharacterSets`, the alphabets are identical across ISO/IEC 18004 and ISO/IEC 23941.
- **A UTF-16 code unit compared as `Int16` is negative above U+7FFF.** `TextAnalyzer`'s AVX2 and SSE2 tiers tested the ASCII (> 127) and ISO-8859-1 (> 255) thresholds with signed packed compares, so every char from U+8000 up — surrogate halves, meaning any emoji or other non-BMP char, plus U+FFFD and the CJK compatibility area — read as in range. Auto-detection then declared Latin-1 for text ISO-8859-1 cannot represent, and the Byte-mode Latin-1 writer truncated it (its own SSE2 tier saturates instead, so the corruption differed per architecture). The ARM64 tier was correct by construction: its reduction is `UMAXV`, unsigned. Two lessons. Threshold tests on char data must be unsigned, and at these two thresholds a bit test is both correct and cheaper than any compare (`c > 127` is "any bit from bit 7 up", `c > 255` is "any high-byte bit", one `vptest` on AVX2). And a tier the dispatcher cannot reach on the test machine needs a direct-entry parity test: AVX2 hides SSE2 on every CPU that has it, so the dispatch-level test cannot see an SSE2-only defect — `TextAnalyzerX64ParityTest` calls both entry points itself, the way the ARM64 test always has.

