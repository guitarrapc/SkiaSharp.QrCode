# Size measurements behind the core split

Reference material for [featherqr-core-split-plan.md](../featherqr-core-split-plan.md). Measured on 2026-09-04 against `main` at commit `3346bec`, `SkiaSharp.QrCode.dll` built in Release for net10.0 on win-x64 with the .NET 10 SDK. Re-run the scripts below after Phase 1 to record the per-package numbers in the Progress log. This file is deleted together with the plan.

## What the numbers decided

- A per-symbology package split was rejected: trimming already gives per-symbology sizing to the consumers who care (AOT, WASM, `PublishTrimmed`), and it works because the three generators and decoders are separate static classes that do not reference one another.
- "Lightweight" in the FeatherQR pitch means zero dependencies, zero hot-path allocations and trim safety, not assembly bytes: the assembly is larger than the two minimalist generators because it carries three symbologies, decoders and SIMD paths.
- The real weight is the native SkiaSharp payload, which is deployed per RID even when trimming removes every managed SkiaSharp call. Only a package boundary removes it.

## Untrimmed assembly breakdown (net10.0, 384 KB)

| Component | Size |
|---|---:|
| IL (method bodies) | 191 KB |
| Metadata (names, signatures, tables) | 160 KB |
| Static data blobs (lookup tables, Kanji table) | 26 KB |

IL by namespace:

| Namespace | IL bytes | Types | Methods |
|---|---:|---:|---:|
| `Internals.StandardQr` | 60,499 | 23 | 244 |
| root (public types) | 36,622 | 40 | 417 |
| `Internals.RmQr` | 32,518 | 18 | 176 |
| `Internals.MicroQR` | 16,278 | 8 | 102 |
| `Internals.ImageDecoders` | 13,419 | 10 | 85 |
| `Image` (Skia builders, shapes) | 10,765 | 24 | 237 |
| `Internals.BinaryEncoders` | 10,607 | 4 | 42 |
| `Internals` (shared) | 10,487 | 22 | 153 |
| `Internals.BinaryDecoders` | 4,898 | 3 | 26 |

Largest types: `StandardQr.ModulePlacer` 39,376 (table-driven placement plus the SIMD mask tiers), `BinaryEncoders.EccBinaryEncoder` 8,844, `RmQr.RmQRImageDecoder` 7,813, `StandardQr.QRImageDecoder` 7,318, `QRCodeGenerator` 7,120, `RmQr.RmQRModulePlacer` 6,707, `ImageDecoders.LuminanceConverter` 6,471, `MicroQR.MicroQRModulePlacer` 5,328. The largest static blob is 16 KB; the Kanji table is not a size problem.

## Trimmed assembly by consumer profile

`PublishTrimmed=true`, `TrimMode=full`, self-contained win-x64, net10.0 console apps referencing the library project:

| Consumer profile | `SkiaSharp.QrCode.dll` | Notes |
|---|---:|---|
| `QRCodeGenerator.CreateQrCode` only | 95 KB | `SkiaSharp.dll` trimmed away entirely; `libSkiaSharp.dll` (11.9 MB) still deployed |
| All three generators | 153 KB | |
| All three generators and decoders, plus `TryDecode(SKBitmap)` | 229 KB | `SkiaSharp.dll` trimmed to 34 KB |

IL surviving in the QR-encode-only profile: `Internals.StandardQr` 30,550 (of which `ModulePlacer` 24,872), `BinaryEncoders` 7,856, shared `Internals` 4,194, root 3,597. Static data 9 KB of the 26 KB.

## Comparators (from the local NuGet cache)

| Assembly | netstandard2.0 | net8.0 |
|---|---:|---:|
| `QrCodeGenerator.dll` (Net.Codecrete.QrCodeGenerator 3.1.0) | 144 KB | |
| `QRCoder.dll` (1.8.0) | 195 KB | |
| `zxing.dll` (ZXing.Net 0.16.11) | 502 KB | 502 KB |
| `CodeGlyphX.dll` (2.1.0, 46 symbologies) | 1,910 KB | 2,077 KB |
| `SkiaSharp.QrCode.dll` (this repo, 1.2.0 + main) | 304 KB | 388 KB |

## Scripts

### IL breakdown by namespace and type

Save as `ilsize.cs` and run `dotnet run ilsize.cs -- <path-to.dll>`. Reads metadata only; no package references.

```csharp
#:property LangVersion=14
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var path = args[0];
using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var byNs = new Dictionary<string, (int il, int types, int methods)>();
var byType = new Dictionary<string, int>();
foreach (var th in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(th);
    var ns = md.GetString(t.Namespace); var name = md.GetString(t.Name);
    if (!t.GetDeclaringType().IsNil) { var d = md.GetTypeDefinition(t.GetDeclaringType()); ns = md.GetString(d.Namespace); name = md.GetString(d.Name) + "+" + name; }
    int il = 0, mc = 0;
    foreach (var mh in t.GetMethods()) { var m = md.GetMethodDefinition(mh); if (m.RelativeVirtualAddress != 0) { il += pe.GetMethodBody(m.RelativeVirtualAddress).Size; mc++; } }
    var e = byNs.GetValueOrDefault(ns); byNs[ns] = (e.il + il, e.types + 1, e.methods + mc);
    byType[ns + "." + name] = il;
}
long rva = 0; var rvaList = new List<(int size, string name)>();
foreach (var fh in md.FieldDefinitions)
{
    var f = md.GetFieldDefinition(fh); if (f.GetRelativeVirtualAddress() == 0) continue;
    var sig = md.GetBlobReader(f.Signature); sig.ReadByte(); var tc = sig.ReadSignatureTypeCode(); int size = 0;
    if (tc == SignatureTypeCode.TypeHandle) { var h = sig.ReadTypeHandle(); if (h.Kind == HandleKind.TypeDefinition) size = md.GetTypeDefinition((TypeDefinitionHandle)h).GetLayout().Size; }
    else size = tc switch { SignatureTypeCode.Byte or SignatureTypeCode.SByte => 1, SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 => 2, SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single => 4, SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double => 8, _ => 0 };
    rva += size; rvaList.Add((size, md.GetString(f.Name)));
}
Console.WriteLine($"file={fs.Length/1024} KB  metadata={md.MetadataLength/1024} KB  IL total={byNs.Values.Sum(v=>v.il)/1024} KB  RVA data total={rva/1024} KB");
Console.WriteLine("\n== IL bytes by namespace");
foreach (var kv in byNs.OrderByDescending(k => k.Value.il)) Console.WriteLine($"{kv.Value.il,8}  types={kv.Value.types,3} methods={kv.Value.methods,4}  {kv.Key}");
Console.WriteLine("\n== top 25 types by IL");
foreach (var kv in byType.OrderByDescending(k => k.Value).Take(25)) Console.WriteLine($"{kv.Value,8}  {kv.Key}");
Console.WriteLine("\n== top 15 RVA data blobs");
foreach (var (s, n) in rvaList.OrderByDescending(x => x.size).Take(15)) Console.WriteLine($"{s,8}  {n}");
```

### Trimmed size per consumer profile

One console project per profile, outside the repository tree so `Directory.Build.props` does not apply to the app:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>full</TrimMode>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="<repo>/src/SkiaSharp.QrCode/SkiaSharp.QrCode.csproj" />
  </ItemGroup>
</Project>
```

Profiles used: (1) `QRCodeGenerator.CreateQrCode(text, ECCLevel.M).Size`; (2) the same plus `MicroQRCodeGenerator.CreateMicroQRCode(text, MicroQREccLevel.M)` and `RmQRCodeGenerator.CreateRmQRCode(text, RmQREccLevel.M)`; (3) profile 2 plus `TryDecode` on each generated matrix and `QRCodeDecoder.TryDecode(new SKBitmap(64, 64), out _)`. Publish with `dotnet publish -c Release -o out_<profile>` and read the size of `SkiaSharp.QrCode.dll` (after the split: `FeatherQR.dll` and `FeatherQR.SkiaSharp.dll`) in the output directory. The trimmed assemblies can be fed to `ilsize.cs` for the surviving-IL breakdown.
