# SkiaSharp.QrCode.Playground

Browser playground for SkiaSharp.QrCode, published to GitHub Pages: https://guitarrapc.github.io/SkiaSharp.QrCode/

A static `Microsoft.NET.Sdk.WebAssembly` app (no Blazor, no server). The page script loads the
.NET runtime via `_framework/dotnet.js` and calls `[JSExport]` methods on `QrInterop` to render
QR codes with the real SkiaSharp native library compiled to WebAssembly.

## Local development

The SkiaSharp native library is linked into `dotnet.native.wasm` **only on publish**
(see the `_IsPublishing` condition in the csproj). This keeps solution builds fast and free of
the Emscripten toolchain, but it means `dotnet run`/`dotnet build` outputs cannot render —
always go through `dotnet publish`:

```bash
# once
dotnet workload install wasm-tools

# fast inner loop (no AOT, no trimming)
dotnet publish src/SkiaSharp.QrCode.Playground/SkiaSharp.QrCode.Playground.csproj -c Debug -p:PlaygroundSoftFingerprint=true -o publish/playground

# serve the static output (any static file server works)
dotnet serve -d publish/playground/wwwroot   # or: python -m http.server -d publish/playground/wwwroot
```

`-p:PlaygroundSoftFingerprint=true` emits both fingerprinted and plain filenames so the page
works on hosts without import-map rewriting (GitHub Pages, plain static servers).

The production build adds AOT + full trimming:

```bash
dotnet publish src/SkiaSharp.QrCode.Playground/SkiaSharp.QrCode.Playground.csproj -c Release -p:PlaygroundSoftFingerprint=true -o publish/playground
```

Do not pass `-r browser-wasm`: the WebAssembly SDK already defaults to it, and as a CLI global
property it propagates to the multi-targeted library reference, which would then demand the
`wasm-tools-net8` workload.

## Deploy

The `build-playground` / `deploy-playground` jobs in
[.github/workflows/release.yaml](../../.github/workflows/release.yaml) publish to the
`github-pages` environment as part of every release tag push (`X.Y.Z`). To redeploy a tag,
re-run its release workflow run. GitHub Pages must be configured with
**Source: GitHub Actions** in the repository settings.

## Share links

The Share button stores the full playground state (compressed with `CompressionStream`,
base64url) in the URL hash — nothing is sent to a server. Uploaded logo images are excluded
from share links; the link falls back to the built-in logo.
