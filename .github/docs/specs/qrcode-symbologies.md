# QR Symbology Architecture

Design record for supporting multiple QR symbologies — Standard QR (ISO/IEC 18004),
Micro QR (ISO/IEC 18004), and rMQR (ISO/IEC 23941) — in one library. This document
defines how the codebase is organized so each symbology can be added without
destabilizing the others. Implementation details live in code comments next to the
code; the implementation order lives in
[the implementation plan](../plans/skiasharp-qrcode-microqr-rmqr-implementation-plan.md).

---

## What

### Symbology status

| Symbology | Standard | Encode | Matrix decode | Image decode |
|---|---|---|---|---|
| Standard QR (versions 1–40) | ISO/IEC 18004 | Shipped | Shipped | Shipped (Tier 1–2) |
| Micro QR (M1–M4) | ISO/IEC 18004 | Shipped | Planned | Planned |
| rMQR (R7x43–R17x139) | ISO/IEC 23941 | Planned | Planned | Planned |

### Document set

Specs are one set of files per symbology plus this cross-cutting record — see the
[documentation index](../README.md) for the file list. Structure and naming rules live
in [docs_authoring_guidelines.md](../docs_authoring_guidelines.md); new symbologies
copy the section skeleton of the Standard QR document of the same type.

### Internal organization

Internals are split into shared primitives and per-symbology pipelines.

**Shared primitives** (`Internals`, `Internals.BinaryEncoders`, `Internals.BinaryDecoders`,
`Internals.ImageDecoders`) — knowledge that is identical across all three symbologies:

| Component | Why it is shared |
|---|---|
| `GaloisField`, `Polynom`, `EccBinaryEncoder`, `EccBinaryDecoder` | All three symbologies use Reed-Solomon over GF(256) with the same primitive polynomial (0x11D) |
| `BitWriter`, `BitReader` | Bit-stream packing is symbology-independent |
| `ECCInfo` | RS block structure (data codewords, ECC per block, up to two block groups) describes all three symbologies |
| `EncodingMode`, `TextAnalyzer`, `CharacterSets` | Mode alphabet definitions (Numeric / Alphanumeric / Byte character classes, alphanumeric encoding values) are shared; only indicator widths and legality differ per symbology |
| `LuminanceConverter`, `PerspectiveTransform` | Image preprocessing and geometry are symbology-independent |
| `Point`, `Rectangle` | Plain geometry types |

**Per-symbology pipelines** (`Internals.StandardQr`, later `Internals.MicroQr`,
`Internals.RmQr`) — knowledge specific to one symbol format:

- Capacity / ECC / interleaving tables and version selection
- Mode indicator and character-count indicator widths
- Format (and version) information encoding and decoding
- Function pattern layout, data module placement, mask patterns and mask scoring
- Symbol detection and sampling in images

Dependency rule: shared code never references a symbology namespace; symbology
namespaces never reference each other. Each symbology pipeline composes the shared
primitives. Existing Standard QR code moves under `Internals.StandardQr` unchanged —
no behavioral or algorithmic modification accompanies the move.

Two components currently live in `Internals.StandardQr` but contain liftable pieces;
they are lifted only when a second consumer actually appears (Phase 6, image
detection), not speculatively:

- `QRImageDecoder.ComputeOtsuThreshold` — generic binarization
- `FinderPatternFinder`'s 1:1:3:1:1 run-ratio scan — Micro QR uses the same finder
  pattern shape (single finder instead of three)

### Public API direction

Each symbology gets its own generator entry point with symbology-typed version and
error-correction parameters. `QRCodeGenerator.CreateQrCode` and its overloads remain
unchanged — Standard QR users see no difference.

Decoding: matrix-level entry points are symbology-explicit (matrix size alone
distinguishes Micro QR 11–17 from Standard 21–177, but rectangular input needs
width/height). Image-level decoding keeps Standard-QR-only scanning as the default;
additional symbologies are opt-in so the existing detection hot path keeps its
performance characteristics.

Exact API names and shapes are finalized per-symbology at implementation time,
spec-first, following the API-driven development principle in
[DESIGN.md](../DESIGN.md).

### Data model direction

`QRCodeData` stays Standard-QR-only. New symbologies get their own data types, with
rectangular dimensions (width ≠ height) decided once when the first new type is
introduced.

Serialization: the `QRR` format is frozen as-is for Standard QR (header + 1-byte
size, sizes 21–177, square). New symbologies use a new serialization header carrying
symbol type, width, and height. Old readers reject new-format streams cleanly (the
existing header/size validation already guarantees this).

## Why

### Why separate entry points instead of extending `CreateQrCode`

The existing parameters do not generalize:

| Parameter | Standard QR | Micro QR | rMQR |
|---|---|---|---|
| Version | `int` 1–40 | M1–M4 | R7x43–R17x139 (32 rectangular sizes) |
| ECC level | L/M/Q/H | M1: detection only; M2–M3: L/M; M4: L/M/Q | M/H only |
| Auto-selection | Smallest version | Smallest version, mode legality varies per version | Fit strategy is two-dimensional (width-first vs height-first) |

Overloading one method family with union-typed parameters would make illegal
combinations representable and push validation to runtime. Separate entry points make
each symbology's constraints visible in the type system.

### Why sibling namespaces instead of a polymorphic abstraction

The Standard QR pipeline is heavily performance-tuned (zero-allocation steady state,
SIMD kernels, stackalloc buffers, aggressive inlining). A shared abstraction over the
pipeline stages (virtual dispatch, interface indirection, or generic strategy types)
would put abstraction cost on the hot path and couple all symbologies to one pipeline
shape — even though their stages genuinely differ (e.g. rMQR has no mask selection,
Micro QR has no interleaving for most versions, format information differs in size,
location, and BCH code).

Sibling namespaces bound the blast radius instead: a Micro QR change cannot touch
Standard QR code paths. The regression guard is structural (namespace dependency
rule) plus empirical (Standard QR benchmarks must stay flat through every phase).

### Why `QRCodeData` is not generalized

`QRCodeData` is a shipped public type whose contract is square, 21–177 modules,
versions 1–40, with a serialization format that encodes exactly that. Generalizing it
would either break the serialization contract or turn every member into a
symbology-conditional. Sibling data types keep the shipped contract byte-for-byte
stable and let rectangular geometry be designed without compatibility constraints.

### Why Kanji mode is deferred

The library does not implement Kanji segments for Standard QR today (detected and
reported on decode, never encoded). Extending that line to Micro QR (M3/M4) and rMQR
keeps encode/decode capability symmetric across symbologies. Capacity tables and
version auto-selection are still written with the Kanji column present, so adding the
mode later is a data+segment change, not a table redesign.

## Scope decisions

| Decision | Choice | Revisit when |
|---|---|---|
| Kanji mode (all symbologies) | Deferred; tables keep the column | User demand or decoder interop need |
| Image detection default | Standard QR only; new symbologies opt-in | Phase 6 API design |
| Shared detection primitives (Otsu, run-ratio scan) | Stay in `Internals.StandardQr` until a second consumer exists | Phase 6 |
| `QRCodeData` | Frozen for Standard QR | Never (compatibility contract) |

## Lessons learned

- ZXing.Net (the in-CI cross-validation oracle for Standard QR) cannot decode Micro QR
  or rMQR, so in-CI cross-verification is unavailable for the new symbologies.
  Committed external fixtures are the primary conformance oracle instead — see the
  [test strategy](../plans/skiasharp-qrcode-microqr-rmqr-test-strategy.md).
- `EncodingModeExtensions.GetCountIndicatorLength` looked shared but encodes Standard
  QR's version thresholds (10/27); Micro QR and rMQR define their own indicator-width
  tables. The enum is shared; the width logic is per-symbology.
- The character-class predicates and alphanumeric encoding values (`IsNumeric`,
  `IsAlphanumeric`, `GetAlphanumericValue`, `IsValidISO88591`) lived inside the
  Standard QR constants class, so `TextAnalyzer` (shared) silently depended on the
  Standard QR table class. Applying the namespace dependency rule surfaced this
  immediately; the predicates now live in shared `CharacterSets` — the alphabets are
  identical across ISO/IEC 18004 and ISO/IEC 23941.
