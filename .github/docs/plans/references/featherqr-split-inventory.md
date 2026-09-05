# Repository inventory for the core split

Reference material for [featherqr-core-split-plan.md](../featherqr-core-split-plan.md): everything in the repository and outside it that the split touches, collected on 2026-09-04 at commit `3346bec` so that later sessions can start from the phase list instead of re-surveying. Paths are pre-split; the plan's Phase 4 renames them. This file is deleted together with the plan.

## 1. The seam: what touches SkiaSharp today

Files that reference an `SK*` type (the only ones that move to, or need adapting for, `FeatherQR.SkiaSharp`):

| File | Goes to | Note |
|---|---|---|
| `src/SkiaSharp.QrCode/Image/*.cs` (13 files: builders, `IconData`, `IconShape`, `ModuleShape`, `FinderPatternShape`, `GradientOptions`, `QRImageLayout`, `BufferWriterStream`, `SvgRootAttributeInjectorStream`, `Vector2Slim`) | rendering | `Vector2Slim` has no public consumer and becomes internal in 2.0.0 |
| `src/SkiaSharp.QrCode/QRCodeRenderer.cs` | rendering | Uses the internal module-run enumerator (`Internals/ModuleRunScanner.cs`, `IModuleMatrixView`); the one expected `InternalsVisibleTo` dependency |
| `src/SkiaSharp.QrCode/QRCodeExtensions.cs` | rendering | `SKCanvas` extension `Render` overloads (6) |
| `src/SkiaSharp.QrCode/QRCodeDecoder.cs`, `MicroQRCodeDecoder.cs`, `RmQRCodeDecoder.cs` | core, minus the two `TryDecode(SKBitmap ...)` overloads each, which move to the rendering package as extension members | `TryDecodeImage(ReadOnlySpan<byte> luminance, int width, int height, ...)` is already public on all three and stays in the core |
| `src/SkiaSharp.QrCode/Internals/ImageDecoders/LuminanceConverter.cs` | split: `Convert(SKBitmap, Span<byte>)` and `TryConvertPixmap(SKPixmap, ...)` go to rendering; the pixel-format kernels (`ConvertRgba`, scalar/AVX2/AdvSimd tiers, Gray8, BGRA8888, RGBA8888, RGB888x, premultiplied handling, `IsVectorTierTaken*`, `ConvertRgbaForTest`) become the core's public luminance API | Tests for tier parity move with the kernels |

Everything else under `src/SkiaSharp.QrCode/` (35 root public types, `Internals/**`) has no Skia reference and moves to the core unchanged except for the namespace line. The library has no `using SkiaSharp;` today because the root namespace `SkiaSharp.QrCode` resolves `SK*` implicitly; after the move every rendering file needs `using SkiaSharp;` above its file-scoped namespace.

Namespaces today (file counts): `SkiaSharp.QrCode` 35, `.Image` 13, `.Internals` 15, `.Internals.StandardQr` 14, `.Internals.RmQr` 14, `.Internals.MicroQR` 10, `.Internals.ImageDecoders` 9, `.Internals.BinaryEncoders` 6, `.Internals.BinaryDecoders` 4. Note the mixed casing (`StandardQr`, `RmQr` vs `MicroQR`); the 2.0.0 rename plan fixes it to `QR`, this plan only replaces the `SkiaSharp.QrCode` prefix.

Public members with Skia types in `PublicAPI.approved.txt`: 61 lines of 678. They are exactly the rendering surface listed above (`Render`, `GetFinderPatternRect`, `GetIconRects`, `Draw` on shapes, `GradientOptions` colors, `IconData.FromImage*`, builder `GetImageBytes` / `WriteImage` / `ToBitmap` / `ToImage` / `WithColors` / `WithFormat`, and the six `TryDecode(SKBitmap ...)`).

## 2. Project graph

| Project | References the library as | Framework(s) | Notes |
|---|---|---|---|
| `src/SkiaSharp.QrCode` | (the library) | netstandard2.0; netstandard2.1; net8.0; net10.0 | `IsPackable`, `GenerateDocumentationFile`, `EnablePackageValidation` baseline 1.1.1, `IsAotCompatible` on net7+, SBOM on, `InternalsVisibleTo` to the test assembly with the `opensource.snk` public key, `PackageReference`: PolySharp (private), SkiaSharp, Microsoft.Sbom.Targets (private) |
| `tests/SkiaSharp.QrCode.Tests` | ProjectReference | net8.0; net10.0 | TUnit; ZXing.Net and its SkiaSharp binding as oracles; folders `StandardQr`, `MicroQR`, `RmQr`, `Shared`, `Rendering`, `Fixtures`, `testdata`; 29 test files use `SKBitmap` / `SKCanvas` / `SKColor` |
| `tests/SkiaSharp.QrCode.AotAnalysis` | ProjectReference | net10.0 | `PublishAot`, `TrimmerRootAssembly Include="SkiaSharp.QrCode"`, warnings as errors; needs a second `TrimmerRootAssembly` after the split |
| `src/SkiaSharp.QrCode.Benchmark` | ProjectReference | net10.0 | BenchmarkDotNet; comparators CodeGlyphX, Net.Codecrete.QrCodeGenerator, QRCoder, ZXing.Net |
| `src/SkiaSharp.QrCode.Playground` | ProjectReference | net10.0 (browser-wasm) | `RootNamespace` / `AssemblyName` set explicitly; `PlaygroundSoftFingerprint` publish property; `wwwroot/index.html` carries `og:url` and release / license links to the old repository name |
| `tools/QRInteropFixtures` | ProjectReference | net10.0 | SkiaSharp, ZXing.Net, ZXingCpp oracles |
| `samples/BlazorWasm`, `ConsoleApp`, `ConsoleAppNativeAOT`, `NanoServerGenerate`, `SimpleGenerate`, `SimpleSerialize` | ProjectReference | | All six reference the library project directly; after the split they reference `FeatherQR.SkiaSharp` |

Shared build files: `Directory.Build.props` (single `<Version>1.2.0</Version>`, `IsPackable` false by default, signing with `opensource.snk`, README and LICENSE packed, `PackageProjectUrl` / `RepositoryUrl` to the old repository name) and `Directory.Packages.props` (central package versions; SkiaSharp 4.148.0). Solution file: `SkiaSharp.QrCode.slnx`.

## 3. CI and tools that name the assembly or project

| Location | What it does today | Change |
|---|---|---|
| `.github/workflows/build.yaml`, job `build` | `dotnet run tools/filter_public_docs.cs -- src/SkiaSharp.QrCode/bin/Release/*/SkiaSharp.QrCode.xml`; on linux-x64 `check_public_api.cs --check src/SkiaSharp.QrCode/PublicAPI.approved.txt src/SkiaSharp.QrCode/bin/Release/*/SkiaSharp.QrCode.dll`; `dotnet pack` for package validation with SBOM off | Both tools take several inputs already; run over both assemblies (two approved listings, two XML globs); add the nupkg dependency assertion after pack |
| `build.yaml`, job `run` | Runs `ConsoleApp`, `SimpleGenerate`, `ConsoleAppNativeAOT` per RID | Paths only |
| `build.yaml`, job `aot-analysis` | Publishes `tests/SkiaSharp.QrCode.AotAnalysis` and runs the binary | Path and the second root assembly |
| `build.yaml`, job `build-playground` | Publishes the Playground with `RunAOTCompilation=false` | Path |
| `.github/workflows/release.yaml`, job `validate` | Verifies `<Version>` in `Directory.Build.props` equals the tag (tag pattern `[0-9]+.[0-9]+.[0-9]+*`, which matches `2.0.0-preview.1`) | None |
| `release.yaml`, job `build-dotnet` | Build, trim docs, check API, `dotnet pack -o ./publish`, attest `./publish/*.nupkg`, upload | Same tool changes as `build.yaml`; pack and attest already glob |
| `release.yaml`, job `build-playground` | `public_api.cs --html --source-links -o src/SkiaSharp.QrCode.Playground/wwwroot/api/index.html`, then publish | Tool must render both assemblies; path |
| `release.yaml`, job `create-release` | `NuGet/login` (OIDC Trusted Publishing, `SYNCED_NUGET_USER` secret), `dotnet nuget push "./nuget/*.nupkg" --skip-duplicate` | None in the file; the nuget.org policy is bound to the repository name (see section 5) |
| `.github/workflows/lint-dotnet.yaml` | `sln-file: "SkiaSharp.QrCode.slnx"` | Solution rename |
| `tools/check_public_api.cs` | `[--check] <approved.txt> <assembly.dll>...`; metadata-based, one listing per invocation | Run twice |
| `tools/filter_public_docs.cs` | `[--check] <documentation.xml>...`; finds the dll beside each xml | Pass both globs |
| `tools/public_api.cs` | `#:project ../src/SkiaSharp.QrCode/SkiaSharp.QrCode.csproj`, reflects over `typeof(SkiaSharp.QrCode.QRCodeData).Assembly` | Reference the rendering project (which brings the core) and enumerate both assemblies |
| `tools/bump_version.cs` | Rewrites `<Version>` in `Directory.Build.props` and version strings in `README.md`; major / minor / patch only | Preview versions are a manual edit; README version strings will name three packages |
| `.claude/skills/test-first-development/SKILL.md` | Nine path references (`tests/SkiaSharp.QrCode.Tests`, `src/SkiaSharp.QrCode.Benchmark`, `src/SkiaSharp.QrCode.Playground`, a link to the authoring guidelines under the old repository URL) | Phase 4 |
| `src/SkiaSharp.QrCode/CompatibilitySuppressions.xml` | Suppresses the `EciModeExtensions` removal against baseline 1.1.1 for the four TFMs | Deleted with the old project; no baseline for the new packages until 2.0.0 ships |

## 4. Documentation that names the package or repository

- `README.md`: badges (build, release, NuGet for `SkiaSharp.QrCode`), the Playground link `https://guitarrapc.github.io/SkiaSharp.QrCode/`, Installation, Quick Start, API Overview (Image Builders, `QRCodeRenderer`, Generators, options, Zero-allocation APIs, Decoders), Platform-Specific Considerations with `<PackageReference Include="SkiaSharp.QrCode" Version="1.2.0" />` examples (six occurrences), FAQ, Release flow (bump, tag, draft release), the `GetRequiredBufferSize` obsolete note.
- `docs/migration.md`: version-ordered bullet list at the top, then sections; 2.0.0 is already referenced by the `GetRequiredBufferSize` and `Compression` removals.
- `.github/docs/DESIGN.md`: "A Pure C# Core with No External Dependencies" principle (English and Japanese).
- `.github/docs/specs/qrcode-symbologies.md`: architecture record; line 64 names `src/SkiaSharp.QrCode/PublicAPI.approved.txt` and the 2.0.0 reshaping.
- `.github/docs/README.md`: the documentation index (this plan is listed there).
- Playground `wwwroot/index.html`: `og:url`, releases link, license link.

## 5. Facts outside the repository

- **GitHub Pages does not redirect on repository rename.** Web URLs, issues, stars and every `git` operation against the old name redirect indefinitely, as long as the old name is never reused; project-site URLs are the documented exception. Current Pages: `https://guitarrapc.github.io/SkiaSharp.QrCode/`, `build_type` workflow, no custom domain. `guitarrapc/guitarrapc.github.io` exists with Pages enabled, so a `SkiaSharp.QrCode/index.html` meta-refresh stub there will serve the old path once the project site no longer claims it. Creating a new repository named `SkiaSharp.QrCode` to host the stub would break the repository redirect and is ruled out.
- **nuget.org Trusted Publishing** is configured per repository owner and name. After the rename the policy must be edited on nuget.org before the next release; until then `NuGet/login` fails and the push step never runs.
- **The memory directory** used by the assistant is keyed to the local working directory path (`D--github-guitarrapc-SkiaSharp-QrCode`). Renaming the local clone moves it; the maintainer copies or renames the directory as a local step.
- **Reference libraries** (`.references/`, not committed) and the private micro-benchmark workflow are unaffected.

## 6. Verified mechanics

### C# 14 extension members for `TryDecode(SKBitmap)`

Probe built on 2026-09-04: a netstandard2.0 library (`LangVersion` 14) declaring

```csharp
public static class QRCodeImageDecoder
{
    extension(QRCodeDecoder)
    {
        public static bool TryDecode(FakeBitmap bitmap, out string text) { ... }
    }
}
```

and two net8.0 consumers. With `LangVersion` 12 the consumer compiles and runs `QRCodeImageDecoder.TryDecode(bitmap, out text)`; with `LangVersion` 14 it compiles and runs both that and `QRCodeDecoder.TryDecode(bitmap, out text)`. No runtime support is needed; the implementation methods are ordinary public static methods on the enclosing class. Consumer default `LangVersion` is tied to the target framework (net8.0 is C# 12, net10.0 is C# 14), so the enclosing-class spelling is the one most consumers will use and the one documented first. The approved-API tooling reads metadata, so expect the compiler-generated marker type inside the extension block to appear in the listing; decide in Phase 2 whether to filter it.

### Metapackage packing

An empty package is produced by a project with `IncludeBuildOutput=false`, no compile items, a `ProjectReference` to `FeatherQR.SkiaSharp` (turned into a package dependency at the lockstep version on pack), `NoWarn` for `NU5128`, and `EnablePackageValidation=false`. The README packed into it should say what it is and point to `FeatherQR.SkiaSharp`.

### Lockstep dependency range

A `ProjectReference` packs as `FeatherQR (>= <version>)`. The plan keeps that default rather than an exact `[x.y.z]` pin; revisit only if a consumer mixing versions is ever observed.
