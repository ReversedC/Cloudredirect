# Build and sign the universal GameHub Touch HUD Android APK
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "resources\mobile\gamehub_universal"

Write-Host "Building GameHub Mobile Touch HUD APK..."
python (Join-Path $projectDir "build.py")

$apkSource = Join-Path $repoRoot "resources\mobile\GameHub-TouchHUD.apk"
$apkDest = Join-Path $repoRoot "ui\bin\publish\GameHub-TouchHUD.apk"
Copy-Item $apkSource $apkDest -Force

Write-Host "GameHub-TouchHUD.apk built successfully and copied to ui/bin/publish!"
