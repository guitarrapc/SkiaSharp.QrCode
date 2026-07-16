# Standard QR Encoder

Design record for the Standard QR encode feature (`QRCodeGenerator`): what it does, why the pipeline is structured this way, and what was learned while making the implementation spec-compatible and fast. Normative details and implementation locations are indexed in the [spec-to-code map](standardqr-spec-map.md). The inverse pipeline is documented in [Standard QR Decoder](standardqr-decoder.md).

---

## What

`QRCodeGenerator` converts text into a Standard QR module matrix through the complete ISO/IEC 18004 encoding pipeline:

```
Text
  -> mode / ECI analysis
  -> version selection
  -> data bit stream and padding
  -> Reed-Solomon ECC per block
  -> data / ECC interleaving
  -> function-pattern and data placement
  -> best-of-8 mask selection
  -> format and version information
  -> QRCodeData or byte-per-module matrix
```

### Public entry points

The encoder exposes two output models.

#### `QRCodeData`

`CreateQrCode(string|ReadOnlySpan<char>, ...)` returns a `QRCodeData` object.

- The core matrix is stored bit-packed, one bit per module.
- The quiet zone is virtual: it changes the public coordinate space but consumes no payload storage.
- The matrix is deterministic for the same text and options.
- This is the convenient object model used by renderers and serialization.

#### Caller-provided matrix buffer

`CreateQrCode(string|ReadOnlySpan<char>, ..., Span<byte> destination, ...)` writes:

- one byte per module;
- `0` for light and `1` for dark;
- flat row-major order;
- quiet zone included.

`GetRequiredBufferSize` returns the required matrix side, byte count, and automatically selected version. The encoder clears exactly the written region, accepts a dirty pooled destination, and leaves any tail beyond the returned byte count untouched. After JIT and pool warm-up, the span path is allocation-free in Release builds.

### Supported

| Area | Coverage |
|---|---|
| Symbology | Standard QR |
| Versions | 1–40 |
| ECC levels | L, M, Q, H |
| Data modes | Numeric, Alphanumeric, Byte |
| ECI | Default/no header, ISO-8859-1 (assignment 3), UTF-8 (assignment 26) |
| UTF-8 BOM | Optional in UTF-8 Byte mode |
| Version selection | Automatic minimum-fit or caller-requested version |
| Quiet zone | Configurable non-negative size; span sizing/output rejects dimensions that cannot fit an `int`-sized matrix |
| Output | Bit-packed `QRCodeData` or byte-per-module `Span<byte>` |

### Not implemented

- Kanji mode
- FNC1
- Structured Append
- Arbitrary ECI assignment numbers
- Arbitrary binary payload input
- Multi-segment optimization within one payload
- Micro QR and rMQR

The current encoder analyzes the complete input once and emits one data segment. For example, mixed text such as a long numeric prefix followed by lowercase text is encoded entirely in Byte mode instead of being split into Numeric and Byte segments. The output remains valid, but can require a larger version than a globally optimized multi-segment encoder.

---

## Pipeline

### 1. Validate the matrix request

All generation overloads reject:

- requested versions outside `1..40` (except `-1`, meaning automatic);
- negative quiet-zone sizes.

`GetRequiredBufferSize` and the span-output overload additionally reject quiet zones whose resulting side or squared byte count exceeds `int.MaxValue`. The span overload also rejects caller-provided buffers smaller than the calculated matrix. These paths compute dimensions with `long` arithmetic before narrowing to `int`, preventing overflow in `coreSize + 2 * quietZoneSize` and `totalSize * totalSize`.

### 2. Analyze text and choose mode / ECI

`TextAnalyzer` performs a single pass over the UTF-16 input and classifies the entire payload:

1. Numeric when every character is `0..9`.
2. Alphanumeric when every character belongs to the 45-character QR alphabet.
3. Byte otherwise.

Empty input is deliberately represented as a zero-length Byte segment. The standard does not define a special empty-data mode, and Byte mode gives the least surprising representation.

With `EciMode.Default`, Byte-mode character encoding is selected as follows:

| Input | Effective ECI | Header |
|---|---|---|
| ASCII only | Default | none |
| Contains non-ASCII, but every character is in U+0000..U+00FF | ISO-8859-1 | ECI 3 |
| Any character above U+00FF | UTF-8 | ECI 26 |

Numeric and Alphanumeric payloads do not need character-set conversion, although an explicitly requested non-default ECI is still emitted before the data-mode indicator.

Explicit ECI is a caller constraint. In particular, forcing `Iso8859_1` is only semantically correct for text in U+0000..U+00FF; `Default` avoids an incompatible choice by upgrading such input to UTF-8.

On supported x86/x64 runtimes, analysis uses AVX2 or SSE2 for character-class checks, with a scalar path for short inputs and other targets.

### 3. Select the version

Automatic selection scans versions 1 through 40 and picks the first whose data-codeword capacity can hold:

```
optional ECI header
+ 4-bit mode indicator
+ version-dependent character-count indicator
+ encoded payload bits
```

The character-count width changes at versions 10 and 27:

| Version range | Numeric | Alphanumeric | Byte |
|---|---:|---:|---:|
| 1–9 | 10 | 9 | 8 |
| 10–26 | 12 | 11 | 16 |
| 27–40 | 14 | 13 | 16 |

Byte-mode capacity is calculated from encoded byte count, not UTF-16 `char` count. UTF-8 BOM contributes three bytes to both capacity selection and the Byte-mode character-count indicator.

The version calculation does not reserve four mandatory terminator bits: the terminator is allowed to shrink to the remaining capacity, including zero bits for an exact fit. If no version can hold the required header and payload bits, generation fails instead of truncating.

When `requestedVersion` is supplied, automatic selection is bypassed. It is intended for callers that need a fixed symbol size and already know the payload fits.

### 4. Build the data codewords

`QRBinaryEncoder` writes MSB-first through `BitWriter`:

1. Optional ECI indicator `0111` and 8-bit assignment number.
2. Data mode indicator.
3. Character-count indicator.
4. Mode-specific payload.
5. Up to four zero terminator bits.
6. Zero bits to the next byte boundary.
7. Alternating pad codewords `0xEC`, `0x11` until the data capacity is full.

Mode-specific packing is:

| Mode | Packing |
|---|---|
| Numeric | 3 digits → 10 bits; final 2 → 7 bits; final 1 → 4 bits |
| Alphanumeric | 2 values → `first * 45 + second` in 11 bits; final 1 → 6 bits |
| Byte | 8 bits per encoded byte |

Byte mode uses ISO-8859-1 narrowing or UTF-8 encoding. Temporary charset buffers use `stackalloc` up to 256 bytes and `ArrayPool<byte>` above that threshold. `BitWriter` stages bits in a 64-bit accumulator and bulk-writes big-endian words; Byte-mode data is copied eight bytes at a time where possible.

### 5. Generate Reed-Solomon error correction

The selected version and ECC level identify:

- total data codewords;
- ECC codewords per block;
- Group 1 and Group 2 block counts;
- data codewords per block in each group.

Data codewords are partitioned by that table, and `EccBinaryEncoder` calculates the Reed-Solomon remainder independently for each block over GF(256), using primitive polynomial `0x11D` and generator roots `alpha^0..alpha^(n-1)`.

The public dispatch selects the fastest available kernel while preserving byte-identical results:

- GFNI / SSSE3 on supported x86/x64 targets;
- AdvSimd on ARM64;
- cached log-domain scalar implementation elsewhere.

Every optimized kernel is parity-tested against a deliberately naive polynomial-division reference.

### 6. Interleave the final message

`BinaryInterleaver` emits:

1. data codeword 0 from every block, then data codeword 1 from every block, and so on;
2. the extra final data row from the longer Group 2 blocks, when present;
3. ECC codeword 0 from every block, then ECC codeword 1, and so on;
4. zero remainder bits for the selected version.

The implementation writes the output sequentially and accepts strided source reads. A one-block symbol takes an identity fast path. Remainder-bit storage is cleared explicitly so uninitialized stack or pooled memory cannot affect the matrix.

### 7. Place function patterns and data

The matrix starts as a zeroed byte-per-module core. The encoder places or reserves:

- three 7×7 finder patterns;
- one-module separators;
- alignment patterns for version 2+;
- horizontal and vertical timing patterns;
- the fixed dark module;
- both format-information areas;
- both version-information areas for version 7+.

Reserved modules are represented by a compact bit mask. The same `PlaceFunctionModules` routine is reused by the decoder when it needs to distinguish function modules from data modules, keeping both directions structurally identical.

Interleaved bits are then consumed MSB-first in the standard two-column zigzag from bottom-right to top-left, skipping column 6 and every reserved module. The hot path handles both modules in a strip row together and keeps up to 64 pending stream bits in a register.

### 8. Evaluate all eight masks

The encoder tests every Standard QR mask pattern and chooses the lowest ISO/IEC 18004 penalty score:

1. long same-color runs;
2. 2×2 same-color blocks;
3. finder-like `1:1:3:1:1` patterns with the required light margin;
4. deviation from 50% dark modules.

Each candidate is scored as the final symbol will appear:

- the mask is applied only to data modules;
- candidate-specific format bits are inserted before scoring;
- version bits are included for version 7+.

This matters because format modules participate in the visual penalty rules and can change which mask wins. Ties are deterministic: the lower mask index wins because candidates are visited in order and only a strictly lower score replaces the current best.

Masking and scoring operate on packed rows rather than byte-per-module loops:

- versions 1–11 fit each row in one `ulong`;
- versions 12–40 use a fixed 192-bit row made from three `ulong` values.

The eight formulas are precomputed as 12-row periodic templates. XOR, shifts, and popcount implement both masking and all four penalty rules without changing the reference result. Parity tests compare this representation against straightforward textbook formulas.

### 9. Write format / version information and expose output

After the winning mask is applied:

- BCH(15,5) format information encodes ECC level and mask index, applies the standard format mask, and is written twice;
- BCH(18,6) version information is written twice for versions 7–40.

For `QRCodeData`, the temporary core matrix is packed into the object's one-bit-per-module payload; the quiet zone remains virtual.

For span output:

- with quiet zone 0, the core pipeline writes directly into the destination;
- with a quiet zone, the contiguous core is built in a pooled temporary and copied row-by-row into the centered destination.

The encoder produces a module matrix, not an image. Color, pixels-per-module, shapes, gradients, icons, PNG/SVG encoding, and other presentation concerns belong to `QRCodeRenderer` / `QRCodeImageBuilder`.

---

## Why

- **One canonical binary pipeline.** String and span inputs, object and span outputs, and all rendering APIs ultimately depend on the same mode, ECC, interleaving, placement, and masking logic.
- **Tables drive structural correctness.** Capacity, block grouping, alignment centers, and remainder counts come from centralized Standard QR tables rather than duplicated conditionals.
- **Encoder-decoder parity by construction.** Function-module layout, format generation, and block conventions are shared or tested in both directions.
- **Independent validation.** ZXing decodes generated symbols across modes, ECI choices, ECC levels, boundary capacities, and large versions. The in-process decoder adds all-version round trips and error-injection coverage.
- **Optimizations are representation changes, not algorithm changes.** The production kernels are guarded by parity tests against simple reference implementations for ECC, interleaving, placement, and mask scoring.
- **Deterministic output.** Version scan order, block order, interleaving order, mask tie-breaking, zeroed remainder bits, and clean destination handling make repeated calls byte-for-byte stable.

---

## Decisions

- **Single segment per input.** It keeps the API and implementation auditable and makes mode selection a single pass. The trade-off is non-minimal symbols for mixed-mode payloads.
- **No Kanji mode.** Unicode input is represented as UTF-8 Byte mode with ECI 26. This avoids Shift-JIS tables and an additional encoding dependency, at the cost of lower capacity for Japanese text.
- **ASCII omits ECI by default.** This minimizes overhead and maximizes compatibility. Latin-1 and wider Unicode receive explicit ECI declarations under automatic selection.
- **BOM is explicit and UTF-8-only.** `utf8BOM` affects the stream only when the selected data mode is Byte and the effective ECI is UTF-8.
- **Version can be forced.** Fixed-size applications need control over symbol dimensions, so `requestedVersion` bypasses minimum-fit selection rather than acting as a lower bound.
- **Quiet zone is output policy, not core symbol data.** Core encoding is always performed on the `21 + 4 * (version - 1)` matrix. Quiet-zone storage differs by output model without changing encoded modules.
- **Mask scoring includes final metadata.** Scoring a data-only candidate can choose a different winner from scoring the actual final matrix, so format and version information are part of candidate evaluation.

---

## Lessons Learned

### Capacity and text encoding

- **Byte-mode length means encoded bytes, not UTF-16 characters.** This is the central boundary condition for UTF-8 input; using `text.Length` would under-size every non-ASCII payload and make version transitions wrong.
- **The UTF-8 BOM belongs in the Byte character count.** Treating it as out-of-band metadata creates symbols that some readers reject because the declared count is three bytes short.
- **ECI overhead can change the version and the mask.** Twelve header bits can cross a version boundary; even when they do not, they shift every following bit, which changes ECC, interleaving, placed data, and often the selected mask.
- **Exact-capacity inputs need no full terminator.** Padding must add `min(remaining, 4)` terminator bits rather than assuming four bits are always available.

### Matrix construction

- **Function areas need one shared source of truth.** Placement, data walking, masking, and decoding all depend on exactly the same blocked-module geometry. Reconstructing it independently is an invitation for one-module drift around format, alignment, or version areas.
- **Remainder bits must be deterministic even though they carry no payload.** Stack and pooled buffers are not guaranteed to be zeroed; leaving the tail untouched makes output depend on prior memory contents.
- **Mask candidates must contain their own format bits.** The 30 format modules affect runs, 2×2 blocks, finder-like windows, and dark balance. Scoring without them is observably not the same algorithm.
- **The quiet zone should not inflate object storage.** Keeping it virtual reduced `QRCodeData` to core bits only while preserving the public matrix coordinate space.

### Performance

- **Bit-packing was the decisive mask optimization.** Parallelizing eight expensive byte-domain candidates still pays the byte-domain cost and adds scheduling/allocation overhead. Packed scalar rows measured roughly 8× at version 1, 44× at version 10, and 30–40× at version 40 over the former per-module implementation.
- **Sequential output wins during interleaving.** Round-robin source reads with a contiguous destination measured better than sequential source reads with scattered writes, despite the strided access.
- **The data placement stream should stay in a register.** Refilling a 64-bit MSB-aligned accumulator removes a byte load and variable shift from each module and enables a two-module fast path for the common unblocked case.
- **Reed-Solomon setup is reusable.** Generator polynomials depend only on ECC count, so caching their log-domain form removes repeated polynomial construction and reduces the scalar inner loop to table lookup and XOR.
- **Steady-state allocation guarantees require warm-up-aware tests.** Lazy tables, JIT compilation, and `ArrayPool` initialization are one-time effects; the Release-only allocation test warms them before measuring the span API.

---

## Validation

The encoder is covered at several independent layers:

| Layer | Evidence |
|---|---|
| Bit stream | mode, ECI, count widths, Numeric/Alphanumeric/Byte packing, BOM, padding, canonical `HELLO WORLD` codewords |
| Capacity | exact-fit and one-over boundaries across modes, ECC levels, and representative versions |
| ECC | ISO worked examples and scalar/SIMD parity against naive GF(256) division |
| Interleaving | unequal block groups, single-block identity, version-40 block counts, naive-reference parity |
| Placement | binary placement parity against a per-module zigzag reference |
| Masking | all-zero, all-one, and realistic matrices compared with byte-domain reference formulas |
| Output APIs | `QRCodeData` and span matrices compared module-for-module, dirty buffers, quiet-zone sizes, overflow checks, allocation test |
| External compatibility | generated images decoded by ZXing |
| Internal compatibility | encode/decode round trips for all versions and ECC levels |

When the optimized implementation and a reference disagree, the simple reference and external decoder are treated as the specification oracle; performance code is not allowed to define behavior.
