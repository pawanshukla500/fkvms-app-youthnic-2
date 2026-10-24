# Publishes both projects into installer/publish for the Inno Setup script.
# Run from the repo root:  powershell -File installer/publish.ps1
$ErrorActionPreference = 'Stop'
$root  = Split-Path $PSScriptRoot -Parent
$out   = Join-Path $PSScriptRoot 'publish'
$desktopOut = Join-Path $out 'Desktop'
$apiOut     = Join-Path $out 'LookupApi'

foreach ($d in @($desktopOut, $apiOut)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

Write-Host 'Publishing Youthnic.Desktop (self-contained win-x64)...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src/Youthnic.Desktop/Youthnic.Desktop.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $desktopOut

Write-Host 'Publishing Youthnic.LookupApi (self-contained win-x64)...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src/Youthnic.LookupApi/Youthnic.LookupApi.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $apiOut

# The lookup API reads its configuration from environment variables (or a .env
# next to the exe in dev). Never copy a real .env into the publish output.
Write-Host "Publish complete -> $out" -ForegroundColor Green
