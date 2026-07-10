using System.Runtime.Versioning;

// Browser-only assembly: satisfies CA1416 for [JSExport]/[JSImport] call sites.
[assembly: SupportedOSPlatform("browser")]

// WASM host starts the Mono runtime once; QrInterop is exported to JavaScript.
Console.WriteLine("SkiaSharp.QrCode Playground WASM runtime initialized.");
