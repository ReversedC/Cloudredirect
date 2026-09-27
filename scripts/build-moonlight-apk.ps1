# Script to compile Java sources and rebuild the customized Steam-themed Moonlight APK
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot "resources\mobile\src\com\cloudredirect"
$publishDir = Join-Path $repoRoot "ui\bin\publish"

Write-Host "Rebuilding CloudRedirect Stream APK from $sourceDir..."

# JDK and Android SDK paths
$javac = "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\bin\javac.exe"
$jar = "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\bin\jar.exe"
$java = "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\bin\java.exe"
$d8 = "C:\Users\admin\AppData\Local\Android\Sdk\build-tools\35.0.0\d8.bat"
$zipalign = "C:\Users\admin\AppData\Local\Android\Sdk\build-tools\35.0.0\zipalign.exe"
$apksigner = "C:\Users\admin\AppData\Local\Android\Sdk\build-tools\35.0.0\apksigner.bat"
$androidJar = "C:\Users\admin\AppData\Local\Android\Sdk\platforms\android-34\android.jar"

Write-Host "Build script ready."
