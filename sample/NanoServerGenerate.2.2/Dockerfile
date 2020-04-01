#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

FROM mcr.microsoft.com/dotnet/core/runtime:2.2-nanoserver-1903 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:2.2-nanoserver-1903 AS build
WORKDIR /src
COPY ["sample/NanoServerGenerate.2.2/NanoServerGenerate.2.2.csproj", "sample/NanoServerGenerate.2.2/"]
RUN dotnet restore "sample/NanoServerGenerate.2.2/NanoServerGenerate.2.2.csproj"
COPY . .
WORKDIR "/src/sample/NanoServerGenerate.2.2"
RUN dotnet build "NanoServerGenerate.2.2.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NanoServerGenerate.2.2.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
CMD ["dotnet", "NanoServerGenerate.2.2.dll", "sample_message", "hoge.png"]