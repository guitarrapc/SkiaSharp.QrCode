# Downloads the pinned qrtool prebuilt release binary used as the second external
# encoder lineage for Micro QR (and later rMQR) fixture generation, and verifies
# it against the pinned SHA-256 before extraction.
#
# qrtool is a Rust CLI building on the qrcode crate: https://github.com/sorairolake/qrtool
# The checksum below is taken from the release's official sha256sums.txt and
# re-pinned here so a re-tagged release cannot silently change the oracle.
#
# Usage: pwsh tools/QrInteropFixtures/get-qrtool.ps1

$ErrorActionPreference = 'Stop'

$version = '0.13.2'
$asset = "qrtool-v$version-x86_64-pc-windows-msvc.zip"
$sha256 = 'c0cad331b96632e74da48f4e9d32e924cc5d3bd2be4258a4a0a39da99202d712'
$url = "https://github.com/sorairolake/qrtool/releases/download/v$version/$asset"

$targetDir = Join-Path $PSScriptRoot 'external/qrtool'
New-Item -ItemType Directory -Force $targetDir | Out-Null
$zipPath = Join-Path $targetDir $asset

if (-not (Test-Path $zipPath)) {
    Write-Host "downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zipPath
}

$actual = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToLowerInvariant()
if ($actual -ne $sha256) {
    Remove-Item $zipPath
    throw "SHA-256 mismatch for ${asset}: expected $sha256, got $actual. Download removed."
}

Expand-Archive -Path $zipPath -DestinationPath $targetDir -Force
$exe = Get-ChildItem -Path $targetDir -Recurse -Filter 'qrtool.exe' | Select-Object -First 1
& $exe.FullName --version
Write-Host "qrtool $version ready at $($exe.FullName)"
