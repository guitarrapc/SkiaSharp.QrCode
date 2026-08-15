# rMQR Decoder

Design record for the rMQR Code (ISO/IEC 23941) decode feature (`RmQRCodeDecoder`): what it does, why it is scoped the way it is, and what was learned during implementation. Implementation locations are indexed in the [spec-to-code map](rmqr-spec-map.md); the encoder side is in [rMQR Encoder](rmqr-encoder.md); the implementation order is the [rMQR implementation plan](../plans/skiasharp-qrcode-rmqr-implementation-plan.md).

Status: **matrix level shipped (Phase 6, 2026-08-15); image level shipped (Phase 7, 2026-08-16); adversarial review 2026-08-16 (dimension-aware format arbitration, too-small destination terminal per finder, perspective quadratic fix; see the plan's Progress log).**

---

## What

`RmQRCodeDecoder` decodes rMQR symbols back into text.

### Matrix level

Input: `RmQRCodeData`, or a byte-per-module span with `width` and `height` (the format `RmQRCodeGenerator.CreateRmQRCode(Span<byte>)` produces), with or without a light quiet zone (uniform or asymmetric borders are stripped automatically).

Behavior: version from the physical dimensions → both format-information copies → fixed unmask + inverse zigzag → block deinterleave → per-block Reed-Solomon correction → bit-stream parsing (Numeric / Alphanumeric / Byte, ECI segments parsed) → text. Diagnostics in `RmQRCodeDecodeInfo` (status, version, ECC level, corrected codewords). Overloads mirror `MicroQRCodeDecoder`: string result, and an allocation-free span-destination variant sized by `GetMaxDecodedLength(version)`.

### Image level

Input: an `SKBitmap`, or a grayscale luminance span with width and height (`TryDecodeImage`), with an allocation-free span-destination variant (size it with `GetMaxDecodedLength(RmQRVersion.R17x139)` when the version is unknown).

Behavior: shared Otsu threshold and finder candidates → local grid frames around the finder (four right angles × transpose for mirroring, then the angular finder-axis sweep for arbitrary rotation) → the finder-side format copy is sampled in the frame, which yields the version and therefore the symbol width and height before any full grid is sampled → the sub-finder is located near the corner the version predicts and anchors the far end of the symbol (global scale and rotation in closed form) → affine attempts, then a bounded projective search (two projective coefficients × row-axis shear, scale and rotation re-solved from the sub-finder each time, gated by the sub-finder-side format copy and the edge timing patterns) → this matrix decoder → one inverted retry.

### Supported

| Area | Coverage |
|---|---|
| Versions | All 32 (R7x43 … R17x139), identified by width × height and cross-checked against the format information |
| ECC levels | M, H |
| Data modes | Numeric, Alphanumeric, Byte (UTF-8 / ISO-8859-1 heuristics as the other symbologies), ECI headers 1 / 3 / 26 / 27 |
| Quiet zone | Matrix: any light border, uniform or not (dark bounding box = core). Image: 1, 2 and 4 modules verified; the finder scan needs some light margin around the finder |
| Error correction | Reed-Solomon per block at full strength ⌊ecc/2⌋ codewords per block, corrections reported |
| Format information | Matrix: either copy alone suffices (≤ 3 bit errors per copy corrected; only copies naming the version the dimensions give count, the closer of those wins, so a copy miscorrected toward another version's word cannot veto the valid one). Image: the finder-side copy must be readable (≤ 3 bit errors); it is what names the version before any grid is sampled, the sub-finder-side copy is a consistency gate on the perspective path |
| Output | `string` (allocates the result only) or `Span<char>` (allocation-free steady state, image path included) |
| Image envelope | Clean renders of every version × ECC at 3-13 px/module and non-integer scales; non-square modules; translation; right-angle and arbitrary rotation (every integer degree verified for R7x43 / R13x77); mirroring; reflectance reversal; JPEG q60, low contrast, additive noise; extreme aspect ratios; keystone 2 % and 4 % along either axis up to R17x139, also combined with 30° rotation or mirroring; the 144-symbol external PNG corpus |

### Not supported

- Kanji mode segments (`UnsupportedContent`), FNC1, Structured Append (rMQR does not define it), ECI assignments other than the four above (`UnsupportedContent`)
- Strong perspective, uneven lighting, blur, and heavily styled symbols below about 5 px/module (rounded modules + gradients make the finder runs too fuzzy; the same styling decodes at 8+ px/module)

## Why

- Explicitly typed decoder (no auto-detection inside `QRCodeDecoder`): the same reasoning as Micro QR, Standard QR scanning performance and behavior stay untouched; a caller who has an rMQR matrix knows it.
- Version from the dimensions, not from the format information alone: a matrix whose format copies decode to another version is corruption (or a wrong crop), not a smaller symbol; rejecting the contradiction is the safer default (`FormatInformationInvalid`).
- Dark-bounding-box quiet-zone stripping: rMQR has dark modules at all four core corners (finder, two corner patterns, sub-finder) and timing patterns on every edge, so the bounding box of dark modules is exactly the core, no uniform-border assumption is needed (Micro QR needed the finder-corner trick because its right/bottom edges are data).
- Format first, then geometry: an rMQR symbol has 32 possible sizes, so trying every size per frame (the Micro QR approach over 4 sizes) would be 8× more sampling; the finder-side format copy sits within 12 modules of the finder, where even a coarse local frame reads it reliably, and it carries the version. Every full grid sample is therefore made with the dimensions already known.
- Sub-finder as the far anchor: the finder-local scale estimate is pixel-accurate over 7 modules (±2 %), which is 2-3 modules of error at the far end of a 139-module symbol, and the angular sweep is quantized to 1°; neither is usable over that baseline. The sub-finder is a precise far point (5×5 template on a half-module lattice, then the center dark module's midpoint), so scale along the finder→sub-finder line and the frame rotation come out exactly, in closed form, for any candidate projective coefficients.
- Row-axis shear as a searched parameter: a keystone leans the finder's column axis by atan(shrink / height) (9° at 2 % on 17 rows, 14° at 4 %); the sub-finder pins the column direction (the finder→sub-finder line is almost a symbol row) but the row axis is free, so it is searched (±20°, 1° steps) alongside the two projective coefficients rather than measured (a 1-module feature at 8 px cannot be measured to the needed precision).
- Cheap gates before every full sample on the perspective path: the sub-finder-side format copy (18 samples, must decode to the same version within distance 1) and the edge timing patterns between the anchors (rows 0 and h−1, alternating; wrong coefficients bend the middle even when both anchors are right). Without the timing gate the search exhausted its full-decode budget on grids that were right at both anchors and wrong in between.
- Full RS strength per block, mirroring the Standard QR decoder: whether ISO/IEC 23941 reserves misdecode-protection codewords the way ISO/IEC 18004 Table 9 does for Micro QR could not be confirmed (specification text not available here); zxing-cpp also corrects at full strength on rMQR (it silently fixed qrtool's tail defect). Recorded as open, see Decisions.

## Decisions

| Decision | Choice | Revisit when |
|---|---|---|
| Correction cap | Full RS strength (⌊ecc/2⌋ per block), as Standard QR | The ISO/IEC 23941 text (Table 8) is available: if it lists misdecode-protection codewords, add the post-correction cap and its false-positive test class as `MicroQRMatrixDecoder` has |
| Format copy arbitration | Matrix: only copies whose version equals the dimensions are candidates, the closer wins, ties → finder side (review 2026-08-16: arbitrating before the dimension check let a copy miscorrected toward another version's word veto the valid copy). Image: finder-side copy names the version; sub-finder-side copy gates the perspective path only | Image-level recovery from a damaged finder-side copy (would need a sub-finder-side read per candidate version through a frame that is only accurate near the finder; only narrow symbols would benefit) |
| ECI on decode | Parsed (shared reader lifted to `SegmentDecoders`), mapped to ISO-8859-1 / UTF-8; others → `UnsupportedContent` | Demand for other charsets (cross-symbology decision) |
| Corpus expectations | libzint symbols decode with 0 corrections, qrtool symbols with ≤ 1 (documented tail defect); the same through the image path | qrtool fixes the defect (regenerate, tighten to 0) |
| Image search budgets | ≤ 8 finder candidates, ≤ 256 full-grid decodes per candidate, sub-finder search radius 6 + width/20 modules (half-module lattice, three column-axis leans), perspective grid ±12 % (1 % steps along the width, 2 % along the height) × ±20° shear | A measured input class needs more (raise the constant, add its test) or profiling shows the budget dominates a common failure |
| Sub-finder search order | Outward by rings, stop at the first perfect 25/25 match | Noisy inputs where the true position scores < 25 while a nearer position scores 25 (none observed) |

## Lessons learned

- With the encoder proven module-exact against two external lineages, the decoder round trip is a weak test on its own; the strong ones were the corpus (144 external symbols, both lineages, payload + version + ECC) and the damage classes (t per block corrected in every block of every version × ECC, t + 1 in one block never decoding cleanly, format copies singly / jointly damaged, format-vs-dimension contradiction, remainder bits ignored).
- The two format copies make the format decoder trivially robust: one copy can be destroyed outright. The arbitration rule that holds up is "among the copies naming the version the dimensions give, the closer wins": the review found that arbitrating on distance alone first let a copy miscorrected toward another version's word (5+ errors landing within 3 of it) veto the valid copy, so the matrix decoder now feeds the dimension-derived version into the arbitration.
- Image detection on a wide symbol is a long-baseline problem: everything measured at the finder is only good to a few percent, and a few percent of 139 modules is several modules. The design that worked was to make the far anchor (sub-finder) authoritative for scale and rotation and to keep every locally measured quantity as an approximation that the anchor overrides.
- Test payloads must respect per-version capacity (R7x43-M holds 7 alphanumerics): 13 early "decoder failures" were the generator rejecting the payload; the decoder had passed everything.
- The corpus's qrtool tail defect turned into a free robustness fixture: those symbols decode with exactly one corrected codeword, so the fixture test asserts `ErrorsCorrected ≤ 1` for that lineage and `0` for libzint.
