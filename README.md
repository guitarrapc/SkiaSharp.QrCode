[![Build](https://github.com/guitarrapc/SkiaSharp.QrCode/actions/workflows/build.yaml/badge.svg)](https://github.com/guitarrapc/SkiaSharp.QrCode/actions/workflows/build.yaml)
[![release](https://github.com/guitarrapc/SkiaSharp.QrCode/actions/workflows/release.yaml/badge.svg)](https://github.com/guitarrapc/SkiaSharp.QrCode/actions/workflows/release.yaml)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/SkiaSharp.QrCode.svg?label=SkiaSharp%2EQrCode%20nuget)](https://www.nuget.org/packages/SkiaSharp.QrCode)

## Skia.QrCode

Qr Code generator with [Skia.Sharp](https://github.com/mono/SkiaSharp).

## Install

.NET CLI

```
$ dotnet add package SkiaSharp.QrCode
```

Package Manager

```
PM> Install-Package SkiaSharp.QrCode
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

var content = "testtesttest";
using var output = new FileStream(@"output/hoge.png", FileMode.OpenOrCreate);

// generate QRCode
var qrCode = new QrCode(content, new Vector2Slim(256, 256), SKEncodedImageFormat.Png);
// output to file
qrCode.GenerateImage(output);
```

If you want specify detail, you can generate manually.

```csharp
using SkiaQrCode;
using SkiaSharp;
using System;
using System.IO;

namespace SkiaQrCodeSampleConsole;

var content = "testtesttest";
using var generator = new QRCodeGenerator();

// Generate QrCode
var qr = generator.CreateQrCode(content, ECCLevel.L);

// Render to canvas
var info = new SKImageInfo(512, 512);
using var surface = SKSurface.Create(info);
var canvas = surface.Canvas;
canvas.Render(qr, info.Width, info.Height);

// Output to Stream -> File
using var image = surface.Snapshot();
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
using var stream = File.OpenWrite(@"output/hoge.png");
data.SaveTo(stream);
```

## TIPS

### Linux support

You have 2 choice to run on Linux. If you don't need font operation, use `SkiaSharp.NativeAssets.Linux.NoDependencies`.

1. Use `SkiaSharp.NativeAssets.Linux` package. In this case, you need to install `libfontconfig1` via apt or others.
1. Use `SkiaSharp.NativeAssets.Linux.NoDependencies` 2.80.2 or above. In this case, you don't need `libfontconfig1`.

SkiaSharp.NativeAssets.Linux.NoDependencies still can draw text, however can't search font cased on character or other fonts.

> Detail: https://github.com/mono/SkiaSharp/issues/964#issuecomment-549385484

**SkiaSharp.NativeAssets.Linux sample**

```shell
sudo apt update && apt install -y libfontconfig1
```

```csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SkiaSharp.QrCode" Version="0.5.0" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="2.80.2" />
  </ItemGroup>
</Project>
```

**SkiaSharp.NativeAssets.Linux.NoDependencies sample**

```csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SkiaSharp.QrCode" Version="0.5.0" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="2.80.2" />
  </ItemGroup>
</Project>
```

### Docker Build & Run

Test Build lib.

```shell
docker build -t skiasharp.qrcode .
```

Test Run on linux.

```shell
cd samples/LinuxRunSamples
docker-compose up
```

## License

MIT

## Thanks

> [aloisdeniel/Xam.Forms.QRCode](https://github.com/aloisdeniel/Xam.Forms.QRCode) : Qr Sample with Skia
> [codebude/QRCoder](https://github.com/codebude/QRCoder) : all QRCode generation algorithms
