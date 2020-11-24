 ![dotnet-build](https://github.com/guitarrapc/SkiaSharp.QrCode/workflows/dotnet-build/badge.svg) ![release](https://github.com/guitarrapc/SkiaSharp.QrCode/workflows/release/badge.svg) [![codecov](https://codecov.io/gh/guitarrapc/SkiaSharp.QrCode/branch/master/graph/badge.svg?token=L5LHltghbd)](https://codecov.io/gh/guitarrapc/SkiaSharp.QrCode) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[![NuGet](https://img.shields.io/nuget/v/SkiaSharp.QrCode.svg?label=SkiaSharp%2EQrCode%20nuget)](https://www.nuget.org/packages/SkiaSharp.QrCode)

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

There are many System.Drawing samples to generate QRCode, and there are a lot of cases I want avoid System.Drawing for GDI+ issue. However, you may require many conding to generate QRCode using [ZXing.Net](https://github.com/micjahn/ZXing.Net) or [ImageSharp](https://github.com/SixLabors/ImageSharp) or [Core.Compat.System.Drawing](https://github.com/CoreCompat/System.Drawing).

I just want to create QR in much simpler way.

## Why Skia?

Performance and size and .NET Core support status.

> [.NET Core Image Processing](https://blogs.msdn.microsoft.com/dotnet/2017/01/19/net-core-image-processing/)

## Sample Code

Here's minimum sample to generate specific qrcode via args.

```csharp
using SkiaSharp;
using SkiaSharp.QrCode.Image;
using System;
using System.IO;

namespace SimpleGenerate
{
    class Program
    {
        static void Main(string[] args)
        {
            var content = "testtesttest";
            using (var output = new FileStream(@"output/hoge.png", FileMode.OpenOrCreate))
            {
                // generate QRCode
                var qrCode = new QrCode(content, new Vector2Slim(256, 256), SKEncodedImageFormat.Png);

                // output to file
                qrCode.GenerateImage(output);
            }
        }
    }
}

```

If you want specify detail, you can generate manually.

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