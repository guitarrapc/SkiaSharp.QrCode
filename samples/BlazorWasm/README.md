# BlazorWasm sample

Blazor WebAssembly sample for SkiaSharp.QrCode. The UI mirrors
[SkiaSharp.QrCode.Playground](../../src/SkiaSharp.QrCode.Playground) (the pure-WASM
GitHub Pages playground), but demonstrates the Blazor-specific integration:

- **Live preview** renders directly onto a [SkiaSharp.Views.Blazor](https://www.nuget.org/packages/SkiaSharp.Views.Blazor)
  `SKCanvasView` with the low-level `QRCodeRenderer` canvas API — every control change repaints the surface.
- **Download PNG / SVG** exports through the `QRCodeImageBuilder` fluent API at the selected export size.
- [QrOptions.cs](QrOptions.cs) holds the page state; [QrImageFactory.cs](QrImageFactory.cs) translates it
  into library calls shared by both paths.

## Run

SkiaSharp.Views.Blazor links the native Skia library into the WASM binary, which requires
the wasm-tools workload:

```bash
# once
dotnet workload install wasm-tools

dotnet run --project samples/BlazorWasm
```
