FROM microsoft/dotnet:2.2-sdk-bionic AS build
WORKDIR /app

# restore
COPY src/SkiaSharp.QrCode/*.csproj ./
RUN dotnet restore

# build
COPY src/SkiaSharp.QrCode/. /app
RUN dotnet build -c Release
RUN dotnet publish -c Release

# pack
WORKDIR /app
VOLUME /app/pack
CMD ["dotnet", "pack", "--include-symbols", "-c", "Release", "-o", "pack"]