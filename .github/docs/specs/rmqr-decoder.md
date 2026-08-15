# rMQR Decoder

Design record for the rMQR Code (ISO/IEC 23941) decode feature (`RmQRCodeDecoder`): what it does, why it is scoped the way it is, and what was learned during implementation. Implementation locations are indexed in the [spec-to-code map](rmqr-spec-map.md); the encoder side is in [rMQR Encoder](rmqr-encoder.md); the implementation order is the [rMQR implementation plan](../plans/skiasharp-qrcode-rmqr-implementation-plan.md).

Status: **matrix level shipped (Phase 6, 2026-08-15); image level planned (Phase 7).**

---

## What

`RmQRCodeDecoder` decodes rMQR symbols back into text.

### Matrix level

Input: `RmQRCodeData`, or a byte-per-module span with `width` and `height` (the format `RmQRCodeGenerator.CreateRmQRCode(Span<byte>)` produces), with or without a light quiet zone (uniform or asymmetric borders are stripped automatically).

Behavior: version from the physical dimensions → both format-information copies → fixed unmask + inverse zigzag → block deinterleave → per-block Reed-Solomon correction → bit-stream parsing (Numeric / Alphanumeric / Byte, ECI segments parsed) → text. Diagnostics in `RmQRCodeDecodeInfo` (status, version, ECC level, corrected codewords). Overloads mirror `MicroQRCodeDecoder`: string result, and an allocation-free span-destination variant sized by `GetMaxDecodedLength(version)`.

### Image level

Planned (Phase 7): shared Otsu binarization + finder candidates → format read at the finder side → dimensions known → sub-finder confirmation → projective sampling → this matrix decoder.

### Supported

| Area | Coverage |
|---|---|
| Versions | All 32 (R7x43 … R17x139), identified by width × height and cross-checked against the format information |
| ECC levels | M, H |
| Data modes | Numeric, Alphanumeric, Byte (UTF-8 / ISO-8859-1 heuristics as the other symbologies), ECI headers 1 / 3 / 26 / 27 |
| Quiet zone | Any light border, uniform or not (dark bounding box = core) |
| Error correction | Reed-Solomon per block at full strength ⌊ecc/2⌋ codewords per block, corrections reported |
| Format information | Either copy alone suffices; ≤ 3 bit errors per copy corrected; the closer valid copy wins |
| Output | `string` (allocates the result only) or `Span<char>` (allocation-free steady state) |

### Not supported

- Kanji mode segments (`UnsupportedContent`), FNC1, Structured Append (rMQR does not define it), ECI assignments other than the four above (`UnsupportedContent`)
- Image input (Phase 7)

## Why

- Explicitly typed decoder (no auto-detection inside `QRCodeDecoder`): the same reasoning as Micro QR, Standard QR scanning performance and behavior stay untouched; a caller who has an rMQR matrix knows it.
- Version from the dimensions, not from the format information alone: a matrix whose format copies decode to another version is corruption (or a wrong crop), not a smaller symbol; rejecting the contradiction is the safer default (`FormatInformationInvalid`).
- Dark-bounding-box quiet-zone stripping: rMQR has dark modules at all four core corners (finder, two corner patterns, sub-finder) and timing patterns on every edge, so the bounding box of dark modules is exactly the core, no uniform-border assumption is needed (Micro QR needed the finder-corner trick because its right/bottom edges are data).
- Full RS strength per block, mirroring the Standard QR decoder: whether ISO/IEC 23941 reserves misdecode-protection codewords the way ISO/IEC 18004 Table 9 does for Micro QR could not be confirmed (specification text not available here); zxing-cpp also corrects at full strength on rMQR (it silently fixed qrtool's tail defect). Recorded as open, see Decisions.

## Decisions

| Decision | Choice | Revisit when |
|---|---|---|
| Correction cap | Full RS strength (⌊ecc/2⌋ per block), as Standard QR | The ISO/IEC 23941 text (Table 8) is available: if it lists misdecode-protection codewords, add the post-correction cap and its false-positive test class as `MicroQRMatrixDecoder` has |
| Format copy arbitration | Closer valid copy wins, ties → finder side; version must equal the dimensions | - |
| ECI on decode | Parsed (shared reader lifted to `SegmentDecoders`), mapped to ISO-8859-1 / UTF-8; others → `UnsupportedContent` | Demand for other charsets (cross-symbology decision) |
| Corpus expectations | libzint symbols decode with 0 corrections, qrtool symbols with ≤ 1 (documented tail defect) | qrtool fixes the defect (regenerate, tighten to 0) |

## Lessons learned

- With the encoder proven module-exact against two external lineages, the decoder round trip is a weak test on its own; the strong ones were the corpus (144 external symbols, both lineages, payload + version + ECC) and the damage classes (t per block corrected in every block of every version × ECC, t + 1 in one block never decoding cleanly, format copies singly / jointly damaged, format-vs-dimension contradiction, remainder bits ignored).
- The two format copies make the format decoder trivially robust: one copy can be destroyed outright; the useful arbitration rule is "closer valid copy wins", and it needs no extra state.
- The corpus's qrtool tail defect turned into a free robustness fixture: those symbols decode with exactly one corrected codeword, so the fixture test asserts `ErrorsCorrected ≤ 1` for that lineage and `0` for libzint.
