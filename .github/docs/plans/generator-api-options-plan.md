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
- SDK package validation against the last released package, as the mechanism that keeps the released contract intact while the surface above it is reshaped.
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
3. **ECC boosting and Standard QR mixed-mode segmentation stay out**, and do not become Phases 6 and 7 of this plan. Both change generated output and need oracle verification this plan has none of; the options struct is what makes them cheap afterwards.
4. **The naming question is settled in favour of `{consuming generator}Options`**, with the reasoning recorded under Guiding Decisions. The related question it raised, whether the options type needs to vary by symbology at all, is answered by the option-set matrix in the same section: it does, and a shared type is not expressible.

No open decisions remain. New ones discovered during implementation are appended to the Progress log entry for the phase that found them.

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
