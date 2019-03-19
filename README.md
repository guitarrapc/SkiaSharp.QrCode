## Skia.QrCode

Qr Code generator with [Skia.Sharp](https://github.com/mono/SkiaSharp).

## Install

.NET CLI

```
$ dotnet add package SkiaQrCode
```

Package Manager

```
PM> Install-Pacakge Skia.QrCode
```

## Motivation

There are many ZXing.Net + System.Drawing samples to generate Qr.
If you want avoid System.Drawing, you may use [ImageSharp](https://github.com/SixLabors/ImageSharp) or [Core.Compat.System.Drawing](https://github.com/CoreCompat/System.Drawing).

However using these code required much coding, I just want to create QR!

## Why Skia?

> [.NET Core Image Processing](https://blogs.msdn.microsoft.com/dotnet/2017/01/19/net-core-image-processing/)

## Sample Code

```csharp
using SkiaQrCode;
using SkiaSharp;
using System;
using System.IO;

namespace SkiaQrCodeSampleConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            var content = "testtesttest";
            using (var generator = new QRCodeGenerator())
            {
                // Generate QrCode
                var qr = generator.CreateQrCode(content, ECCLevel.L);

                // Render to canvas
                var info = new SKImageInfo(512, 512);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Render(qr, info.Width, info.Height);

                    // Output to Stream -> File
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (var stream = File.OpenWrite(@"output/hoge.png"))
                    {
                        data.SaveTo(stream);
                    }
                }
            }
        }
    }
}

```

## Build

```
docker build -t skiasharp.qrcode .
```

## License

MIT

## Thanks

> [aloisdeniel/Xam.Forms.QRCode](https://github.com/aloisdeniel/Xam.Forms.QRCode) : Qr Sample with Skia
> [codebude/QRCoder](https://github.com/codebude/QRCoder) : all QRCode generation algorithms