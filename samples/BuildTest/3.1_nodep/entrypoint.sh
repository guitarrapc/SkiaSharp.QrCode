#!/bin/bash -ex

FRAMEWORK=netcoreapp3.1
while [ $# -gt 0 ]; do
    case $1 in
        -f) FRAMEWORK=$2; shift 2; ;;
        *) shift ;;
    esac
done

dotnet run --csproj BuildTest.csproj -f "${FRAMEWORK}"
