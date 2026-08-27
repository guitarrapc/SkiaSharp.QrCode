# Generator API Reshaping: Options Structs and Version Ranges

## Purpose

This document defines the order in which the three generators' public method surface is reshaped from long optional-parameter lists into per-symbology options structs, and how the first new option that motivated the change (a version *range* rather than a single fixed version) is added on top.

It defines WHAT is built, in WHICH order, and WHY. HOW each piece is verified follows the mandatory test-first workflow.

## The problem, in numbers

| Generator | Public static methods | Max parameters | Shipped in a release? |
|---|---:|---:|---|
| `QRCodeGenerator` | 6 | 6 | 5 of 6 (all but `TryGetRequiredBufferSize`) |
| `MicroQRCodeGenerator` | 5 | 4 | 4 of 5 (all but `TryGetRequiredBufferSize`) |
| `RmQRCodeGenerator` | **12** | **8** | **none** |

`RmQRCodeGenerator` is where the shape has already failed, and it failed in two visible ways.

- **An option escaped into method names.** `EciMode` could not be added to the parameter list without disturbing the positional order, so `CreateRmQRCodeWithEci`, `GetRequiredBufferSizeWithEci` and `TryGetRequiredBufferSizeWithEci` were added instead, doubling the method count. Standard QR passes the same option as an ordinary parameter, so the two symbologies now express one concept two different ways.
- **Parameter order stopped meaning anything.** `RmQRSegmentation` was added as a trailing optional argument to four methods, because trailing is the only position that preserves source compatibility. `requestedVersion, fitStrategy, height, quietZoneSize, segmentation` is an append log, not a design.

Adding `minVersion` / `maxVersion` in the same style would introduce 2 parameters across 23 methods and take rMQR to 10 parameters. The shape has to change before the next option lands, not after.

## The release window (this is what sets the order)

Verified against the tags: the newest release is **1.1.1 (2026-07-19)**, and `src/SkiaSharp.QrCode/RmQRCodeGenerator.cs` does not exist at that tag. rMQR was merged on 2026-08-16, into the still-unreleased 1.2.0.

**No rMQR public API has ever been in a NuGet package.** The consequences drive the whole plan:

- **rMQR can be reshaped by deletion, at zero compatibility cost, until 1.2.0 ships.** No `[Obsolete]` period, no forwarding shims, no removal deferred to a major version. `CreateRmQRCodeWithEci` and its two siblings just go.
- **This window closes on release.** If 1.2.0 ships first, the same change costs an obsolete cycle and a 2.0.0.
- **Standard QR and Micro QR have no such window.** Their pre-1.2.0 overloads are frozen and stay, whatever else happens. Only `TryGetRequiredBufferSize` on those two (added in unreleased 1.2.0) is still free to move, and it is not worth moving on its own.

**Phases 0 through 5 all gate the 1.2.0 release.** rMQR is the part that is *impossible* to do later at this cost, but shipping only Phase 1 would release a version in which rMQR takes an options struct and the other two symbologies take parameter lists, which is a worse public API than either end state. The whole reshaping goes out together, and 1.2.0 waits for it.

Within that gate, rMQR still goes first, because it is the phase whose cost would change if the release slipped past it.

## Scope

**In scope.**

- Per-symbology options structs: `QRCodeGeneratorOptions`, `MicroQRCodeGeneratorOptions`, `RmQRCodeGeneratorOptions`.
- Deletion of the rMQR `*WithEci` family and of the long-parameter rMQR overloads, replaced by an options-based surface.
- Version range types for the two symbologies whose versions are totally ordered: `QRCodeVersionRange` (1-40) and `MicroQRVersionRange` (M1-M4), subsuming today's `requestedVersion`.
- Public API approval tests, as the mechanism that makes every step above reviewable.
- Builder wiring (`WithVersionRange`) and documentation.

**Out of scope.**

- **ECC boosting** (raise the ECC level while the version stays the same). It is a behaviour change to shipped output and needs its own oracle verification. The options struct is what makes it cheap to add later; adding it here would confuse an API change with an encoding change.
- **Mixed-mode segmentation for Standard QR and Micro QR.** Same reasoning, larger. It is the highest-value follow-up and the options struct is a prerequisite for it, not a part of it.
- **Obsoleting the released Standard QR and Micro QR overloads.** They keep working and stay un-obsoleted through this work; see the decision below.
- **Decoder APIs.** They have no option-list problem.

## Guiding Decisions

### One options struct per symbology, not one shared one

A single shared options type was considered and is **not expressible**. The measured option sets:

| Option | Standard QR | Micro QR | rMQR |
|---|---|---|---|
| Version | `int`, 1-40 | `MicroQRVersion`, M1-M4 | `RmQRVersion`, 32 values |
| QuietZoneSize | `int`, default **4** | `int`, default **2** | `int`, default **2** |
| EciMode | yes | **no** | yes |
| Utf8BOM | yes | **no** | **no** |
| FitStrategy | no | no | `RmQRFitStrategy` |
| Height | no | no | `RmQRHeight?` |
| Segmentation | no | no | `RmQRSegmentation` |

Two facts settle it.

- **`Version` is three different types.** A shared struct would need generics or three mutually exclusive nullable fields, and the second option turns a compile-time error ("Micro QR has no version 27") into a runtime one.
- **The one genuinely shared member has three different defaults**, and they are not arbitrary: ISO/IEC 18004 requires a 2-module quiet zone for Micro QR against 4 for Standard QR. A shared struct whose `default` is correct for one symbology is silently wrong for the other two, which is exactly the failure mode the sentinel decision below exists to prevent.

The intersection of the three sets is one `int` with three different correct defaults. There is nothing to share. Three structs mirror the existing `QRCodeData` / `MicroQRCodeData` / `RmQRCodeData` split and let each one be exactly its symbology's option set, with invalid combinations unrepresentable rather than rejected at runtime.

### The type name is `{consuming generator}Options`

`QRCodeGeneratorOptions`, `MicroQRCodeGeneratorOptions`, `RmQRCodeGeneratorOptions`. Each name is the consuming type plus `Options`, so the pairing is mechanical and no reader has to work out which generator takes which struct. This follows the BCL habit of naming an options bag after its consumer (`JsonSerializerOptions` for `JsonSerializer`) and leaves `{X}DecoderOptions` free under the same rule if the decoders ever need one.

`QRCodeEncodeOptions` was the alternative and is rejected on vocabulary: the public surface says *generator* (`QRCodeGenerator`, `MicroQRCodeGenerator`, `RmQRCodeGenerator`) and *encoder* is an internal word (`QRBinaryEncoder`, `EccBinaryEncoder`, all under `Internals`). Naming a public type after the internal vocabulary would introduce a second word for one concept.

### `readonly record struct`, passed by `in`

The generators are the allocation-free path; an options *class* would put an allocation in front of every `Span<byte>` overload and defeat the point. `record struct` supplies `with`, value equality and a `ToString` for free, which makes call sites read as `QRCodeGeneratorOptions.Default with { ... }`.

The cost is real and has to be designed for rather than discovered: **any option whose natural default is not the zero value needs a sentinel**, because `default(T)` must remain a valid, correct "everything default" value. Today that is `QuietZoneSize` (default 4, and 0 is a legitimate caller choice, so the backing field stores `value + 1` and 0 means unset). Any future option in the same situation must do the same. An options struct whose `default` is subtly wrong is worse than the parameter list it replaced.

### `requestedVersion` and a version range do not coexist

A struct carrying both `RequestedVersion` and `MinVersion` / `MaxVersion` admits contradictions (`requested = 5, max = 3`) that can only be resolved by throwing. Under the contract recorded in [rmqr-encoder.md](../specs/rmqr-encoder.md), `Try*` returns `false` for "does not fit" and throws for contradictory arguments, so that design buys nothing and enlarges the throwing surface.

The range is the single concept and the fixed version is its degenerate case: `QRCodeVersionRange.Exactly(n)`. The retained legacy `requestedVersion` parameter maps to `Exactly(n)`, and `-1` maps to `Any`. Validation (`min <= max`, both in 1-40) lives in the range type's factory methods, so it happens once at construction instead of in every generator entry point.

`default(QRCodeVersionRange)` must mean the full range. Version 0 does not exist, so 0 is a safe "unset" sentinel in the backing fields.

### The range applies to Standard QR and Micro QR only, and rMQR keeps its own vocabulary

Standard QR versions 1-40 and Micro QR M1-M4 are totally ordered by capacity, so "at least" and "at most" mean something. **rMQR's 32 versions are not totally ordered**: R7x43, R9x43 and R7x59 have no min/max relation. rMQR already has the correct vocabulary for its constraint space in `RmQRFitStrategy` and `RmQRHeight`, and forcing a version range onto it for the sake of symmetry would produce an option that cannot be given a meaning. If rMQR later needs a bound, it is a width bound in modules, not a version range.

Resisting symmetry here is a feature of having three structs rather than one.

### Defaulted `in` parameters are legal for rMQR and forbidden for the other two

`in RmQRCodeGeneratorOptions options = default` is valid C# and gives rMQR a single method per operation, because there are no released overloads to collide with.

Standard QR and Micro QR keep their released overloads, so their options overload **must not** default its `options` parameter: `CreateQrCode(text, ECCLevel.M)` would otherwise be ambiguous between the legacy overload's defaults and the new one's. Callers there write `QRCodeGeneratorOptions.Default with { ... }` explicitly, which is the correct outcome anyway, since the two-argument convenience call already exists.

This asymmetry is deliberate and must be stated in the specs; discovering it as a compiler error later is how someone talks themselves into defaulting the legacy parameters instead.

### The released overloads are kept and not obsoleted in this work

`CreateQrCode(text, ECCLevel.M)` is the headline of the root README and the shortest correct call in the library. It is not a legacy shim, it is the intended entry point, and it stays un-obsoleted. What changes is the **rule**: it and its siblings are frozen at their current parameter lists, and no future option is ever added to them again.

Whether the wider Standard QR / Micro QR parameter lists (`utf8BOM`, `eciMode`, `requestedVersion`, `quietZoneSize` positional) should be `[Obsolete]`-marked so callers migrate to the options struct is a decision **deferred until Phase 5 is complete**, deliberately not taken now. Taking it early would mean designing the migration path against an options struct that has not been used by anything yet; after Phase 5 the builders, samples, benchmarks and Playground have all been through it, which is the evidence the decision needs. The v1.0.0 removal of the obsolete `QrCode` class is the precedent for how the obsolete-then-remove cycle runs here when it is taken.

### Prior art

The options-struct shape is not novel; the specific reason for choosing it over more overloads is the count in the table above. Two .NET QR libraries were read while deciding: `Net.Codecrete.QrCodeGenerator` reached 9 parameters on `EncodeTextAdvanced` and suppresses the analyser warning rather than changing shape, which is the outcome this plan exists to avoid; it also supplies the `minVersion` / `maxVersion` semantics being adopted here. `QRCoder` supplied the public API approval test pattern used in Phase 0.

## Implementation Order

### Phase 0, freeze the surface with public API approval tests

Blocking: every later phase is reviewed through this diff.

- New `tests/SkiaSharp.QrCode.ApiTests` project using `PublicApiGenerator`, generating the public API of `SkiaSharp.QrCode` for each of the four TFMs (netstandard2.0, netstandard2.1, net8.0, net10.0) and comparing against committed approved files.
- Follow the established pattern of one approved file per group of TFMs that produce identical output, and a separate file per TFM that diverges. The `#if NET8_0_OR_GREATER` blocks in this library make divergence likely, so the test must report which TFMs differ rather than merely failing.
- Wire into CI alongside the existing test run.

Deliverable: a baseline approved file set at current HEAD. **No production code changes in this phase**, so the diff is purely additive and the baseline is trustworthy.

### Phase 1, rMQR options struct and deletion of the legacy surface

Every phase gates 1.2.0, but this is the one whose *cost* changes if the release slips past it, so it goes first.

- Introduce `RmQRCodeGeneratorOptions` (`EciMode`, `Version`, `FitStrategy`, `Height`, `QuietZoneSize`, `Segmentation`), `readonly record struct`, `Default` static, quiet-zone sentinel as decided above.
- Replace the 12 public methods with 5, each taking `in RmQRCodeGeneratorOptions options = default`:
  - `CreateRmQRCode(string, RmQREccLevel, in RmQRCodeGeneratorOptions)`
  - `CreateRmQRCode(ReadOnlySpan<char>, RmQREccLevel, in RmQRCodeGeneratorOptions)`
  - `CreateRmQRCode(ReadOnlySpan<char>, RmQREccLevel, Span<byte>, in RmQRCodeGeneratorOptions)`
  - `GetRequiredBufferSize(ReadOnlySpan<char>, RmQREccLevel, in RmQRCodeGeneratorOptions)`
  - `TryGetRequiredBufferSize(ReadOnlySpan<char>, RmQREccLevel, out RmQRCodeCalculatedSize, in RmQRCodeGeneratorOptions)`
- **Delete** `CreateRmQRCodeWithEci`, `GetRequiredBufferSizeWithEci`, `TryGetRequiredBufferSizeWithEci` outright. The ECI-explicit path is `options with { EciMode = ... }`.
- Update the in-repo call sites: roughly 191 across tests, samples, benchmarks and the Playground, of which about 62 use a `*WithEci` method. Mechanical, but it is the bulk of the phase's diff and must not be mixed with behaviour changes.
- Behaviour must be bit-identical. The existing rMQR test suite is the proof and must pass with only call-shape edits, no expectation edits. **An expectation change in this phase is a defect**, not a rebaseline.

Exit criteria: approval diff shows 12 methods removed and 5 added, nothing else; full suite green on net8.0 and net10.0; encode benchmarks within the guardrail (see Risks).

### Phase 2, Standard QR and Micro QR options structs, additive

- `QRCodeGeneratorOptions` (`EciMode`, `Utf8BOM`, `Version`, `QuietZoneSize`) and `MicroQRCodeGeneratorOptions` (`Version`, `QuietZoneSize`).
- Add one options overload per operation, `in` parameter **without** a default value.
- Reimplement the released overloads as thin forwarders that build an options value. Their signatures, exceptions, messages and output are unchanged.
- The existing Standard QR and Micro QR tests must pass **untouched**. Unlike Phase 1 there are no call-shape edits either, so any test file change in this phase is a defect.

### Phase 3, version ranges

- `QRCodeVersionRange` (backing `byte` min/max, 0 = unset) with `Any`, `Exactly`, `AtLeast`, `AtMost`, `Between`; `MicroQRVersionRange` over `MicroQRVersion`.
- Wire into version selection: choose the smallest version within the range that fits, instead of the smallest version overall.
- Contract, consistent with the existing sizing rules: content that fits no version in the range makes `TryGetRequiredBufferSize` return `false`; an invalid range (`min > max`, out of 1-40) throws from the range factory, before any generator is called. Record explicitly that a Micro QR range whose versions cannot carry the text's required mode is a `false`, not a throw, matching the rule already documented for `TryGetRequiredBufferSize`.
- Equivalence classes to cover: range excludes everything that fits (below and above); range whose only fitting member is its minimum; range whose only fitting member is its maximum; `Exactly(n)` reproducing today's `requestedVersion` output byte for byte across a sweep; `Any` reproducing today's automatic selection byte for byte across a sweep.
- The two byte-for-byte sweeps are what prove the range did not change existing behaviour, and they are the reason this is a separate phase from Phase 2.

### Phase 4, builders

- `QRCodeImageBuilder.WithVersionRange(QRCodeVersionRange)` and the Micro QR equivalent. `WithVersion(int)` stays and maps to `Exactly`.
- Builders construct an options value internally rather than passing positional arguments, which removes the long call in `QRCodeImageBuilder`.
- The rMQR builder gains nothing new here beyond forwarding through `RmQRCodeGeneratorOptions`; its existing `WithFitStrategy` / `WithHeight` / `WithSegmentation` are already the right surface.

### Phase 5, specs, migration, and measurement

- `specs/rmqr-encoder.md`: the API section transcribes the deleted signatures verbatim and is wrong the moment Phase 1 lands. Rewrite it around the options struct and record why rMQR has no version range.
- `specs/qrcode-symbologies.md`: add the API direction rule (options struct is where new options go; the released convenience overloads are frozen) so it is stated once, in the shared record.
- `specs/standardqr-encoder.md` and the Micro QR scope: version range semantics.
- `docs/migration.md`: document the new surface, **and fix an existing error found while writing this plan**. The "rMQR mixed-mode segmentation" section tells the reader that adding `segmentation` is "binary breaking: assemblies compiled against v1.1.1 or earlier must be recompiled". No assembly can be compiled against those methods, because rMQR does not exist in v1.1.1. The whole section describes changes to an API that never shipped and should be folded into the 1.2.0 rMQR introduction instead.
- Root `README.md`: the zero-allocation example and the rMQR examples change shape.
- Measurement: encode benchmarks for all three symbologies, before and after, allocating and `Span` overloads.
- Update the [documentation index](../README.md) with this plan.

## Risks

- **Passing options by `in` through the encode path could cost time.** The generators currently take scalars that the JIT keeps in registers through several inlined layers; a struct behind an `in` reference is not automatically free. This is the one performance-relevant risk in the plan and the reason Phase 1 and Phase 2 each carry a benchmark gate rather than deferring measurement to Phase 5. If a regression appears, the likely fix is passing the struct by value (it is small) rather than reverting the shape.
- **The quiet-zone sentinel is the kind of detail that survives review and fails in production.** A test that constructs `default(T)` and asserts a quiet zone of 4, for each of the three structs, is mandatory in the phase that introduces them.
- **Phase 1 is a large mechanical diff.** Mixing any behaviour change into it makes the approval diff unreadable. If something needs fixing in rMQR encoding, it is a separate commit before or after, never inside.

## Working Rules

- Test-first is mandatory for every `src/` change: failing test, then implementation, then the full suite on net8.0 and net10.0.
- Every phase's public API approval diff is reviewed as part of that phase, not at the end.
- Phase 1 and Phase 2 are behaviour-preserving by definition. A changed test expectation in either is a defect to investigate, not a baseline to update.
- Progress logging (mandatory): when a phase completes, append an entry to the Progress log below recording what was done, lessons learned, and benchmark deltas, or an explicit statement of why benchmarks are not applicable.

## Decisions taken, 2026-08-28

The three questions this plan opened with are settled. Recorded here so a later reader does not reopen them.

1. **Phases 0 through 5 gate the 1.2.0 release**, not Phase 1 alone. Shipping a partial reshaping would put two different API shapes in one release.
2. **Obsoleting the released Standard QR / Micro QR overloads is revisited after Phase 5**, with the intent to move callers onto the options struct. Not decided now, and nothing in Phases 0-5 depends on the answer.
3. **ECC boosting and Standard QR mixed-mode segmentation stay out**, and do not become Phases 6 and 7 of this plan. Both change generated output and need oracle verification this plan has none of; the options struct is what makes them cheap afterwards.
4. **The naming question is settled in favour of `{consuming generator}Options`**, with the reasoning recorded under Guiding Decisions. The related question it raised, whether the options type needs to vary by symbology at all, is answered by the option-set matrix in the same section: it does, and a shared type is not expressible.

No open decisions remain. New ones discovered during implementation are appended to the Progress log entry for the phase that found them.

## Progress log

_No phases completed yet._
