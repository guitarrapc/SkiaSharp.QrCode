# Generator API Reshaping: Options Structs and Version Ranges

## Purpose

This document defines the order in which the three generators' public method surface is reshaped from long optional-parameter lists into per-symbology options structs, and how the first new option that motivated the change (a version *range* rather than a single fixed version) is added on top.

It defines WHAT is built, in WHICH order, and WHY. HOW each piece is verified follows the mandatory test-first workflow.

## The problem, in numbers

| Generator | Public static methods | Max parameters | Shipped in a release? |
|---|---:|---:|---|
| `QRCodeGenerator` | 6 | 6 | 5 of 6 (all but `TryGetRequiredBufferSize`) |
| `MicroQRCodeGenerator` | 5 | 4 | 4 of 5 (all but `TryGetRequiredBufferSize`) |
| `RmQRCodeGenerator` | **10** | **9** | **none** |

`RmQRCodeGenerator` is where the shape has already failed, and it failed in two visible ways.

- **An option escaped into method names.** `EciMode` could not be added to the parameter list without disturbing the positional order, so `CreateRmQRCodeWithEci`, `GetRequiredBufferSizeWithEci` and `TryGetRequiredBufferSizeWithEci` were added instead, doubling the method count. Standard QR passes the same option as an ordinary parameter, so the two symbologies now express one concept two different ways.
- **Parameter order stopped meaning anything.** `RmQRSegmentation` was added as a trailing optional argument to four methods, because trailing is the only position that preserves source compatibility. `requestedVersion, fitStrategy, height, quietZoneSize, segmentation` is an append log, not a design.

Adding `minVersion` / `maxVersion` in the same style would introduce 2 parameters across 21 methods and take rMQR to 11 parameters. The shape has to change before the next option lands, not after.

## The release window (this is what sets the order)

Verified against the tags: the newest release is **1.1.1 (2026-07-19)**, and `src/SkiaSharp.QrCode/RmQRCodeGenerator.cs` does not exist at that tag. rMQR was merged on 2026-08-16, into the still-unreleased 1.2.0.

**No rMQR public API has ever been in a NuGet package.** The consequences drive the whole plan:

- **rMQR can be reshaped by deletion, at zero compatibility cost, until 1.2.0 ships.** No `[Obsolete]` period, no forwarding shims, no removal deferred to a major version. `CreateRmQRCodeWithEci` and its two siblings just go.
- **This window closes on release.** If 1.2.0 ships first, the same change costs an obsolete cycle and a 2.0.0.
- **Standard QR and Micro QR have no such window.** Their pre-1.2.0 overloads are frozen and stay, whatever else happens. Only `TryGetRequiredBufferSize` on those two (added in unreleased 1.2.0) is still free to move, and it is not worth moving on its own. **Superseded by Phase 6**: it became worth moving once it was no longer on its own, and it is deleted rather than moved.

**Phases 0 through 5 all gate the 1.2.0 release.** rMQR is the part that is *impossible* to do later at this cost, but shipping only Phase 1 would release a version in which rMQR takes an options struct and the other two symbologies take parameter lists, which is a worse public API than either end state. The whole reshaping goes out together, and 1.2.0 waits for it.

Within that gate, rMQR still goes first, because it is the phase whose cost would change if the release slipped past it.

## Scope

**In scope.**

- Per-symbology options structs: `QRCodeGeneratorOptions`, `MicroQRCodeGeneratorOptions`, `RmQRCodeGeneratorOptions`.
- Deletion of the rMQR `*WithEci` family and of the long-parameter rMQR overloads, replaced by an options-based surface.
- Version range types for the two symbologies whose versions are totally ordered: `QRCodeVersionRange` (1-40) and `MicroQRVersionRange` (M1-M4), subsuming today's `requestedVersion`.
- SDK package validation against the last released package, as the mechanism that keeps the released contract intact while the surface above it is reshaped.
- Builder wiring (a range-taking `WithVersion` overload) and documentation.

**Out of scope.**

- **ECC boosting** (raise the ECC level while the version stays the same). It is a behaviour change to shipped output and needs its own oracle verification. The options struct is what makes it cheap to add later; adding it here would confuse an API change with an encoding change.
- **Mixed-mode segmentation for Standard QR and Micro QR.** Same reasoning, larger. It is the highest-value follow-up and the options struct is a prerequisite for it, not a part of it.
- **Obsoleting the released Standard QR and Micro QR overloads.** They keep working and stay un-obsoleted through this work; see the decision below. **Superseded in part by Phase 6**: the two released *sizing* methods are obsoleted; the `Create*` overloads still are not.
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

The cost is real and has to be designed for rather than discovered: **any option whose natural default is not the zero value needs a sentinel**, because `default(T)` must remain a valid, correct "everything default" value. Today that is `QuietZoneSize`, whose default is 4 (Standard QR) or 2 (the other two) while 0 is a legitimate caller choice and so cannot double as "unset". It stores the **offset from the default** rather than a `value + 1` sentinel, for the reason recorded in the Phase 1 log: only the offset makes the canonical form unique, so writing the default explicitly produces the same value as not writing it and the generated equality does not call two identical option sets different. Any future option in the same situation must do the same. An options struct whose `default` is subtly wrong is worse than the parameter list it replaced.

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

> **Superseded in part by Phase 6 (2026-08-29).** The two released sizing methods, `QRCodeGenerator.GetRequiredBufferSize` and `MicroQRCodeGenerator.GetRequiredBufferSize`, *are* obsoleted, for a reason this section did not anticipate: the objection is not that they take a parameter list but that they throw for a routine outcome. Everything below still holds for the `Create*` overloads, which stay un-obsoleted.

`CreateQrCode(text, ECCLevel.M)` is the headline of the root README and the shortest correct call in the library. It is not a legacy shim, it is the intended entry point, and it stays un-obsoleted. What changes is the **rule**: it and its siblings are frozen at their current parameter lists, and no future option is ever added to them again.

Whether the wider Standard QR / Micro QR parameter lists (`utf8BOM`, `eciMode`, `requestedVersion`, `quietZoneSize` positional) should be `[Obsolete]`-marked so callers migrate to the options struct is a decision **deferred until Phase 5 is complete**, deliberately not taken now. Taking it early would mean designing the migration path against an options struct that has not been used by anything yet; after Phase 5 the builders, samples, benchmarks and Playground have all been through it, which is the evidence the decision needs. The v1.0.0 removal of the obsolete `QrCode` class is the precedent for how the obsolete-then-remove cycle runs here when it is taken.

### Prior art

The options-struct shape is not novel; the specific reason for choosing it over more overloads is the count in the table above. Two .NET QR libraries were read while deciding: `Net.Codecrete.QrCodeGenerator` reached 9 parameters on `EncodeTextAdvanced` and suppresses the analyser warning rather than changing shape, which is the outcome this plan exists to avoid; it also supplies the `minVersion` / `maxVersion` semantics being adopted here, and it already enables SDK package validation, which is what Phase 0 adopts.

## Implementation Order

### Phase 0, pin the released contract with SDK package validation

Blocking: this is what makes Phase 2's "additive only" claim a checked fact rather than an intention.

- `EnablePackageValidation` and `PackageValidationBaselineVersion` on the library project, baselined against the newest released package (1.1.1). The SDK then compares the package under construction against the real published artifact and errors on any breaking change, and separately cross-checks the four framework assets against each other.
- Wire into CI: validation runs on `Pack`, which `dotnet build` never invokes, so the build workflow needs an explicit pack step or a pull request never sees it. The release workflow already packs, so it is covered there for free.

**What this does and does not cover, stated plainly.** It answers *"did we break a consumer of the released package?"* authoritatively, against the published artifact rather than against a file in this repository that a developer can regenerate. It does **not** produce a reviewable listing of the surface: additions are not breaking, so the new options structs, the new overloads and the version range types pass in silence, and the Phase 1 rMQR deletion is invisible to it because no released package contains rMQR. Those are reviewed as ordinary source diffs.

That split is acceptable because the risks are not symmetric. The rMQR deletion is deliberate and legible in one file's diff; breaking a released Standard QR overload in Phase 2 is an accident that no reviewer reliably catches by eye, and that is the one this phase makes impossible.

**The baseline must be bumped to 1.2.0 once 1.2.0 is published.** From that moment the reshaped rMQR surface is itself frozen, which is exactly the release-window argument above turned into a mechanically enforced rule rather than a note in a document.

Deliverable: two properties, one CI step. **No production code changes in this phase.**

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

Exit criteria: the rMQR public surface is exactly the 5 methods listed above and the source diff shows no other API change; full suite green on net8.0 and net10.0; encode benchmarks within the guardrail (see Risks). Package validation is silent here by design, because no released package contains rMQR, so the source diff is the review.

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

- A `QRCodeImageBuilder.WithVersion(QRCodeVersionRange)` overload and the Micro QR equivalent. `WithVersion(int)` stays and maps to `Exactly`.
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

### Phase 6, sizing becomes Try-first

Phase 5 closed the reshaping; this phase corrects a shape decision inside it. Phases 2 and 3 gave the new options surface a `GetRequiredBufferSize` / `TryGetRequiredBufferSize` pair, mirroring what Standard QR shipped in 1.1.1. That pairing is wrong for this operation, and the window to fix it without cost is the same 1.2.0 window as Phase 1.

**Why the throwing form does not belong on a sizing API.** Three facts, and the first is the one that decides it.

- **"Does not fit" is an ordinary, data-dependent outcome here, not a defect.** The README already says so in its own words: Micro QR M1 holds 5 digits and rMQR holds 5-150 bytes, so overflow is a normal answer to a normal question. An exception is the wrong classification for a routine result before it is anything else.
- **The cost is not proportionate.** A .NET throw/catch is microseconds; this library's own measured encode is 142-940 ns (Phase 5 table). One exception therefore costs one to two orders of magnitude more than the entire operation it is reporting on. On any path that sizes user-supplied content, that is a denial-of-service shape, not a micro-optimisation.
- **The BCL precedent that was cited for the pair is the wrong family.** `Parse` / `TryParse` is the precedent for *parsing*. For **sizing or formatting into a caller-supplied buffer**, the modern BCL is `Try`-first and frequently has no throwing twin at all: `Utf8Formatter.TryFormat`, `Utf8Parser.TryParse`, `IUtf8SpanFormattable.TryFormat`, `TryWriteBytes`, and `Base64.EncodeToUtf8` which returns `OperationStatus` rather than throwing. Phase 2 reached for the wrong analogy.

**Deliverables.**

- **Delete, at zero compatibility cost** (all five are unreleased, added in 1.2.0):
  - `QRCodeGenerator.GetRequiredBufferSize(ReadOnlySpan<char>, ECCLevel, in QRCodeGeneratorOptions)`
  - `MicroQRCodeGenerator.GetRequiredBufferSize(ReadOnlySpan<char>, MicroQREccLevel, in MicroQRCodeGeneratorOptions)`
  - `RmQRCodeGenerator.GetRequiredBufferSize(ReadOnlySpan<char>, RmQREccLevel, in RmQRCodeGeneratorOptions)`
  - `QRCodeGenerator.TryGetRequiredBufferSize(ReadOnlySpan<char>, ECCLevel, out QRCodeCalculatedSize, bool, EciMode, int)`
  - `MicroQRCodeGenerator.TryGetRequiredBufferSize(ReadOnlySpan<char>, MicroQREccLevel, out MicroQRCodeCalculatedSize, MicroQRVersion?, int)`
- **Mark `[Obsolete]`, scheduled for removal in 2.0.0**, the two released throwing sizing methods: `QRCodeGenerator.GetRequiredBufferSize(ReadOnlySpan<char>, ECCLevel, bool, EciMode, int)` and `MicroQRCodeGenerator.GetRequiredBufferSize(ReadOnlySpan<char>, MicroQREccLevel, MicroQRVersion?, int)`. The message names the options `Try` overload as the replacement. Not `[Obsolete(error: true)]`: a warning is what a deprecation cycle is made of.
- **Move the implementation bodies into private cores.** Phase 2 deliberately made the options overloads unpack onto the parameter list ones, and recorded that "when the released overloads are eventually obsoleted, the bodies move then". This is then. The obsolete public method becomes a thin forwarder onto a non-obsolete core, so nothing inside the library calls an obsolete member and no `#pragma` appears in `src/`.
- **Migrate the in-repo call sites** across tests, benchmarks, tools, samples and the Playground.

**The resulting sizing surface**, which is the exit criterion:

| | Throwing | Non-throwing |
|---|---|---|
| `QRCodeGenerator` | `GetRequiredBufferSize(param list)` — `[Obsolete]`, gone in 2.0.0 | `TryGetRequiredBufferSize(…, in QRCodeGeneratorOptions)` |
| `MicroQRCodeGenerator` | `GetRequiredBufferSize(param list)` — `[Obsolete]`, gone in 2.0.0 | `TryGetRequiredBufferSize(…, in MicroQRCodeGeneratorOptions)` |
| `RmQRCodeGenerator` | none | `TryGetRequiredBufferSize(…, in RmQRCodeGeneratorOptions)` |

Total public static methods: Standard QR 12 to 10, Micro QR 10 to 8, rMQR 5 to 4.

**Why the parameter list `Try` is deleted rather than kept through the deprecation cycle.** Keeping it would give a 1.2.0 caller a minimal-diff escape from the obsolete `Get` without constructing an options value, which is a genuine migration convenience. It loses to one fact: **shipping it in 1.2.0 freezes it permanently.** Phase 0's rule bumps the validation baseline to 1.2.0 on release, so a parameter list `Try` released now could not be removed in 2.0.0 alongside the `Get` it was introduced to help people leave — it would outlive its own purpose and reach 3.0.0. Adding a permanent member to the family being retired, in order to ease retiring it, is not a trade worth making for a diff that is two tokens wider.

**Why `CreateQrCode` and friends keep throwing, and this is not the first half of a larger change.** Sizing is the pre-check that lets encoding treat "does not fit" as a defect: a caller who has asked `Try` and got `true` cannot then walk into the encoder's capacity throw. `Try`-sizing paired with throwing-encode is the coherent design, not a half-migrated one. Adding `TryCreate*` is explicitly **not** in scope and is not implied by this phase.

**What this costs, recorded before it is paid.**

- `TryGetRequiredBufferSizeTest.RmQR_Agrees_WithThrowingOverload` and `RmQR_WithEci_Agrees_WithThrowingOverload` lose their oracle, because the throwing overload they compare against is deleted. Standard QR and Micro QR keep theirs, since the obsolete released `Get` remains through 2.0.0, so the property itself keeps a home. The rMQR cases are rewritten against the invariant that actually matters and that the pair was a proxy for: **the size and version reported by `Try` are the size and version an encode with the same options actually produces.** That is a stronger statement than agreement between two sizing methods, and it does not depend on a throwing twin existing.
- The two surviving agreement tests now call an obsolete member on purpose. `#pragma warning disable CS0618` is correct there and only there: pinning that a deprecated member still behaves identically to its replacement is exactly what a deprecation cycle needs tested, and it is the test project, not `src/`.

**Verified while planning, not assumed.**

- **`[Obsolete]` is not a breaking change to package validation.** Probed by attaching `[Obsolete("PROBE")]` to the released `QRCodeGenerator.GetRequiredBufferSize` and packing against the 1.1.1 baseline: `APICompat ran successfully without finding any breaking changes`, with the `RunPackageValidation` target confirmed to have actually executed rather than being skipped by the semaphore. Reverted.
- **`TreatWarningsAsErrors` is not set anywhere in the build**, so CS0618 at the in-repo call sites will not break the build while they are being migrated. They are migrated regardless; the point is that the migration does not have to be atomic.

Exit criteria: the sizing surface is exactly the table above; full suite green on net8.0 and net10.0; package validation silent against 1.1.1; no `#pragma warning disable CS0618` anywhere under `src/`; docs and specs updated in the same phase rather than deferred, because Phase 5's own lesson was that documentation is code that has not been compiled.

## Risks

- **Passing options by `in` through the encode path could cost time.** The generators currently take scalars that the JIT keeps in registers through several inlined layers; a struct behind an `in` reference is not automatically free. This is the one performance-relevant risk in the plan and the reason Phase 1 and Phase 2 each carry a benchmark gate rather than deferring measurement to Phase 5. If a regression appears, the likely fix is passing the struct by value (it is small) rather than reverting the shape.
- **The quiet-zone sentinel is the kind of detail that survives review and fails in production.** A test that constructs `default(T)` and asserts a quiet zone of 4, for each of the three structs, is mandatory in the phase that introduces them.
- **Phase 1 is a large mechanical diff, and the one phase no tool checks.** Package validation is silent on rMQR, so the source diff is the only review it gets. Mixing any behaviour change into it makes that diff unreadable. If something needs fixing in rMQR encoding, it is a separate commit before or after, never inside.

## Working Rules

- Test-first is mandatory for every `src/` change: failing test, then implementation, then the full suite on net8.0 and net10.0.
- Package validation runs in CI on every pull request. A phase that touches the public surface of Standard QR or Micro QR must keep it green **without** a suppression file. If a suppression looks necessary, that is a breaking change and a decision to escalate, not a file to generate.
- Phase 1 and Phase 2 are behaviour-preserving by definition. A changed test expectation in either is a defect to investigate, not a baseline to update.
- Progress logging (mandatory): when a phase completes, append an entry to the Progress log below recording what was done, lessons learned, and benchmark deltas, or an explicit statement of why benchmarks are not applicable.

## Decisions taken, 2026-08-28

The three questions this plan opened with are settled. Recorded here so a later reader does not reopen them.

1. **Phases 0 through 5 gate the 1.2.0 release**, not Phase 1 alone. Shipping a partial reshaping would put two different API shapes in one release.
2. **Obsoleting the released Standard QR / Micro QR overloads is revisited after Phase 5**, with the intent to move callers onto the options struct. Not decided now, and nothing in Phases 0-5 depends on the answer.

   **Answered, 2026-08-29, in Phase 6, and only in part.** The two released *sizing* methods are obsoleted with removal scheduled for 2.0.0, because Phase 6 concluded a throwing sizing API is the wrong shape rather than merely the older one. The released `CreateQrCode` / `CreateMicroQRCode` parameter lists are **not** obsoleted and the question stays open for them: nothing has been found wrong with their shape, only with their extensibility, and freezing them was already the answer to that.
3. **ECC boosting and Standard QR mixed-mode segmentation stay out**, and do not become Phases 6 and 7 of this plan. Both change generated output and need oracle verification this plan has none of; the options struct is what makes them cheap afterwards.
4. **The naming question is settled in favour of `{consuming generator}Options`**, with the reasoning recorded under Guiding Decisions. The related question it raised, whether the options type needs to vary by symbology at all, is answered by the option-set matrix in the same section: it does, and a shared type is not expressible.

One decision remains open, deliberately: whether the released `Create*` parameter lists are ever obsoleted (item 2 above). New ones discovered during implementation are appended to the Progress log entry for the phase that found them.

## Progress log


### Phase 0, SDK package validation, completed 2026-08-28

`EnablePackageValidation` plus `PackageValidationBaselineVersion` = 1.1.1 on the library project, and a `dotnet pack` step in the build workflow scoped to one runner. **No production code changed**, so benchmarks are not applicable. Full solution suite unaffected and green.

**PublicApiGenerator was implemented first and then removed.** An approval-test project was built, working, and produced a 518-line committed baseline; it was replaced on a maintainer decision to keep the API contract on first-party SDK tooling rather than a third-party reflection-based test. The replacement is not equivalent, and the difference is worth recording rather than glossing:

- **Package validation is the stronger guarantee where the two overlap.** It compares against the artifact actually published to NuGet, not against a file in this repository that any contributor can regenerate. An approval baseline can be rubber-stamped in the same commit that breaks it; a published package cannot.
- **It also checks something the approval test never did.** Compatible-framework validation asserts that a consumer compiled against one framework asset works against another. The approval test only observed that the four surfaces happened to be textually identical, which is a weaker and different statement.
- **It gives up the reviewable listing.** Additions are not breaking, so the whole point of Phases 2 to 4 passes in silence, and Phase 1 is invisible to it because no released package contains rMQR. Those phases are now reviewed as ordinary source diffs. That was accepted knowingly: the deletion is deliberate and legible, and the accident worth machine-checking is breaking a released overload.

**Verified, not assumed, that it catches a break.** Turning `QRCodeGenerator.GetRequiredBufferSize` from `public` to `internal` and packing produced `CP0002` for all four framework assets, naming the exact member and both sides of the comparison, and failed the build. Reverted. A validation gate that has never been observed failing is not known to be a gate.

**Lessons learned**

- **Package validation runs on `Pack`, and `dotnet build` never invokes `Pack`.** Enabling the property alone would have left every pull request unvalidated while looking configured; only the release workflow, which packs, would have caught anything, at the worst possible moment. The build workflow needed an explicit pack step.
- **A passing local `dotnet pack` may not have validated anything.** The target is incremental against `obj/Release/Microsoft.NET.ApiCompat.ValidatePackage.semaphore`, and a second pack over the same output logs `Skipping target "RunPackageValidation" because all output files are up-to-date`. This was observed, not theorised. CI is safe because a fresh checkout has no semaphore, and the CI sequence (build, then `pack --no-build`) was confirmed to run the target. Locally, delete `src/SkiaSharp.QrCode/obj/Release` before trusting a green pack.
- The CI pack passes `-p:GenerateSBOM=false`. That step exists to validate the API, not to produce a shippable package, and SBOM generation adds seconds and a network dependency for license lookup to every pull request for no benefit. The release workflow still generates it.
- **`EciModeExtensions` is public with no public members**, found while reading the surface during the discarded approval work: every method on it is `internal`, and it has been that way since at least 1.1.1. Package validation will not report it, since it is not a break. Recorded here so it is not lost: it is dead public surface for the post-Phase-5 obsolete pass to absorb.
- **All four framework assets have an identical public surface today.** The `#if NET8_0_OR_GREATER` and `#if SIMD_SUPPORTED` blocks are all behind internal types. Worth knowing for Phase 3, where `QRCodeVersionRange` could be the first public type to need a `netstandard2.0` variation, and would be the first thing compatible-framework validation has to reason about.

### Phase 1, rMQR options struct and deletion of the legacy surface, completed 2026-08-28

`RmQRCodeGeneratorOptions` (`readonly record struct`) replaces the rMQR generator's parameter lists. The public surface went from **10 methods with up to 9 parameters to 5 methods with up to 4**, and `CreateRmQRCodeWithEci`, `GetRequiredBufferSizeWithEci` and `TryGetRequiredBufferSizeWithEci` are deleted outright: the ECI-explicit path is now `options with { EciMode = … }`. 201 call sites across 23 files (tests, samples, benchmarks, the Playground, the fixture tools and the rMQR image builder) were migrated. Full suite green on net8.0 and net10.0, 15,413 passed / 0 failed, with **no test expectation changed**.

**Quiet zone is stored as an offset from 2, not as a `value + 1` sentinel.** Both encodings make `default(T)` mean the ISO/IEC 23941 value while leaving 0 expressible, but only the offset gives the canonical form a unique field value: with `value + 1`, writing `QuietZoneSize = 2` explicitly produces a *different* field than not writing it, so the compiler-generated equality reports two behaviourally identical option sets as unequal. That would have been a latent trap the day someone used the struct as a dictionary key or asserted equality. `QuietZoneSize_WrittenAsItsDefaultValue_IsIndistinguishableFromUnset` pins it, including `GetHashCode`.

**The merge was safe for a reason that had to be checked, not assumed.** The old code had two encode paths: the no-ECI entry point dispatched on the *resolved* ECI and could take a faster writer, while the `*WithEci` entry point always took the general one. Unifying them means the explicit-ECI path now goes through that dispatch too, which would silently drop the ECI header if an explicitly requested `Iso8859_1` / `Utf8` could ever resolve back to `Default`. It cannot: `TextAnalyzer.Analyze` returns `requestedEciMode` verbatim whenever it is not `Default`, on all four of its code paths. The existing `Create_ExplicitEci_ChangesAsciiStream_AndLatin1RoundTrips` is what proves the behaviour did not change, since it asserts that explicit UTF-8 over ASCII produces a *different* stream from no ECI.

**Benchmark gate: passed, and the ECI paths got faster.** `RmQREncodeEndToEnd`, net10.0, `--warmupCount 5 --iterationCount 15`, baseline built from a `git worktree` at the pre-change commit and measured back to back.

| | Baseline | After | Delta |
|---|---:|---:|---:|
| `RmQR_Numeric_R7x43_Encode` | 144.8 ns | 147.9 ns | +2.1 % |
| `RmQR_Byte_R17x139_Encode` | 927.2 ns | 915.8 ns | −1.2 % |
| `RmQR_Latin1Eci_R17x139_Encode` | 928.6 ns | 918.5 ns | −1.1 % |
| `RmQR_Utf8Eci_R17x139_Encode` | 970.2 ns | 927.2 ns | −4.4 % |
| `RmQR_Numeric_AutoFit_Encode (Span)` | 195.6 ns | 182.4 ns | −6.7 % |
| `RmQR_UTF8_ECI_R17x139_Encode (Span)` | 993.2 ns | 939.4 ns | −5.4 % |
| `StandardQr_Numeric_V1_Encode (Span)` (control, untouched code) | 925.9 ns | 924.8 ns | −0.1 % |

Allocation is byte-identical on every row (112 / 160 / 368 B on the allocating paths, 0 B on the span paths). The one regression is +2.1 % against a ±6-8 ns error bar, so it is noise; the flat control row is what makes the rest of the table comparable. **The `in` parameter did not cost anything**, which was the plan's one performance risk. The options struct is constructed inside each benchmark method, so the measurement includes it and is the cost a caller actually pays. The improvement is small and not fully explained — the likely cause is that deleting the duplicated `*WithEci` bodies removed a near-copy of a large method — so it is recorded as measured, not as understood.

**Lessons learned**

- **The mechanical migration was scripted, and the script was applied to three files that had already been migrated by hand**, producing `new RmQRCodeGeneratorOptions { Version = new RmQRCodeGeneratorOptions { … } }`. Caught immediately, but the real lesson is the ordering: a call-site rewriter must run *before* any hand-editing of the same call sites, or its input set has to exclude them explicitly. The damage was limited to two untracked files and one hunk because everything else was committed.
- **The type change is what made a 201-site rewrite safe.** The destination-buffer overload keeps its `Span<byte>` in position 3, so a script that guessed wrong about whether the third argument was a version or a buffer produced a type error rather than a silently different symbol. Nine sites were misclassified (destination variables named `modules`, `pristine`, `sized`, `undersized`) and the compiler named every one. A rewrite of this shape against an untyped parameter list would have had no such net.
- **One test's name outlived its contract.** `ExistingDefaultLiteralPosition_RemainsSourceCompatible` existed to pin that a positional `default` in the `requestedVersion` slot compiles. That parameter no longer exists, and the mechanical rewrite left the test passing while asserting nothing about its subject. It was rewritten as `DefaultLiteralInTheOptionsSlot_ResolvesAndMeansAllDefaults`, which pins the genuinely subtle successor property: `default` there must bind to the options struct rather than to the `Span<byte>` destination overload. A test that survives an API change unchanged is not automatically still testing something.
- **`dotnet format --verify-no-changes` fails on six files for CRLF line endings, and four of them are untouched by this phase** (`QRCodeGenerator.cs`, `MicroQRCodeGenerator.cs`, `RmQRSegmentPlanner.cs`, `RmQRVersionSelector.cs`, `CapacityOverflowGuardTest.cs`). Pre-existing debt that arrived with the `TryGetRequiredBufferSize` work, deliberately **not** fixed here so the Phase 1 diff stays reviewable. It is a separate cleanup commit.
- **`specs/rmqr-encoder.md` now transcribes deleted signatures** and is wrong until Phase 5 rewrites it. Expected and scheduled, but it is stale in the repository in the meantime.

### Phase 2, Standard QR and Micro QR options structs, completed 2026-08-28

`QRCodeGeneratorOptions` (`EciMode`, `Utf8BOM`, `Version`, `QuietZoneSize`) and `MicroQRCodeGeneratorOptions` (`Version`, `QuietZoneSize`), plus one options overload per operation: six on Standard QR, five on Micro QR. Purely additive, **202 insertions and one deletion** across the two generators, and that deletion is a `private` → `internal` on Micro QR's quiet-zone constant so the options struct can share it. Suite green on net8.0 and net10.0, 15,573 passed / 0 failed in Release. Package validation against the 1.1.1 baseline stays silent, which is the phase's real gate: the released surface is untouched.

**No existing test file changed.** That was the phase's stated defect signal and it held: `git status` shows two modified source files, one modified benchmark file and three new files, nothing else.

**`options` deliberately has no default value here, unlike rMQR.** rMQR could take `in RmQRCodeGeneratorOptions options = default` because its parameter list overloads were deleted. Here they remain, so a defaulted options parameter would make `CreateQrCode(text, ecc)` ambiguous between the two. The asymmetry is intentional and is now visible in the code: rMQR has one method per operation, Standard QR and Micro QR have two.

**`Version` is `int?` / `MicroQRVersion?` for now, and Phase 3 will change its type.** The plan's end state is `QRCodeVersionRange`, but the range's *semantics* are Phase 3's content and its proof is Phase 3's byte-for-byte sweeps. Shipping `int Version` with a `-1` sentinel would have meant inventing a second sentinel encoding that Phase 3 then deletes; `int?` needs none, since `null` is already the natural "automatic" and matches `MicroQRVersion?` and `RmQRVersion?`. The type change in Phase 3 is free because nothing here is released. Note that `-1` is **not** a second spelling of automatic in the options type: `null` is the only one, and `Version = -1` throws.

**The options overloads unpack onto the parameter list overloads, not the other way round.** The plan wrote it as "reimplement the released overloads as thin forwarders", and the opposite direction turned out to be the right one: forwarding legacy → options would move the argument validation into a method with no `requestedVersion` or `quietZoneSize` parameter, so the released overloads' `ArgumentOutOfRangeException` parameter names would have had to change. Keeping the released bodies exactly as they are makes "signatures, exceptions and messages unchanged" true by construction rather than by test. When the released overloads are eventually obsoleted, the bodies move then.

**Standard QR sizing gained version support, which is the one place this phase adds capability.** `GetRequiredBufferSize` and `TryGetRequiredBufferSize` never had a `requestedVersion` parameter, while the Micro QR and rMQR equivalents did. Once the options struct carries `Version`, ignoring it in the sizing overloads would have been a silent trap, so they honour it: an explicit version that cannot hold the content is `false` from `TryGetRequiredBufferSize` and an `ArgumentException` from `GetRequiredBufferSize`, matching the other two symbologies. The released sizing overloads are untouched and still have no version parameter.

**Benchmark gate: passed.** `QRCodeEncodeEndToEnd`, net10.0, baseline from a `git worktree` at the Phase 1 commit. Every released-path row is within ±5.5 % with identical allocation, and since those code paths are byte-identical the spread is noise. Three options-path benchmarks were added permanently, because "is the path we point callers at as fast as the one it replaces" stays a live question through the obsolete decision after Phase 5.

| | Parameter list | Options | Note |
|---|---:|---:|---|
| `QR_Numeric_V1_L_Encode` | 908.3 ns | 865.8 ns | same allocation (120 B) |
| `MicroQR_Numeric_M2_Encode (Span)` | 143.2 ns | 141.6 ns | 0 B both |
| `QR_Byte_V40_L_Encode (Span)` | 76.59 us | 74.91 us | at 40 iterations; see below |

**Lessons learned**

- **A 14 % gap between two benchmarks that run identical code was measurement, and re-measuring is what proved it.** At `--iterationCount 15` the V40-L span pair read 87.7 us against 75.0 us with non-overlapping error bars, which looks like signal. The options overload is a single-expression forwarder that resolves to exactly the call its twin makes, so no code difference could explain it. At `--iterationCount 40` the pair collapsed to 76.59 us against 74.91 us with overlapping intervals. **Non-overlapping confidence intervals at a low iteration count are not evidence**; this benchmark case is large enough (~76 us, mask evaluation dominated) to be layout sensitive, and its own allocating and span variants had shown the same spread in both earlier runs.
- **`CreateQrCode` with an explicit version too small for the content throws `ArgumentOutOfRangeException (Parameter 'length')` from an internal slice.** Measured, not assumed: probed before designing the sizing behaviour. It is a pre-existing defect, unrelated to this phase and deliberately not fixed in it, because fixing it changes the exception a released overload throws. It is why the new sizing overloads answer the fit question themselves rather than delegating: `TryGetRequiredBufferSize` returning `true` and letting the caller walk into that exception would be a lie. Worth its own commit.
- **Multi-line `perl -0pi` substitutions silently do nothing on CRLF files.** Two edits anchored on `\n` matched nothing and one line-based pass then over-applied, replacing the body of the helper it was supposed to call with a recursive call to itself. The compiler caught it. The test-first skill already records this trap; it cost a cycle anyway. Line-based `sed` is the safe tool on this checkout.
- **The CRLF format debt shrank from six files to four** as a side effect: editing `QRCodeGenerator.cs` and `MicroQRCodeGenerator.cs` normalised them to LF on disk. Git's `autocrlf` means the index was always LF, so this is invisible in the diff. `RmQRSegmentPlanner.cs`, `RmQRVersionSelector.cs`, `CapacityOverflowGuardTest.cs` and `TryGetRequiredBufferSizeTest.cs` still fail `dotnet format --verify-no-changes`.

### Phase 3, version ranges, completed 2026-08-28

`QRCodeVersionRange` (1-40) and `MicroQRVersionRange` (M1-M4), with `Any` / `Exactly` / `AtLeast` / `AtMost` / `Between`, replacing the nullable single version in both options structs. Suite green on net8.0 and net10.0, 15,687 passed / 0 failed in Release. Package validation silent, as it must be: nothing released changed.

**Bounds are stored normalised so the canonical form is unique.** `_min` holds 0 for "at 1" and `_max` holds 0 for "at 40", which makes `default(T)`, `Any` and `Between(1, 40)` one value rather than three that behave identically and compare unequal. Same class of trap as the quiet-zone offset in Phase 1, and the same reason: a `record struct`'s generated equality compares fields, so any non-canonical spelling of a value becomes a lie.

**The range resolves by scanning its own window, not by comparing against the overall minimum.** Phase 2's sizing code tested `minimumVersion > requestedVersion`, which silently assumes the fit predicate is monotone in the version. It is not obviously so: the character count indicator widens at versions 10 and 27, so a larger version costs more header bits. `TryGetVersionInRange` scans `[Min, Max]` instead and is correct either way. The assumption is now also a checked fact rather than a belief: `StandardQr_FitsIsMonotoneInVersion` sweeps 3 modes × 4 ECC levels × 3 ECI modes × 58 lengths × 40 versions and asserts no version ever stops fitting once one has, and it passes. Both the search and the test were worth having; the search is what makes the code not care.

**The default path costs nothing extra, and only constrained ranges pay.** `Any` short-circuits to the parameter list overloads' automatic marker before any analysis, so `QRCodeGeneratorOptions.Default` follows exactly the Phase 2 code path. A constrained range analyses the text once to resolve the version and the released overload it forwards to then analyses again. That second pass is the price of keeping the released bodies untouched, and it is paid only by calls that actually constrain the version.

**Micro QR needed a distinction Standard QR does not have.** M1-M4 differ in which modes and ECC levels they offer, not only in capacity, so a range can rule out every version for two different reasons. A range that offers the requested ECC level nowhere (`AtMost(M3)` with Q, which exists only on M4) is a contradiction no content could satisfy, so it throws, exactly as pinning such a version already did. A range whose versions cannot carry the mode the text requires (`AtMost(M2)` with Byte content) is an ordinary "does not fit" and returns `false`, because the text is what picks the mode. This is the rule the plan asked to have recorded, and `MicroQr_RangeWithNoValidEccCombination_Throws` and `MicroQr_RangeWhoseVersionsCannotCarryTheMode_IsNotAFit` pin both halves.

**`Exactly(n)` is stricter than the `requestedVersion` parameter it descends from, deliberately.** The parameter is a hard override that hands a too-small version to the encoder, which fails with `ArgumentOutOfRangeException (Parameter 'length')` from an internal slice (measured in Phase 2). `Exactly(n)` resolves through the same fit scan as every other range, so content that does not fit version *n* is reported as not fitting, with an actionable message. The released parameter keeps its old behaviour untouched; only the new API is stricter.

**Benchmark gate: passed, with one row that cannot certify anything.** `QRCodeEncodeEndToEnd`, net10.0, baseline from a `git worktree` at the Phase 2 commit. The small and medium rows are flat within a few percent, allocation is identical everywhere, and the version-selection refactor touches the released hot path (`TryGetVersion` is now a forwarder onto `TryGetVersionInRange`).

| | Baseline | After | Δ |
|---|---:|---:|---:|
| `QR_Numeric_V1_L_Encode` | 884.8 ns | 874.3 ns | −1.2 % |
| `QR_Alphanumeric_V1_M_Encode` | 859.9 ns | 880.7 ns | +2.4 % |
| `QR_Byte_Url_V6_M_Encode` | 2,120 ns | 2,128 ns | +0.4 % |
| `QR_Numeric_V1_L_Encode (Span)` | 936.5 ns | 939.0 ns | +0.3 % |
| `MicroQR_Numeric_M2_Encode (Span)` | 142.8 ns | 140.9 ns | −1.3 % |

`QR_Numeric_V1_L_Encode` is the decisive row for the refactor: at 874 ns, version selection is a far larger fraction of the total than it is at 76 us, so a cost in the scan would show there first. It is flat.

**Lessons learned**

- **`QR_Byte_V40_L_Encode (Span, options)` is not a trustworthy measurement, and raising the iteration count does not fix it.** Across four runs of *identical* code paths it read 78.4 us, 88.4 us, 101.4 us and 76.2 us, with reported error bars of ±1 to ±3 us. The variance is **between** runs, not within them, so a higher iteration count only makes each individual run more confidently wrong: the thing that varies (code and data layout, JIT decisions, machine state) is fixed for the duration of a run. Phase 2 recorded the same row producing a phantom 14 % gap and concluded "re-measure at higher iteration count"; that conclusion was half right. The correct rule is **repeat the run**, not lengthen it. Anything this benchmark says at ±15 % should be ignored, and the small-payload rows are what the encode gate actually rests on.
- **The apparent +29 % regression this produced was worth chasing rather than waving away.** It was investigated as a possible real regression, and what settled it was the baseline showing the same row at 88.4 us while the changed build showed 76.2 us: the changed build measured *faster* than the baseline on the row that supposedly regressed. A regression cannot do that.
- **Phase 2's `minimumVersion > requestedVersion` fit test was a latent assumption that this phase removed.** It happened to be correct, and the sweep now proves it, but it was written without checking. The replacement does not depend on it being true.
- **`EncodingMode` is internal, so it cannot appear in a public test method signature.** The monotonicity data source had to yield only public types and loop the modes inside the body. Worth knowing before designing a `MethodDataSource` around internal enums.

### Phase 4, builders, completed 2026-08-28

A range-taking `WithVersion` overload on `QRCodeImageBuilder` and `MicroQRCodeImageBuilder`, and both builders now assemble an options value in `ResolveSymbol` instead of passing positional arguments. `WithVersion` keeps its signature and becomes the pinned case: `-1` maps to `Any`, anything else to `Exactly`. The rMQR builder is unchanged, as planned. Suite green on net8.0 and net10.0, 15,711 passed / 0 failed in Release; package validation silent.

**The change is six lines of production code per builder**, and the diff deletions are exactly the private version field, its assignment and the generator call in each.

**`WithVersion(n)` with content that does not fit version *n* now throws a different exception, and that is a deliberate improvement.** The builder used to hand the version straight to the parameter list overload, which failed inside the encoder with `ArgumentOutOfRangeException (Parameter 'length')` from a span slice. Routing through the options overload checks the fit first, so it is now an `ArgumentException` naming the version, the ECC level and the mode. `ArgumentOutOfRangeException` derives from `ArgumentException`, so the change is only visible to a caller catching the derived type specifically — implausible for an exception that never named anything useful, but it is a behaviour change on a released API and belongs in the Phase 5 migration notes.

**The version constraint is one method name with two overloads, not two names.** `WithVersionRange` was written first and then folded into a `WithVersion(QRCodeVersionRange)` overload, on review. Three reasons, and the first is the strongest: this plan's own Guiding Decision says the range is a single concept with the fixed version as its degenerate case, so splitting it back into two method names contradicts the type that was built to unify it. Second, it disagreed with the options struct the builder wraps: `QRCodeGeneratorOptions` has one `Version` member of range type, not a `Version` plus a `VersionRange`. Third, `WithVersionRange(QRCodeVersionRange)` names the method after its parameter type, which the type already says. Nothing was released, so the correction was free.

**The API parity test was the right place for the rMQR asymmetry to surface, and it did.** `QrImageBuilderApiParityTest` fails the moment one builder grows a member the others lack, which is exactly what adding a range overload to two of three does. The fix is to declare the difference, not to work around it: a new `orderedVersionOnlySignatures` list records that rMQR has no version range because its 32 versions are not totally ordered, alongside the existing `standardOnlyMembers` and `rmqrOnlyMembers`. This is the one existing test file the phase touched, and touching it is the mechanism working as designed rather than a defect.

**C#'s `..` range syntax was considered for the version constraint and rejected.** `System.Range`'s end is exclusive, so `WithVersion(1..40)` would read as versions 1 through 40 and mean 1 through 39, with 40 reachable only by writing the non-existent version 41. `Index` is also zero-based, so `..10` is `Index(0)..Index(10)` against a 1-based version space, and `Range` carries no length so `^` cannot be resolved or validated at construction. Documenting the type as inclusive while it looks exactly like the language feature would be precisely the class of trap the canonical-form and sentinel work exists to remove. A second, independent blocker was measured rather than assumed: `System.Range` in a public signature fails to compile for netstandard2.0 with `CS0051`, because PolySharp emits the polyfill as `internal`; making it public would have the library export `System.Range` and collide with any consumer assembly doing the same. Note the BCL does not use `System.Range` for inclusive domain ranges either, `RangeAttribute(1, 40)` being its own precedent.

**What the terseness complaint actually wanted was fixed instead**, with a public inclusive constructor and an implicit conversion from a single version:

| Intent | Spelling |
|---|---|
| automatic | omit `Version` |
| pin one version | `Version = 15` |
| both bounds | `Version = new(10, 20)` |
| one bound | `Version = QRCodeVersionRange.AtLeast(10)` |

`AtLeast` / `AtMost` keep their factories because a constructor cannot express them without writing the 1 or the 40 out, which is exactly the knowledge the named form exists to hide. The factories are now one-liners over the constructor, so validation lives in one place.

**An optional version has to flow through without a branch, and that is what the sentinel was really buying.** The first cut of the terse spellings fixed verbosity and missed this: a caller whose version comes from configuration or a nullable field could write `Version = 15` but not "15 or nothing", so they were forced into

```csharp
var options = configured.HasValue
    ? new QRCodeGeneratorOptions { Version = configured.Value, EciMode = eci }
    : new QRCodeGeneratorOptions { EciMode = eci };
```

`requestedVersion: -1` never had that problem, because the sentinel let one expression carry both cases. **The fix is an implicit conversion from `int?`, not the sentinel back**: `null` is the language's own typed spelling of "no value", so `Version = configured` works with `configured` an `int?`, while `-1` keeps throwing. The optionality that made the sentinel useful is recovered; the magic number that made it dangerous is not. The same conversion makes `WithVersion(configured)` bind to the range overload, so the fluent chain stays one expression too.

A side effect worth knowing: `Version = null` now compiles even though the property is a non-nullable struct, and means automatic. It reads as what it does, and is pinned by a test so it is a decision rather than an accident.

**The implicit conversion deliberately rejects -1.** It is automatic only in the released `WithVersion(int)` builder method, which cannot change. Accepting it here would put the sentinel back into the vocabulary of the type built to remove it, make -1 an arbitrary exception among invalid ints (`0` and `-2` still throw), and — the reason that actually decided it — let a -1 arriving through a variable, a parsed config value or a defaulted field silently produce an automatically sized symbol where a pinned one was asked for. A wrong answer with no exception is worse than a throw. Before this change `Version = -1` did not compile at all; the implicit conversion trades that compile error for a construction-time throw on one value, in exchange for `Version = 15` on every valid one.

**Declaring the difference had to move from the member name to the whole signature.** The existing exclusion lists match on `" {name}("`, which was enough while the difference was a distinctly named method. With an overload, `WithVersion` itself is shared by all three builders and only `WithVersion(VERSIONRANGE)` is absent from rMQR, so a name-based exclusion would have hidden the shared overload too and stopped guarding it. The list now holds normalized signatures.

**Benchmarks: the existing image benchmark does not cover the path this phase changed.** `QRCodeImageEndToEnd` calls `QRCodeImageBuilder.GetPngBytes(QRCodeData, size)` with a symbol generated in `[GlobalSetup]`, so it takes the pre-built-data branch of `ResolveSymbol` and never reaches the generation call that was rewritten. It was run anyway (two runs each side) and is flat with **byte-identical allocation** on all four rows (5.44 / 20.44 / 19.44 / 41.91 KB), which does establish that rendering and its allocation are untouched.

| | Baseline (2 runs) | After (2 runs) |
|---|---|---|
| `Small_512px` | 5.083 / 5.180 ms | 4.831 / 4.753 ms |
| `Small_2048px` | 79.5 / 75.4 ms | 74.0 / 72.9 ms |
| `Large_512px` | 9.882 / 9.305 ms | 9.313 / 9.061 ms |
| `Large_2048px` | 86.8 / 76.7 ms | 76.3 / 74.9 ms |

The changed path costs one stack-allocated struct per image and swaps the parameter list overload for the options overload, which Phase 2 measured as equal at the generator level (874 ns against 823 ns on the ~900 ns case). Against a 5-80 ms image that is unmeasurable, so no benchmark was added for it: one would not resolve anything. **The coverage gap is real and worth recording**: no benchmark exercises `new QRCodeImageBuilder(content).ToByteArray()`, the generate-and-render path most callers actually use. That is a suite gap that predates this plan.

**Lessons learned**

- **A benchmark whose name matches the area is not automatically a benchmark of the change.** `QRCodeImageEndToEnd` looked like the obvious gate for a builder change and is not one, because its `[GlobalSetup]` pre-generates the symbol. It was only caught by reading the benchmark bodies after running them. Reading what a benchmark actually calls has to come before treating its numbers as a gate.
- **The failing test in this phase was the new test, and its data was wrong rather than the code.** `WithVersionExactly_EqualsWithVersionInt(1)` pinned version 1 for a 32-byte payload that needs version 3, so both spellings correctly threw and the assertion compared nothing. Rewritten to derive the smallest fitting version and sweep it plus both count-indicator bands. **A parameterised test that hardcodes a version has to be checked against the payload it is given.**

### Phase 5, specs, migration and measurement, completed 2026-08-28

Documentation caught up with what Phases 1-4 built, and the plan's release gate is now met. Suite green on net8.0 and net10.0, 15,729 passed / 0 failed in Release; package validation silent; `dotnet build` clean of warnings.

**Documents changed**

- `specs/rmqr-encoder.md`: the API section transcribed the deleted 12-method surface verbatim. Rewritten around `RmQRCodeGeneratorOptions`, with the `= default` asymmetry, the `default(T)` sentinel rule, and why rMQR has no version range.
- `specs/qrcode-symbologies.md`: the API direction rule now lives here once, since it is cross-cutting: options structs are where new options go, released parameter lists are frozen, `default(T)` must be the complete default configuration, options are not shared across symbologies, and a version range exists only where versions are totally ordered.
- `specs/standardqr-encoder.md`: version range semantics under version selection, including the two behaviours that differ from the `requestedVersion` parameter and are confined to the options overloads.
- `specs/microqr-spec-map.md`: the range row in the capacity table, recording the ECC-contradiction versus mode-exclusion split.
- `docs/migration.md`: new `generator options` and `rMQR` sections.
- Root `README.md`: a generator options section with version ranges, and every rMQR example moved off the deleted named arguments.

**A pre-existing documentation error was corrected, not just the stale signatures.** `docs/migration.md` told readers that adding rMQR's `segmentation` argument was "binary breaking: assemblies compiled against v1.1.1 or earlier must be recompiled". No assembly can be compiled against those methods, because rMQR does not exist in v1.1.1. The whole section described a migration from an API that never shipped; it is now an introduction to a new symbology.

**The README examples were executed, not just written.** A temporary test compiled every new snippet and asserted the versions the prose claims: `R7x43` for `MinimizeHeight`, `R11x77` single versus `R15x43` optimal, `R17x139` for the 200-character mixed payload that throws without segmentation, and the five `Version =` spellings resolving as documented. All passed. Documentation that claims a specific output is a testable claim and was treated as one.

**Benchmark gate: passed on the evidence that is interpretable, and one part of the run is not.** Baseline is a `git worktree` at the Phase 0 commit, so this measures the whole plan rather than one phase.

| | Baseline (Phase 0) | After (Phase 5) |
|---|---:|---:|
| `RmQR_Numeric_R7x43_Encode` | 157.7 ns | 142.6 ns |
| `RmQR_Byte_R17x139_Encode` | 930.3 ns | 898.6 ns |
| `RmQR_Latin1_ECI_R17x139_Encode (Span)` | 1,016.4 ns | 882.3 ns |
| `RmQR_Numeric_AutoFit_Encode (Span)` | 198.4 ns | 169.2 ns |
| `MicroQR_Numeric_M2_Encode` | 128.5 ns | 126.0 ns |
| `MicroQR_Byte_M4_Encode` | 163.0 ns | 164.8 ns |
| `QR_Numeric_V1_L_Encode` | 934.2 ns | 940.7 ns |
| `QR_Alphanumeric_V1_M_Encode (Span)` | 1,013.7 ns | 973.2 ns |

The rMQR and Micro QR runs were clean (error bars of ±0.3 to ±3 ns on the small rows) and every row is flat or faster, with **allocation byte-identical everywhere**. rMQR is where the largest reshaping happened and it got slightly faster, consistent with what Phase 1 measured.

**The Standard QR V40 rows are not interpretable from this session and are deliberately not quoted.** They read 100-133 us against an 84 us baseline across two runs, with error bars up to ±19,000 ns, while the small rows in the same runs stayed flat — a machine-wide slowdown, not a code effect. Nothing in Phase 5 touches encoding at all (documents and comments only), and Phase 3, which did touch version selection, measured `QR_Byte_V40_L_Encode` at 75.6 us against an 82.1 us baseline. The gate rests on the small and medium rows, which are stable, and on allocation being unchanged.

**Lessons learned**

- **Repeating the run is what separates noise from signal, again.** One Standard QR run showed `QR_Numeric_V1_L_Encode (options)` at 1,319 ns against 963 ns for the parameter list, which reads as a 37 % regression on the path callers are pointed at. The repeat put them at 914 ns and 941 ns, with the options row *faster*. This is the third time in this plan that a single run produced a plausible, wrong number; the rule recorded in Phase 3 held.
- **Documentation is code that has not been compiled.** Every rMQR example in the README used named arguments (`segmentation:`, `fitStrategy:`, `requestedVersion:`) that Phase 1 deleted, so they had been broken for four phases without anything noticing. Nothing in the build or test suite reads the README. Extracting the snippets into a throwaway test found them immediately, and would have found them in Phase 1.
- **A stale spec is not the worst case; a spec that is wrong about history is.** The migration document's claim about v1.1.1 was not merely out of date, it described a compatibility hazard that never existed, which would have sent a reader looking for a recompile they never needed.

### Phase 6, sizing becomes Try-first, completed 2026-08-29

`TryGetRequiredBufferSize` is now the only sizing method on the current surface of all three generators. Five unreleased overloads were deleted (the options `GetRequiredBufferSize` on each symbology, and the parameter list `TryGetRequiredBufferSize` on Standard QR and Micro QR), and the two released throwing methods are `[Obsolete]` with removal scheduled for 2.0.0. Public static method counts: **Standard QR 12 to 10, Micro QR 10 to 8, rMQR 5 to 4**. Suite green on net8.0 and net10.0, 15,751 passed / 0 failed in Release; package validation silent against the 1.1.1 baseline.

**This phase reversed a decision Phase 2 made, and the reversal came from the maintainer.** Phase 2 gave the options surface a `Get` / `Try` pair by analogy with `Parse` / `TryParse`. That analogy is wrong for this operation, and the argument that settled it is not a matter of taste: an exception costs microseconds while this library's own measured encode is 142-940 ns, so reporting a routine, data-dependent outcome that way costs one to two orders of magnitude more than the work it reports on. The BCL agrees where the operation is actually comparable — `Utf8Formatter.TryFormat`, `Utf8Parser.TryParse`, `IUtf8SpanFormattable.TryFormat` and `Base64.EncodeToUtf8` all size or format into a caller buffer and none has a throwing twin. **Recorded because the plan reached for the nearest-looking BCL precedent instead of the applicable one, and no phase gate would have caught that.**

**Deleting the parameter list `Try` was the decision that was not obvious, and it turned on the release calendar rather than on API taste.** Keeping it would have given callers a minimal-diff escape from the obsolete `Get` without building an options value. But Phase 0's own rule bumps the validation baseline to 1.2.0 on release, so shipping it would freeze it permanently: the member introduced to help people leave `GetRequiredBufferSize` would have outlived the thing it was helping them leave, surviving to 3.0.0 while its subject went in 2.0.0.

**Deleting it also bought back something Phase 2 had to give up.** With the parameter list `Try` gone, `TryGetRequiredBufferSize` has exactly one overload per generator, so its `options` parameter can take a default value without the ambiguity that forced Phase 2's asymmetry. `TryGetRequiredBufferSize(text, ecc, out var size)` is now the shortest correct call on all three symbologies, matching rMQR. The `Create` overloads still cannot do this, because their released parameter lists are still there — so the asymmetry Phase 2 documented now applies to `Create` only, and the specs say so.

**`[Obsolete]` is not a breaking change to package validation, verified rather than assumed.** Probed before designing anything by attaching `[Obsolete("PROBE")]` to the released `QRCodeGenerator.GetRequiredBufferSize` and packing against 1.1.1: `APICompat ran successfully without finding any breaking changes`, with the `RunPackageValidation` target confirmed to have actually run rather than being skipped by the semaphore (the Phase 0 trap). Reverted, then re-confirmed silent on the real change.

**The rMQR agreement tests lost their oracle and were rewritten into something stronger.** `RmQR_Agrees_WithThrowingOverload` compared `Try` against a throwing twin that no longer exists. The replacement, `RmQR_ReportedSize_MatchesTheEncodeItDescribes`, asserts the invariant the pair was only ever a proxy for: `true` means an encode with the same options fills exactly the reported buffer at the reported version, and `false` means that encode throws. It does not depend on a throwing twin existing, and it is a statement about the thing callers actually rely on. Standard QR and Micro QR keep their agreement tests, since the obsolete released `Get` is there to agree with until 2.0.0.

**Suppressions are confined to two places in the test project and none in `src/`.** The released bodies were left untouched and the options overloads stopped forwarding through them, so nothing in the library calls a deprecated member. In tests, `QRCodeGeneratorUnitTest` has a scoped `#pragma warning disable CS0618` around the section that *is* the released overload's regression suite, and `Sizing.ReleasedRequired` wraps the deprecated calls the parity tests need so the suppression does not spread across the suite.

**Lessons learned**

- **The scripted call-site rewrite over-applied twice, in the same way Phase 1 recorded and in a new way.** Rewriting `X.GetRequiredBufferSize(` to a helper was safe; stripping `, quietZoneSize: ` to make the arguments positional was not, because it also hit `Create*` calls, where the stripped argument silently rebound to `utf8BOM`. The compiler caught it (a `bool` parameter cannot take `0`), but only because the neighbouring parameter had a different type. **A mechanical edit that changes an argument from named to positional is not the same class of change as one that renames a method, and must be scoped to the call sites whose signature actually changed.**
- **A helper that wraps the API under test turns its own tests into tautologies.** `Sizing.Required` calls `TryGetRequiredBufferSize`, so the blanket rewrite turned `RmQR_Fits_ReturnsTrue_AndMatchesThrowingOverload` into an assertion that `Try` equals `Try`. Three tests were silently reduced to nothing and still passed. They now compare against the generated symbol instead. This is the Phase 1 lesson recurring with a new mechanism: **a test that keeps passing through an API change has not been verified to still test anything, and a rewrite that introduces a wrapper is exactly when to check.**
- **The README samples were compiled again, and again that was the only thing that could have checked them.** Phase 5 found four phases of rot this way and recorded the lesson; the mitigation is still manual, so a throwaway probe file was written, built, and deleted for the second time. Making it a permanent compile-only file is the obvious fix and is not in this phase's scope.
- **The pre-existing CRLF format debt is larger than Phase 2's four files** — `dotnet format --verify-no-changes` currently fails on 19 files, of which 18 were untouched here. It is invisible in `git diff` because `autocrlf` normalises the index, and it shrinks incidentally whenever a file is edited (every file this phase touched now passes). Still deliberately not fixed here, for the same reason Phase 1 gave: it would bury the reviewable diff.
- **Renaming a cross-reference is only safe when the two members are the same kind of thing, and code review is what caught that here.** The blanket rewrite turned `Must be at least <see cref="GetRequiredBufferSize"/> bytes` into `... <see cref="TryGetRequiredBufferSize"/> bytes` on two `destination` parameters. The original was already loose but readable, because `GetRequiredBufferSize` returns a size; the replacement returns `bool`, so the sentence became false. Both now name `QRCodeCalculatedSize.BufferSize` and match the form Micro QR and rMQR already used. **This is the third over-application of the same mechanical pass, and the first that no compiler could have caught** — the previous two were a type error and a tautological test. A rewrite that crosses from code into prose has no checker behind it.
- **A pre-existing imprecision next to changed code becomes a new one.** A benchmark comment said that with the version pinned, sizing "ignores the text". That was loose before and wrong to leave: `TryGetRequiredBufferSize` does analyse the content and can answer `false` if the pinned version cannot carry it. What is actually true is that the *reported size* depends only on the version and quiet zone. Reworded. Renaming the method on that line is what put the claim in front of a reviewer.

**Benchmark gate: allocation certifies it, timing this session does not, and the reason is documented rather than waved away.** `QRCodeEncodeEndToEnd`, `MicroQREncodeEndToend` and `RmQREncodeEndToEnd` on net10.0, baseline from a `git worktree` at the Phase 5 commit, measured back to back.

- **Allocation is byte-identical on all 34 rows** across the three symbologies: 120 / 120 / 280 / 3984 / 3808 B on Standard QR, 88 / 96 / 104 B on Micro QR, 112 / 160 / 368 B on rMQR, and 0 B on every span row.
- **Timing is not quotable, and the control rows are what prove it.** `MicroQREncodeEndToend` contains no code this phase touched at all, yet every row in it moved uniformly slower — +14.1 %, +16.2 %, +12.2 %, +11.8 %, +7.5 %, +2.1 % — and its `StandardQr_Numeric_V1_Encode (Span)` control went 943.1 → 1,067.9 ns. A phase that changed nothing in Micro QR's encode path cannot have made it 14 % slower. The baseline is also far slower than earlier phases measured on the same machine (`QR_Byte_V40_L_Encode` at 112 us against Phase 3's 75.6 us), so the machine, not the code, is what moved. This is the third recurrence of the pattern Phase 3 first recorded.

**Benchmarks are the wrong instrument for this phase anyway, and that was checked rather than assumed.** Every `Sizing.Required` call in the encode benchmarks is inside `[GlobalSetup]`, so **no `[Benchmark]` method executes the sizing code this phase changed**. The `Create*` paths are byte-identical source, the released `Get` bodies were left untouched, and the only runtime differences are a removed `IsAny` short-circuit in Standard QR's options `Try` (which forwarded to `TryGetVersion`, itself a forwarder onto the `TryGetVersionInRange(1, 40)` now called directly) and a Micro QR body moved verbatim from a public method to a private one. The allocation parity above is what the gate actually rests on.
