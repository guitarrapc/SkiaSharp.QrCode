FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# build
COPY src/SkiaSharp.QrCode/. /src/SkiaSharp.QrCode
COPY LICENSE.md /

WORKDIR /src/SkiaSharp.QrCode
RUN dotnet restore
RUN dotnet build -c Release -f netstandard2.1
RUN dotnet publish -c Release -f netstandard2.1
RUN dotnet publish -c Release -f netstandard2.0

# pack
VOLUME /src/SkiaSharp.QrCode/pack
CMD ["dotnet", "pack", "--include-symbols", "-c", "Release", "-o", "pack"]
