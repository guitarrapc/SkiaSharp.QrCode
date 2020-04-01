#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

FROM mcr.microsoft.com/dotnet/core/runtime:3.1-nanoserver-1903 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-nanoserver-1903 AS build
WORKDIR /src
COPY ["sample/NanoServerGenerate.3.1/NanoServerGenerate.3.1.csproj", "sample/NanoServerGenerate.3.1/"]
RUN dotnet restore "sample/NanoServerGenerate.3.1/NanoServerGenerate.3.1.csproj"
COPY . .
WORKDIR "/src/sample/NanoServerGenerate.3.1"
RUN dotnet build "NanoServerGenerate.3.1.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NanoServerGenerate.3.1.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
CMD ["dotnet", "NanoServerGenerate.3.1.dll", "sample_message", "hoge.png"]