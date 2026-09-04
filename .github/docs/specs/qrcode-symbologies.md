# QR Symbology Architecture

Design record for supporting multiple QR symbologies, Standard QR (ISO/IEC 18004), Micro QR (ISO/IEC 18004), and rMQR (ISO/IEC 23941), in one library. This document defines how the codebase is organized so each symbology can be added without destabilizing the others. Implementation details live in code comments next to the code.

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
| `ModuleRunScanner`, `ModuleRunEnumerator<TView>` | Merging dark modules into horizontal runs depends only on the `IModuleMatrixView` shape, not the symbology. One implementation serves both the renderer's merged-run drawing path and the public `GetModuleRectangles` surface on all three data types, so the geometry the public API reports is by construction the geometry the renderer draws |

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

**Options live in a per-symbology options struct, and that is where new options go.** `QRCodeGeneratorOptions`, `MicroQRCodeGeneratorOptions` and `RmQRCodeGeneratorOptions` are `readonly record struct`s passed by `in`, so the allocation-free entry points stay allocation-free. The released parameter list overloads are frozen: they keep working, keep their exact signatures, exceptions and messages, and never gain another parameter. rMQR had no released overloads and so has only the options form. The rule exists because the parameter list shape had already failed on rMQR before it shipped, at 10 methods with up to 9 parameters, with one option escaping into method names: `EciMode` could not be added positionally without disturbing the existing order, so `CreateRmQRCodeWithEci` and two siblings were added instead, doubling the method count while Standard QR expressed the same concept as an ordinary parameter.

**The reshaping was possible only because of a release window, and that window is now closed.** No rMQR API had ever been in a NuGet package, so its surface could be fixed by deletion at zero compatibility cost; Standard QR and Micro QR had no such freedom and kept their released overloads. Package validation against the last published package is what enforced the difference (`EnablePackageValidation` with `PackageValidationBaselineVersion`), and the baseline is bumped on every release, so from 1.2.0 onward the reshaped surface is frozen the same way. Anything that would have wanted this kind of change again has to go through an obsolete cycle instead.

**Package validation only polices the half of a change that breaks somebody, so the exported surface is also held to a listing in the repository.** Validation answers "would this break a caller who compiled against the last release", and a new public member never does: it lands unreviewed, which is the wrong default for a library whose next release is a rename. `src/SkiaSharp.QrCode/PublicAPI.approved.txt` records every exported type and member and is regenerated by `tools/check_public_api.cs`, gated on pull requests and again at release. Changing the surface is therefore a diff someone accepts deliberately, and the 2.0.0 reshaping arrives as one reviewable file rather than as a claim in a pull request description.

**The listing is read from every target framework, which turned an assumption into a fact.** The library builds for four, nothing had ever compared them, and the claim that no exported member is framework-conditional lived only in a tool comment. It held when the gate was introduced, and a build where it stops holding now fails instead of shipping. Two renderers of the same surface exist on purpose, the reflection one behind the Playground's API page and the metadata one behind the gate, and their agreeing line for line is what keeps either from drifting unnoticed.

The type name is `{consuming generator}Options`, mechanically, so no reader has to work out which struct belongs to which generator. This follows the BCL habit of naming an options bag after its consumer (`JsonSerializerOptions` for `JsonSerializer`) and leaves `{X}DecoderOptions` free under the same rule.

Consequences worth stating here rather than three times:

- **Whether the `options` parameter may carry a default value depends on how many overloads share the name.** rMQR's `Create*` methods take `in RmQRCodeGeneratorOptions options = default` because their parameter list versions were deleted. Standard QR and Micro QR keep theirs, so a defaulted options parameter there would make `CreateQrCode(text, ecc)` ambiguous between the two sets, and callers write `QRCodeGeneratorOptions.Default with { ... }` explicitly. Sizing is the exception in the other direction: since `TryGetRequiredBufferSize` is the only sizing overload on every generator, its options parameter *is* defaulted, so `TryGetRequiredBufferSize(text, ecc, out var size)` is the shortest correct call on all three. The asymmetry is a consequence of the released surface, not a preference, and discovering it as a compiler error later is how someone talks themselves into defaulting the legacy parameters instead.

- **`default(T)` must be the complete default configuration**, since it is what an omitted argument sends. Any member whose documented default is not the zero value stores an offset from that default, not a `value + 1` sentinel, so that writing the default explicitly produces the same value as not writing it and the generated equality does not report two identical option sets as different. `QuietZoneSize` is the only such member today, and its correct default differs per symbology (4, 2, 2), which is also why one shared options type is not expressible.
- **Options are not shared across symbologies.** The intersection of the three option sets is one `int` with three different correct defaults: `Version` is a different type in each, Micro QR has no ECI, and rMQR's fit options mean nothing elsewhere. A shared type would carry members invalid for two thirds of its uses and reject them at run time instead of at compile time.
- **The error correction level stays a required parameter and is deliberately not an option.** It is the only input with no correct default: L and H differ by roughly 4x in capacity, and choosing between capacity and robustness is the caller's decision, not the library's. Putting it in the options struct would force `default(T)` to encode that choice silently, which contradicts the rule above. The line this draws is that the required arguments say *what symbol you want* (the text and how robust it must be) while the options say *how to encode and render it*. The image builders do default it to `M`, which is right for a "just give me a picture" entry point and wrong for the generator.
- **A version *range* exists only where versions are totally ordered.** `QRCodeVersionRange` (1-40) and `MicroQRVersionRange` (M1-M4) subsume the single requested version, with a pinned version as the degenerate `Exactly` case; `null` and `int?` convert implicitly so an optional version needs no branch at the call site. rMQR has no range because its 32 versions have no min/max relation, and constrains fit with `RmQRFitStrategy` and `RmQRHeight` instead.

When the parameter list overloads are eventually obsoleted, re-evaluate whether the `string` convenience overloads are still worth their weight: `string` converts implicitly to `ReadOnlySpan<char>` on every target framework, so they add a parameter name (`plainText` rather than `textSpan`) and nothing else. They are kept today because their released counterparts pin the shape.

Decoding: matrix-level entry points are symbology-explicit (matrix size alone distinguishes Micro QR 11–17 from Standard 21–177, but rectangular input needs width/height). Image-level decoding keeps Standard-QR-only scanning as the default; additional symbologies are opt-in so the existing detection hot path keeps its performance characteristics.

Image builders: one builder per symbology, all deriving from `QRCodeImageBuilderBase<TSelf>` (self-referential generic, so fluent chains keep the concrete type). The base carries every shared option and the complete raster/SVG output surface; a symbology builder adds only its typed options (ECC/version) and connects its data model through three `private protected` hooks. Two guards keep the surfaces from drifting: the base class makes output-method omissions structurally impossible, and `QrImageBuilderApiParityTest` (reflection over the public surfaces with a documented allowed-difference list) catches asymmetry in what cannot be shared, the symbology-typed static helpers. The rMQR builder extends the same base and the same parity test.

Capacity pricing overflow: Byte mode costs `8 × length` bits, so a data length past `int.MaxValue / 8` wraps negative and reads as a fit. All three selectors price in `long` — including the UTF-8 BOM's `+ 3`, which wraps on its own near `int.MaxValue`. Widening rather than rejecting the length up front is the deliberate choice: a length fast-path placed ahead of the remaining checks answers "does not fit" for what is actually an argument error, and one placed ahead of the Micro QR version check also handed an unvalidated version to the table-indexing error builder. Reachable only with a quarter-gigabyte input, so `CapacityOverflowGuardTest` drives the selectors directly instead of allocating one.

Sizing: every generator exposes exactly one sizing method, `TryGetRequiredBufferSize`, with one contract across all three symbologies — `false` means the content does not fit, and argument errors throw. Standard QR and Micro QR additionally carry an `[Obsolete]` throwing `GetRequiredBufferSize`, kept only because v1.1.1 released it and scheduled for removal in 2.0.0; it is not part of the current surface and no new option is ever added to it. The reasoning, including why this is a `Try` overload rather than a dedicated exception type and why undefined enum values are not folded into `false`, is recorded once in [rmqr-encoder.md](rmqr-encoder.md); the other two symbologies follow it rather than restating it.

**The two deprecated overloads disagree with each other about exception types, and the disagreement is frozen.** Measured, not read off the docs, which had drifted: Standard QR reports a content overflow as `InvalidOperationException` while Micro QR reports the same condition as `ArgumentException`, and Standard QR reports an undefined `ECCLevel` as `ArgumentException` where Micro QR and rMQR use `ArgumentOutOfRangeException`. Both shipped in v1.1.1, so changing either is exactly the break the deprecation window exists to avoid; they are documented as they are and pinned by `SizingExceptionContractTest`, which is also what the XML docs on these methods are now written from. An exception tag is the one part of a public API with no compiler behind it, which is why it drifted and why a test rather than proofreading is the fix.

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
minus sign, and the cent / pound / not signs), and, within the Kanji-mode range, CP932
additionally assigns 83 characters the standard does not, all of them NEC row 13 (0x8740-0x879C:
circled digits, roman numerals, unit ligatures). Choosing CP932 would have
mangled exactly the characters Japanese payloads use in URLs and price strings. The shared
`ShiftJisKanjiTable` holds the 6,879-cell JIS X 0208 repertoire and nothing else; cells outside it
are reported as `QRCodeDecodeStatus.UnmappedCharacter` rather than replaced, so a corrupt symbol never
becomes a plausible wrong answer and a caller can tell "a CP932 reader would read this" from the
structural `UnsupportedContent` cases (FNC1, Structured Append, unmapped ECI). The
table costs 16 KB of RVA data, shared by all three symbologies, with no allocation and no static
constructor.

**The table is derived from a measurement and gated by arithmetic, not transcribed.** Its values
come from a sweep of every structurally valid Kanji-mode cell (8,023 of them, encoded by qrtool as
raw Shift_JIS and read back by zxing-cpp), and the generator refuses to emit unless that swept data
reproduces JIS X 0208's published repertoire size and its delta against CP932 is exactly the
documented divergence set. Reproducing **6,879** assigned cells independently (524 non-kanji +
6,355 kanji) is what makes "this really is JIS X 0208" a measurement rather than an assumption.
Because every other table test constrains only *which* cells are assigned and would pass a table
whose readings were permuted, a golden digest over all 8,192 entries pins the values themselves;
regenerating the table is expected to change it, in the same reviewed commit.

Two failure causes are kept apart on the error path: a structurally impossible byte pair is
`InvalidBitstream`, a well-formed but unassigned cell is `UnmappedCharacter`. The distinguishing
arithmetic sits on the error path only, so the happy path stays a single indexed load.

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
| ECI 20 (Shift_JIS) byte segments | Unsupported; reported as `UnsupportedContent` (structural, unlike the per-character `UnmappedCharacter`) | Demand for symbols that pair ECI 20 with Byte mode; needs the full CP932 range, roughly twice the Kanji table |
| Image detection default | Standard QR only (`QRCodeDecoder`); Micro QR and rMQR scanning are their own explicitly-typed entries (`MicroQRCodeDecoder`, `RmQRCodeDecoder`); the Playground tries the three in that order | - |
| Shared detection primitives (Otsu, run-ratio scan) | Lifted to `Internals.ImageDecoders` (Phase 4b, second consumer appeared) | - |
| `QRCodeData` | Frozen for Standard QR | Never (compatibility contract) |
| Interoperability runs against external encoders/decoders | Manual, not a CI job. `tools/QRInteropFixtures` spot-checks (`spot-check-rmqr`, `spot-check-microqr`) are run by hand before a release; pull-request CI stays self-contained and consumes the committed fixture corpus instead | A regression the committed corpus cannot catch, or an external oracle that installs cleanly enough for scheduled CI under the pinning policy in [qrcode-test-fixtures.md](qrcode-test-fixtures.md) |
| Physical scanner acceptance | Not automated, and deliberately never a conformance gate: a phone scanner disagreeing proves an interoperability problem, never a specification violation. Run ad hoc against a representative print/screen set before a symbology's first release | A field report that the committed corpus and the image-degradation tests both pass but real scanners fail |

## Lessons learned

- ZXing.Net (the in-CI cross-validation oracle for Standard QR) cannot decode Micro QR or rMQR, so in-CI cross-verification is unavailable for the new symbologies. Committed external fixtures are the primary conformance oracle instead, see [qrcode-test-fixtures.md](qrcode-test-fixtures.md).
- **The Kanji divergence set is seven cells, not the six usually quoted.** 0x815F (reverse solidus U+005C against fullwidth U+FF3C) is not on the commonly circulated list and does diverge, while 0x815C, which is on it, agrees (both U+2015). Building the override list from memory would have shipped two wrong cells; the sweep found both at once.
- **CP932 does not report unassigned cells as unmapped.** .NET returns U+30FB for them, so a naive "did it decode?" comparison counts 1,061 phantom assignments on top of the 83 real NEC row 13 characters. Any comparison against CP932 has to know that, and it is why the generator gates on published totals rather than on a decode-succeeded filter.
- NEC row 13 (0x8740-0x879C: circled digits, roman numerals, unit ligatures) resolved cleanly against the same arithmetic: CP932 assigns 83 of its 92 structurally valid cells, zxing-cpp assigns none, and excluding them is exactly what makes the total come out at 6,879. A symbol carrying `①` in Kanji mode is therefore reported as unmapped rather than decoded under a mapping the standard does not define.
- `EncodingModeExtensions.GetCountIndicatorLength` looked shared but encodes Standard QR's version thresholds (10/27); Micro QR and rMQR define their own indicator-width tables. The enum is shared; the width logic is per-symbology.
- The character-class predicates and alphanumeric encoding values (`IsNumeric`, `IsAlphanumeric`, `GetAlphanumericValue`, `IsValidISO88591`) lived inside the Standard QR constants class, so `TextAnalyzer` (shared) silently depended on the Standard QR table class. Applying the namespace dependency rule surfaced this immediately; the predicates now live in shared `CharacterSets`, the alphabets are identical across ISO/IEC 18004 and ISO/IEC 23941.
- **A UTF-16 code unit compared as `Int16` is negative above U+7FFF.** `TextAnalyzer`'s AVX2 and SSE2 tiers tested the ASCII (> 127) and ISO-8859-1 (> 255) thresholds with signed packed compares, so every char from U+8000 up — surrogate halves, meaning any emoji or other non-BMP char, plus U+FFFD and the CJK compatibility area — read as in range. Auto-detection then declared Latin-1 for text ISO-8859-1 cannot represent, and the Byte-mode Latin-1 writer truncated it (its own SSE2 tier saturates instead, so the corruption differed per architecture). The ARM64 tier was correct by construction: its reduction is `UMAXV`, unsigned. Two lessons. Threshold tests on char data must be unsigned, and at these two thresholds a bit test is both correct and cheaper than any compare (`c > 127` is "any bit from bit 7 up", `c > 255` is "any high-byte bit", one `vptest` on AVX2). And a tier the dispatcher cannot reach on the test machine needs a direct-entry parity test: AVX2 hides SSE2 on every CPU that has it, so the dispatch-level test cannot see an SSE2-only defect — `TextAnalyzerX64ParityTest` calls both entry points itself, the way the ARM64 test always has.

