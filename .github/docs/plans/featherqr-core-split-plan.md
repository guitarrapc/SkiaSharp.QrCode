# Core Split: FeatherQR, FeatherQR.SkiaSharp and the SkiaSharp.QrCode metapackage

## Purpose

This document defines how the single `SkiaSharp.QrCode` package becomes a dependency-free core package plus a SkiaSharp rendering package, inside one repository, without breaking the install line behind four million downloads. It records the decisions taken in the 2026-09-04 design session so that the implementation does not reopen them, and it fixes the order of the work. The research behind the decisions lives in [references/](references/) and is deleted with this plan.

It defines WHAT is built, in WHICH order, and WHY. HOW each piece is verified follows the mandatory test-first workflow. The 2.0.0 type renames (the `QR` casing rule, `ECCLevel` to `QREccLevel` and friends) are a separate workstream that lands in the same major after this plan; they are referenced, not planned, here.

## The problem

Issue [#370](https://github.com/guitarrapc/SkiaSharp.QrCode/issues/370) names the two strengths of the library, SkiaSharp integration and speed with low memory use, and the contradiction between them: anyone who wants only the QR code modules still pays for SkiaSharp. The cost is not the managed assembly. Measured on 2026-09-04 with `PublishTrimmed` on a console app that only calls `QRCodeGenerator.CreateQrCode`:

| What ships | Size |
|---|---:|
| `SkiaSharp.QrCode.dll`, trimmed to QR encode only | 95 KB |
| `SkiaSharp.dll`, trimmed | removed entirely |
| `libSkiaSharp.dll`, native, one RID | 11.9 MB |

The native payload cannot be trimmed away, it is deployed per RID whether or not a single SkiaSharp call survives, and it is the part that needs CVE tracking. The dependency graph is the problem, and only a package boundary fixes a dependency graph.

The core has been ready for this for some time: the only Skia-typed code outside the rendering layer is `TryDecode(SKBitmap)` on the three decoders and the `SKBitmap` entry of `LuminanceConverter`. The library has zero `using SkiaSharp;` lines only because the root namespace `SkiaSharp.QrCode` resolves `SK*` types implicitly; moving the core to another namespace surfaces every one of those sites. The file-level inventory is in [references/featherqr-split-inventory.md](references/featherqr-split-inventory.md).

## Decisions

### Three packages, one brand, one metapackage

| Package | Contents | Dependencies |
|---|---|---|
| `FeatherQR` | Generators, decoders, data types, options, segmentation, `ModuleRect` geometry, luminance conversion kernels, the future pure-BCL outputs (SVG string, 1-bit PNG) | none |
| `FeatherQR.SkiaSharp` | `QRCodeRenderer`, the `SKCanvas` extensions, the image builders, `IconData` and shapes, `SKBitmap` decoding | `FeatherQR`, `SkiaSharp` |
| `SkiaSharp.QrCode` | Nothing. An empty metapackage whose only content is a dependency on `FeatherQR.SkiaSharp` | `FeatherQR.SkiaSharp` |

NuGet has no package redirect. The three mechanisms that exist are keeping an ID, deprecating an ID with an alternate (one-way, for retiring), and an empty metapackage. The metapackage is the one that keeps `<PackageReference Include="SkiaSharp.QrCode" />` compiling after the upgrade, which is the whole point: `SkiaSharp.QrCode` is the ideal name for "SkiaSharp plus QR codes" and it stays the name people install by. New documentation leads with `FeatherQR.SkiaSharp`; the metapackage is the compatibility door, not the front door.

### Why the core is not `SkiaSharp.QrCode.Core`

A dependency-free package must not carry the name of the dependency it is free of. The name would lie to exactly the audience the split is for, and it would fail at the second non-Skia renderer (`SkiaSharp.QrCode.ImageSharp` is nonsense). This was the user's own conclusion before the session and it was reconfirmed. The `SkiaSharp.*` prefix is reserved on nuget.org but public, so the ID was pushable; it was rejected on meaning, not mechanics.

### Why the core is not split per symbology

Standard QR, Micro QR and rMQR keep separate types (`QRCodeGenerator`, `MicroQRCodeGenerator`, `RmQRCodeGenerator`) because their option sets and constraints are different, and that type separation is exactly what makes trimming precise: the entry points do not reference each other, so a trimmed app that uses one symbology keeps one symbology. Measured on the current assembly with `TrimMode=full` (scripts and the full breakdown in [references/featherqr-size-measurements.md](references/featherqr-size-measurements.md)):

| Consumer profile | Trimmed `SkiaSharp.QrCode.dll` |
|---|---:|
| QR encode only | 95 KB |
| All three symbologies, encode | 153 KB |
| All three, encode and decode, including `TryDecode(SKBitmap)` | 229 KB |
| Untrimmed net10.0 | 384 KB |

A per-symbology package split would save untrimmed consumers roughly 100 to 130 KB of the 384 KB and cost a four-package core (shared kernels plus three), a public surface for the shared kernels, and a renderer that either references all three cores or splits into three renderers. ZXing.Net, libzint and CodeGlyphX all ship one core and split at the leaf. So does this plan. A unified `Create(symbology, ...)` entry point must never be added: it would root all three symbologies and undo the trimming result above.

### The brand is `FeatherQR`

"Feather" is the established .NET word for lightweight (`FeatherHttp`, `Feather.Blazor`, `DotFeather`), and lightweight is the title of issue #370. The compound is free on nuget.org, npm, crates.io, PyPI and GitHub, `featherqr.com` and `featherqr.app` were unregistered on 2026-09-04, and no product carries the name. The short version of why the alternatives lost is that every speed word was either a benchmark claim (`FastQR`, `SpeedQR`), a live product (`ZeroQR`, `RapidQR`, `QuickResponse`), a reduced-feature connotation (`LiteQR`), a language (`SwiftQR`), a fictional symbology next to Micro QR (`NanoQR`, `PicoQR`), or a .NET-only term (`SpanQR`).

**What the name is allowed to mean.** Lightweight here is zero dependencies, zero allocations on the hot paths, and trim and AOT safety. It is not assembly bytes: at 300 to 390 KB the assembly is larger than QrCodeGenerator (144 KB) and QRCoder (195 KB), because it carries three symbologies, decoders and SIMD paths. The README states this definition in its first lines so the name never has to be defended against a byte count.

### Namespaces follow the packages

`FeatherQR` for everything in the core, `FeatherQR.SkiaSharp` for everything in the rendering package. The `SkiaSharp.QrCode.Image` sub-namespace is folded into `FeatherQR.SkiaSharp`; 2.0.0 already changes every `using` line, so one namespace per package is the cheapest shape to explain. The root namespace `SkiaSharp.QrCode` disappears from the assemblies. Type forwarding is not used: the same major renames types, so a forward would preserve nothing anyone can compile against.

One trap is designed around rather than discovered: inside `namespace FeatherQR.SkiaSharp`, the simple name `SkiaSharp` binds to that namespace, not to `global::SkiaSharp`. `using SkiaSharp;` must sit above the file-scoped namespace declaration (where it binds globally), never inside a namespace block, and qualified `SkiaSharp.SKBitmap` spellings are not used in the rendering package. Consumers are unaffected: a `using FeatherQR;` imports types, not nested namespaces, so their `SkiaSharp.*` names still resolve. `ZXing.SkiaSharp` is the working precedent for this shape.

### `TryDecode(SKBitmap)` becomes an extension member, with a named fallback

Static methods cannot be added to a type from another assembly, and every renderer package will have the same need (`Image<Rgba32>`, a byte span with a pixel format). C# 14 extension blocks solve it once. Verified empirically on 2026-09-04 with a netstandard2.0 library and two consumers:

```csharp
// FeatherQR.SkiaSharp
public static class QRCodeImageDecoder
{
    extension(QRCodeDecoder)
    {
        public static bool TryDecode(SKBitmap bitmap, out string text) { ... }
    }
}
```

| Consumer compiler | Available call |
|---|---|
| C# 14 | `QRCodeDecoder.TryDecode(bitmap, out text)` and `QRCodeImageDecoder.TryDecode(bitmap, out text)` |
| C# 12 and 13 | `QRCodeImageDecoder.TryDecode(bitmap, out text)` |

The implementation methods are emitted as ordinary public static methods on the enclosing class, so older compilers see them by that name. One source, no `#if`. The consumer's default language version follows its target framework (net8.0 is C# 12), so most users will see the enclosing-class form for years: the README shows that form first. Overloads from several renderer packages coexist because they differ in parameter type.

### The core keeps luminance conversion internal (revised in Phase 2)

`TryDecodeImage(ReadOnlySpan<byte> luminance, int width, int height, ...)` is already public on all three decoders and is the decode seam for any image library. The pixel-to-luminance kernels (Gray8, BGRA, RGBA, RGB888x, premultiplied or not, AVX2 and AdvSimd tiers) are Skia-independent and stay in the core behind one internal entry, `LuminanceConverter.Convert(pixels, width, height, rowBytes, PixelLayout, premultipliedAlpha, luminance)`, which the first-party rendering package reaches through `InternalsVisibleTo`. The original plan made that entry public so that a third-party adapter would get the SIMD tiers and the white compositing for free; in Phase 2 the maintainer decided against growing the public surface ahead of a concrete request, since an API can be added in a minor release and only removed in a major one. A third-party decoder adapter therefore produces luminance itself (the `TryDecodeImage` documentation states the white-compositing rule) and a third-party renderer draws from `GetModuleRectangles`. `InternalsVisibleTo` from `FeatherQR` to `FeatherQR.SkiaSharp` is permanent for the first-party packages; every use is enumerated in the architecture record with a public replacement or a recorded reason.

### Lockstep versions, one tag, one release run

`Directory.Build.props` keeps its single `<Version>`. All three packages ship at the same version from the same tag, and the `ProjectReference` from the rendering package to the core becomes a package dependency at that version automatically on pack. This is what the existing release workflow already does with a glob over `./nuget/*.nupkg`; nothing about the release mechanics changes except the count.

### The repository is renamed to `FeatherQR`, and the Playground URL is handled explicitly

The brand is the repository. GitHub redirects the web URL and every `git` operation from the old name indefinitely, as long as the old name is never reused. GitHub Pages project sites are the documented exception: `https://guitarrapc.github.io/SkiaSharp.QrCode/` stops resolving the moment the repository is renamed, and it is linked from the README of every 1.x package on nuget.org, which cannot be edited.

The fix is the user site. `guitarrapc/guitarrapc.github.io` exists and has Pages enabled, and a project path with no project behind it falls through to the user site, so a `SkiaSharp.QrCode/index.html` there with a meta refresh to the new Playground URL keeps the old links alive without reusing the repository name. This is the standard workaround and it is the only one that does not break the repository redirect.

Two more things are bound to the repository name and must be updated in the same window: the nuget.org Trusted Publishing policy that `NuGet/login` uses (it is configured by owner and repository name), and the CodeQL / Pages / environment settings, which move with the rename but are verified after it.

### Order: split first, renames second, preview releases in between

The split is structural and touches every file; the type renames are textual and touch every consumer. Doing them in one change would produce a diff nobody can review. The split lands, is released as `2.0.0-preview.1` to claim the three IDs on nuget.org and to exercise the pipeline, the renames follow as their own plan, and `2.0.0` ships when both are in. The tag pattern in `release.yaml` already matches `2.0.0-preview.1`; `bump_version.cs` does not produce preview versions, so the preview bump is a manual edit and is documented as such.

## Scope

**In scope.**

- Two new library projects in `src/`, the metapackage project, and the namespace move.
- The public seam: `QRCodeImageDecoder`, the luminance API, the `InternalsVisibleTo` inventory.
- Every CI gate and tool adapted to two assemblies and three packages: the approved API listings, XML documentation trimming, the API page, the AOT analysis gate, package validation baselines, and a new per-TFM nupkg dependency assertion.
- Renaming the ancillary projects (tests, benchmark, playground, AOT gate, solution) and the paths in skills and tools.
- README, `docs/migration.md`, `DESIGN.md`, the architecture record, and the Playground page text.
- The repository rename, the Pages fallback stub, and the Trusted Publishing policy update.

**Out of scope.**

- The 2.0.0 type renames (rule B) and the removals scheduled for 2.0.0 (`Compression`, `GetRequiredBufferSize`, the parameter-list generator overloads). They have their own plan and land after this one.
- New renderer packages. The split makes them possible; none is built here.
- The pure-BCL SVG writer and the 1-bit PNG writer. They belong in the core and are follow-ups.
- Any change to encoding or decoding behavior. Every existing test passes unchanged in meaning; only namespaces and project references move.

## Phases

Each phase is one pull request unless stated otherwise, and each ends with a Progress log entry (what was done, lessons learned, benchmark delta or the reason none applies).

### Phase 1: Split the projects

**Goal.** Two library projects build from the existing sources with the existing tests green, and the core assembly provably has no SkiaSharp reference.

- Create `src/FeatherQR/FeatherQR.csproj` (`PackageId` `FeatherQR`, root namespace `FeatherQR`, the four target frameworks, PolySharp private, no SkiaSharp) and `src/FeatherQR.SkiaSharp/FeatherQR.SkiaSharp.csproj` (root namespace `FeatherQR.SkiaSharp`, `ProjectReference` to the core, `PackageReference` SkiaSharp). Move files with `git mv` so history follows them: everything under `Image/`, `QRCodeRenderer.cs`, `QRCodeExtensions.cs` and the `SKBitmap` overloads go to the rendering project; everything else goes to the core.
- Change every namespace declaration; add `using SkiaSharp;` above the namespace in every rendering file (this is the inventory of implicit Skia resolution mentioned above).
- `InternalsVisibleTo` from the core to the rendering assembly and to the test assembly, with the existing public key.
- The test project references both assemblies; test namespaces move with the code. It stays one assembly.
- Add one test that asserts, from metadata, that the core assembly's references contain no `SkiaSharp` for every target framework. This is the split's regression guard for the rest of the library's life.
- Samples, Benchmark, Playground and the interop fixtures reference `FeatherQR.SkiaSharp`, as a consumer would.

**Exit.** `dotnet build` and `dotnet test` green on the full solution; the no-SkiaSharp assertion passes; Playground publishes.

### Phase 2: The public seam

**Goal.** Everything the rendering package needs from the core is either public or listed.

- `QRCodeImageDecoder`, `MicroQRCodeImageDecoder`, `RmQRCodeImageDecoder` in the rendering package, each an extension block on its decoder, with the enclosing-class names chosen as the documented fallback spelling. Tests cover both call forms (the test project compiles with C# 14; the fallback form is exercised by name).
- The luminance conversion entry in the core, one internal span-based, allocation-free method behind `InternalsVisibleTo` (revised from "public", see the decision above), with the parity tests and a contract test for it.
- Enumerate every remaining `InternalsVisibleTo` use from the rendering package (expected: the module-run enumerator behind `QRCodeRenderer.DrawModuleRuns`). For each, decide public replacement or recorded reason, and write the result into the architecture record.
- Two approved API listings, `src/FeatherQR/PublicAPI.approved.txt` and `src/FeatherQR.SkiaSharp/PublicAPI.approved.txt`, generated by `tools/check_public_api.cs`, which already takes one listing and any number of assemblies and is simply run twice.

**Exit.** Both listings accepted in review; a consumer sample (the existing `SimpleGenerate` is enough) compiles against the public surface with `InternalsVisibleTo` removed from the build locally, proving a third-party renderer could be written.

### Phase 3: Packaging and gates

**Goal.** Three packages pack from one `dotnet pack`, every gate covers both assemblies, and `2.0.0-preview.1` is on nuget.org.

- `src/SkiaSharp.QrCode/SkiaSharp.QrCode.csproj` becomes the metapackage: `IncludeBuildOutput` false, no compile items, a `ProjectReference` to the rendering project so the dependency is generated at the lockstep version, `NU5128` suppressed, package validation disabled (there is no assembly to compare), README and description explaining what it is.
- Package validation: the rendering package and the core have no released baseline, so `PackageValidationBaselineVersion` is unset on both until `2.0.0` ships, then set to `2.0.0`. The old `CompatibilitySuppressions.xml` is deleted with the old project.
- A new `tools/check_package_deps.cs`: opens each nupkg, reads the nuspec dependency groups per target framework, and asserts the exact expectation as measured in Phase 1: the core has empty net8.0 and net10.0 groups, `System.Memory` (plus the `System.Runtime.CompilerServices.Unsafe` it pins transitively) on netstandard2.0 and `System.Runtime.CompilerServices.Unsafe` on netstandard2.1, and nothing else; the rendering package has `FeatherQR`, `SkiaSharp` and, because `CentralPackageTransitivePinningEnabled` promotes pinned transitive packages into the nuspec exactly as it did for 1.x, `SkiaSharp.NativeAssets.Win32` / `.macOS` (and the netstandard shims above), and nothing else; the metapackage has `FeatherQR.SkiaSharp` plus the same promoted set and no `lib/`. Whether to keep the promotion or switch pinning off for the three packable projects is decided here, before the assertion is written, and recorded in the architecture record. This closes the open item from the CodeGlyphX evaluation and is the second regression guard for the split, at the artifact level rather than the assembly level.
- `build.yaml`: XML documentation trimming and the approved-API check run over both assemblies; the pack step packs the solution; the dependency assertion runs after it. `release.yaml`: the same, and the push glob already covers three packages.
- `tests/SkiaSharp.QrCode.AotAnalysis`: `TrimmerRootAssembly` for both `FeatherQR` and `FeatherQR.SkiaSharp`.
- `tools/public_api.cs`: enumerate both assemblies into one page.
- Manual edit of `<Version>` to `2.0.0-preview.1`, tag, release run. The first push is also the definitive check that the three IDs are accepted; `dotnet nuget push` fails cleanly if not.

**Exit.** The three packages are visible on nuget.org at `2.0.0-preview.1`, a fresh console app that references only `FeatherQR` restores with zero transitive packages, and one that references `SkiaSharp.QrCode` restores `FeatherQR.SkiaSharp`, `FeatherQR` and `SkiaSharp`.

### Phase 4: Rename the ancillary projects and paths

**Goal.** Nothing in the repository is still named after the old package except the metapackage and the migration text.

- `git mv`: `tests/FeatherQR.Tests`, `tests/FeatherQR.AotAnalysis`, `src/FeatherQR.Benchmark`, `src/FeatherQR.Playground`, `FeatherQR.slnx`. Assembly names and `InternalsVisibleTo` strings follow.
- Every path in `.github/workflows/*.yaml`, `lint-dotnet.yaml`'s solution file, `tools/*.cs` (`#:project` in `public_api.cs`, defaults in `bump_version.cs`), and `.claude/skills/*/SKILL.md`.
- The assistant memory notes that name old paths are updated when the paths change, per the rename rule in the authoring guidelines.

**Exit.** `grep -r "SkiaSharp.QrCode"` returns only the metapackage project, the migration and README text that deliberately mentions the old ID, and this plan.

### Phase 5: Documentation

**Goal.** A new reader installs the right package and an existing reader knows what changed.

- README: the first lines define what lightweight means; the installation section becomes a three-row table (which package, when); Quick Start shows `FeatherQR.SkiaSharp`; the decoder section shows `QRCodeImageDecoder.TryDecode` first and the C# 14 form second; badges for the three packages; the Playground link. No benchmark numbers, per the README rules.
- `docs/migration.md`: a 2.0.0 section covering the namespaces, the package choice, `TryDecode(SKBitmap)`, and the fact that `SkiaSharp.QrCode` keeps working as a metapackage.
- `DESIGN.md`: the pure-core principle now names the package boundary that enforces it.
- `specs/qrcode-symbologies.md`: a package architecture section (topology, dependency rules, the trimming result and the "no unified entry point" rule, the `InternalsVisibleTo` inventory, the lightweight definition).
- Playground `index.html` text and links.

**Exit.** The docs index lists no stale paths; every link resolves.

### Phase 6: Rename the repository

**Goal.** `github.com/guitarrapc/FeatherQR` is the home, old links still land somewhere sensible, and a release runs from the new name.

1. Before the rename: add `SkiaSharp.QrCode/index.html` to `guitarrapc/guitarrapc.github.io` with a meta refresh to `https://guitarrapc.github.io/FeatherQR/`, and confirm it is deployed (it is shadowed by the project site until the rename, which is expected).
2. Rename the repository on GitHub. Never create a new repository with the old name; that is what breaks the redirect.
3. Update the nuget.org Trusted Publishing policy to the new repository name; update `PackageProjectUrl` and `RepositoryUrl` in `Directory.Build.props`, the README badges and links, the Playground `og:url`, release and license links, and the issue link in this plan's successor documents.
4. Verify: old web URL redirects, old `git remote` works, old Playground URL lands on the stub and forwards, CodeQL and Pages still run.
5. Release `2.0.0-preview.2` from the renamed repository to prove the pipeline end to end, including Trusted Publishing.
6. Local: the working directory and the memory directory keyed to it are renamed by the user; this is outside the repository and is listed so it is not forgotten.

**Exit.** `2.0.0-preview.2` on nuget.org with `RepositoryUrl` pointing at the new name, and the four verifications above recorded in the Progress log.

### Phase 7: Fold into the specs and delete this plan

The decisions above graduate into `specs/qrcode-symbologies.md` (package architecture) and `DESIGN.md`, the lessons go into the design records, outstanding items (SVG writer, 1-bit PNG, any deferred `InternalsVisibleTo` replacement) go into the scope table with a "Revisit when", and this file is deleted. `2.0.0` final is gated by the type-rename plan, not by this one.

## Risks and open points

- **The three IDs are unclaimed until Phase 3 pushes.** Anyone can take `FeatherQR` on nuget.org before then. The preview release is therefore scheduled as early as the pipeline allows, not at the end.
- **Trusted Publishing after the rename.** If the policy is not updated before the Phase 6 release, `NuGet/login` fails and nothing is published; the failure is safe but the release must be re-run.
- **Old Playground links from immutable NuGet READMEs.** Covered by the user-site stub; the stub must stay for as long as 1.x packages are listed.
- **Consumers on C# 12 and 13.** They cannot write `QRCodeDecoder.TryDecode(bitmap)`. The documented spelling is the enclosing class; a consumer who sets `<LangVersion>14</LangVersion>` gets the short form. This is stated in the migration document so it is not reported as a missing overload.
- **Metadata size.** 160 KB of the 384 KB assembly is metadata, proportional to member count. The 2.0.0 removals shrink it; this plan does not touch it.

## References

| Document | Holds |
|---|---|
| [references/featherqr-size-measurements.md](references/featherqr-size-measurements.md) | IL and metadata breakdown, trimmed sizes per consumer profile, comparator sizes, the measurement scripts |
| [references/featherqr-split-inventory.md](references/featherqr-split-inventory.md) | Every file, project, workflow, tool and document the split touches; the Pages and Trusted Publishing facts; the verified extension-member and metapackage mechanics |

## Progress log

Entries are appended per phase: Done, Lessons Learned, benchmark delta (or why none applies).

### Phase 1: Split the projects (2026-09-04)

**Done.** `src/FeatherQR` (core, four TFMs, no SkiaSharp) and `src/FeatherQR.SkiaSharp` (rendering, `ProjectReference` to the core, `PackageReference` SkiaSharp) build from the moved sources; `src/SkiaSharp.QrCode` is already the empty metapackage shell (`IncludeBuildOutput` false, no compile items, `ProjectReference` to the rendering project, NU5128 suppressed, validation off), because with every source moved out the only alternative was an empty compiled assembly. Namespaces are `FeatherQR` and `FeatherQR.SkiaSharp` (`Image` folded in); every rendering file carries `using SkiaSharp;` above its namespace. The `SKBitmap` overloads live in `QRCodeImageDecoder` / `MicroQRCodeImageDecoder` / `RmQRCodeImageDecoder` as C# 14 extension blocks (the Phase 2 shape, taken now because a moved static method needs a home and the extension block is the decided one); `BitmapLuminanceConverter` (internal, rendering) reads the pixmap layout and hands bytes to the core kernels, which stay internal until Phase 2 makes them public. `InternalsVisibleTo` from the core to `FeatherQR.SkiaSharp` and to the test assembly; from the rendering assembly to the test assembly. Tests moved to `FeatherQR.Tests` in the one assembly, 17,651 pass. New `CoreAssemblyDependencyTest` reads the core's assembly references from metadata for every TFM listed in the csproj. Benchmark, Playground, samples, the interop fixtures and the AOT gate reference `FeatherQR.SkiaSharp` as a consumer would; the AOT gate roots both assemblies and publishes clean; the Playground publishes (trimmed, no AOT). `build.yaml` / `release.yaml` trim the docs of both assemblies and check two approved listings (`src/FeatherQR/PublicAPI.approved.txt`, `src/FeatherQR.SkiaSharp/PublicAPI.approved.txt`), generated by the unchanged tool. `dotnet pack` produces the three packages.

**Lessons learned.**

- **The core was never dependency-free on netstandard.** `Span<T>`, `ArrayPool<T>` and `Unsafe` arrived through SkiaSharp's own dependency on `System.Memory` / `System.Runtime.CompilerServices.Unsafe`; removing SkiaSharp surfaced 170 errors on netstandard2.0/2.1 and nothing on net8.0+. The core now references `System.Memory` (netstandard2.0) and `System.Runtime.CompilerServices.Unsafe` (netstandard2.1) at the versions SkiaSharp 4.148 pins, so a consumer of both packages resolves one copy. "Zero dependencies" is true of net8.0 and net10.0 and means "BCL shims only" on netstandard; the README definition in Phase 5 and the Phase 3 assertion must say so.
- **Transitive pinning promotes packages into the nuspec.** With `CentralPackageTransitivePinningEnabled`, `FeatherQR.SkiaSharp` lists `SkiaSharp.NativeAssets.Win32` / `.macOS` as direct dependencies and the metapackage lists `SkiaSharp` and the native assets beside `FeatherQR.SkiaSharp`; the shipped 1.1.1 package has the same shape. Phase 3's dependency assertion is written against this measured shape (or pinning is switched off for the packable projects first); the expectation in the Phase 3 bullet is corrected below.
- **C# 14 extension blocks emit public nested marker types** (`<G>$hash`, `<M>$hash`) that are visible to metadata readers and reflection. They broke `filter_public_docs.cs` (undocumented public types), the shipped-documentation test and the approved listing. All three tools and the test now skip nested types with unspeakable names; the members themselves list on the enclosing class, which is what a caller writes. This closes the "decide in Phase 2 whether to filter" note.
- **Implicit `SK*` resolution reached further than the library.** The tests (`SkiaSharp.QrCode.Tests`), the Playground (`SkiaSharp.QrCode.Playground`) and the Benchmark (no namespace, `global using SkiaSharp.QrCode`) also resolved `SKBitmap` through the parent namespace; 28 test files needed `using SkiaSharp;`, and every consumer of the bitmap decoders needs `using FeatherQR.SkiaSharp;` in scope for the extension to bind. The Benchmark disambiguated `QRCodeGenerator` from QRCoder's by writing `SkiaSharp.QrCode.QRCodeGenerator`; those became `FeatherQR.QRCodeGenerator`.
- **The rendering tool `public_api.cs` renders only the core for now** (it reflects over the `FeatherQR` assembly); enumerating both into one page is Phase 3 as planned, and the release workflow's API page is incomplete until then.
- **Repository mechanics.** The working tree is mixed CRLF/LF under `core.autocrlf=true`; line-anchored `perl -pi` edits need `\r?$`, and an inserted line takes the file's ending or the file ends up mixed. `git mv` of the whole `src/SkiaSharp.QrCode` directory first, then of the rendering files out of it, kept history on 121 renames.

**Benchmark delta.** None applies: no encoder, decoder or renderer code changed; the luminance kernels moved namespace only and the `SKBitmap` path calls the same kernel through one extra static call. `BenchmarkDotNet` was not run.

### Phase 2: The public seam (2026-09-04)

**Done.** The luminance seam is one internal entry, `LuminanceConverter.Convert(ReadOnlySpan<byte> pixels, int width, int height, int rowBytes, PixelLayout layout, bool premultipliedAlpha, Span<byte> luminance)`, with the internal `PixelLayout` enum (`Gray8`, `Rgba8888`, `Bgra8888`, `Rgb888x`), both in `Internals.ImageDecoders`. It was first built public in the root namespace with a `TryGetBufferSize` companion, as the plan said; the maintainer then decided not to grow the public surface ahead of a concrete request (an API is added in a minor and removed only in a major), so it was made internal again and `TryGetBufferSize` was dropped (`ImageDimensions.TryGetPixelCount` serves the first-party callers). The decision section above is revised accordingly. The entry validates its arguments (undefined layout, negative dimension, stride shorter than a row: `ArgumentOutOfRangeException`; short pixel or luminance buffer: `ArgumentException`; zero width or height: no-op), and the Gray8 path now runs the same extent check as the RGBA kernels. `FeatherQR.SkiaSharp.Internals.BitmapLuminanceConverter` is a pure adapter (an `SKColorType` to `PixelLayout` lookup over `SKPixmap.GetPixelSpan()`), so a later promotion of the seam is a visibility change only. The rendering package mirrors the core: its internal types (`BitmapLuminanceConverter`, `QRImageLayout`, `BufferWriterStream`, `SvgRootAttributeInjectorStream`) live under `FeatherQR.SkiaSharp.Internals`; `Vector2Slim` joins them when the 2.0.0 rename plan makes it internal. The public `TryDecodeImage` overloads now document that transparent pixels must be composited against white, which is the rule a hand-written converter would otherwise miss. `RmQRCodeImageBuilder` no longer reaches `RmQRConstants`: the default quiet zone comes from `default(RmQRCodeGeneratorOptions).QuietZoneSize` and version validation from `Enum.IsDefined`. The extension-block decoders from Phase 1 are unchanged; `ImageDecoderCallFormTest` covers both spellings (`QRCodeDecoder.TryDecode(bitmap, ...)` and `QRCodeImageDecoder.TryDecode(bitmap, ...)`), both overloads, all three symbologies, the null and too-small contracts. `LuminanceConverterContractTest` pins the seam through `InternalsVisibleTo`: the BT.601 formula and white compositing per layout, premultiplied handling, layout equivalence on padded rows, the short-last-row allowance, every validation branch, and byte parity between the Skia adapter and a direct call for all six color/alpha type combinations plus the redraw path for an unsupported color type. The `InternalsVisibleTo` inventory is in `specs/qrcode-symbologies.md` ("Package seam"): one entry replaced (`RmQRConstants`), two kept with recorded reasons (the luminance seam; `IModuleMatrixView` / matrix views / `ModuleRunEnumerator<TView>` behind `QRCodeRenderer`). The public surface of both assemblies is unchanged from Phase 1; 17,761 tests pass; AOT gate green.

**Exit check.** With the `InternalsVisibleTo` grant to `FeatherQR.SkiaSharp` removed from `FeatherQR.csproj`, the rendering package fails to compile at exactly the sites the inventory keeps (`QRCodeRenderer` and the luminance adapter and bitmap decoders); every image builder compiles against the public surface. The sample check the plan names (`SimpleGenerate` against the public surface) holds trivially: no sample or tool has ever had a grant.

**Lessons learned.**

- **"Writable against the public surface" has two readings, and the plan conflated them.** A third-party adapter can decode today through `TryDecodeImage` and can render through `GetModuleRectangles`; what it cannot get is the first-party fast path. The plan's phrase was read as the second, which is what led to publishing the kernels; the maintainer's rule is the first: the public surface grows on request, not on anticipation. Recorded here so Phase 5's README and the follow-up renderer packages do not reopen it.
- **A seam needs its own contract tests, not the kernel's.** The parity tests prove the tiers agree with each other; nothing proved what the bytes mean. Writing the formula into a test found no bug but did catch a wrong assumption in the test itself: all-zero bytes in an alpha layout are fully transparent and convert to white, not black. That is precisely the behavior an adapter author would trip over, so the `TryDecodeImage` documentation now says it.
- **`Enum.IsDefined` is the right public replacement for an internal range check only when the enum is contiguous.** `RmQRVersion` is (1..32), so the two are exact equivalents; the spec entry says so, so a future non-contiguous enum does not inherit the pattern blindly.
- **The "build without the grant" experiment is the inventory.** Enumerating `InternalsVisibleTo` uses by reading is error-prone (the Phase 1 inventory expected one entry; there were three); the compiler produces the list in seconds. The spec records the method so later phases repeat it rather than the reading.
- The seam entry `Convert` takes `(pixels, width, height, rowBytes, layout, premultipliedAlpha, luminance)`: source first, destination last, matching the `TryDecodeImage(luminance, width, height, ...)` family it feeds; the kernels behind it keep their historical `(pixels, luminance, ...)` order and are not renamed, to keep the parity tests and SIMD files untouched.

**Benchmark delta.** None applies: the kernels are byte-identical and the adapter adds one enum switch per bitmap outside the pixel loop. `BenchmarkDotNet` was not run.
