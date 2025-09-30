FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# build
COPY src/SkiaSharp.QrCode/. /src/SkiaSharp.QrCode
COPY Directory.Build.props /
COPY Directory.Packages.props /
COPY opensource.snk /
COPY LICENSE /
COPY README.md /

WORKDIR /src/SkiaSharp.QrCode
RUN dotnet restore
RUN dotnet build -c Release

# pack
VOLUME /src/SkiaSharp.QrCode/pack
CMD ["dotnet", "pack", "--include-symbols", "-c", "Release", "-o", "/pack/"]
