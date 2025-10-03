#!/bin/bash
set -ex

apt update && apt install -y libfontconfig1
dotnet run --csproj BuildTest.csproj
