# scripts/build-apk.ps1 - Automated builder for SUO Link Android APK

$ErrorActionPreference = "Stop"

$SdkRoot = "C:\Users\admin\AppData\Local\Android\Sdk"
$BuildTools = "$SdkRoot\build-tools\34.0.0"
$AndroidJar = "$SdkRoot\platforms\android-34\android.jar"
$Aapt2 = "$BuildTools\aapt2.exe"
$Zipalign = "$BuildTools\zipalign.exe"
$D8 = "$BuildTools\d8.bat"
$ApkSigner = "$BuildTools\apksigner.bat"

$Javac = "C:\Program Files\Microsoft\jdk-21.0.12.7-hotspot\bin\javac.exe"
if (-not (Test-Path $Javac)) {
    $Javac = (Get-Command javac.exe -ErrorAction Stop).Source
}
$Keytool = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($Javac), "keytool.exe")
$Java = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($Javac), "java.exe")

$ProjectRoot = Resolve-Path "$PSScriptRoot\.."
$AndroidDir = "$ProjectRoot\android"
$BuildDir = "$AndroidDir\build"

Write-Host "=== Building SUO Link Android APK ===" -ForegroundColor Cyan

# 1. Clean & recreate build directories
if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
New-Item -ItemType Directory -Force -Path "$BuildDir\compiled_res", "$BuildDir\gen", "$BuildDir\classes", "$BuildDir\dex" | Out-Null

# 2. Compile Android Resources with AAPT2
Write-Host "[1/6] Compiling resources with aapt2..." -ForegroundColor Yellow
& $Aapt2 compile --dir "$AndroidDir\res" -o "$BuildDir\compiled_res\res.zip"
if ($LASTEXITCODE -ne 0) { throw "aapt2 compile failed" }

# 3. Link resources and generate R.java
Write-Host "[2/6] Linking resources..." -ForegroundColor Yellow
& $Aapt2 link -I $AndroidJar --manifest "$AndroidDir\AndroidManifest.xml" -o "$BuildDir\base.apk" --java "$BuildDir\gen" "$BuildDir\compiled_res\res.zip"
if ($LASTEXITCODE -ne 0) { throw "aapt2 link failed" }

# 4. Compile Java sources with javac
Write-Host "[3/6] Compiling Java sources with javac..." -ForegroundColor Yellow
$JavaFiles = Get-ChildItem -Recurse -Path "$BuildDir\gen", "$AndroidDir\src" -Filter "*.java" | ForEach-Object { $_.FullName }
& $Javac --release 8 -cp $AndroidJar -d "$BuildDir\classes" $JavaFiles
if ($LASTEXITCODE -ne 0) { throw "javac compilation failed" }

# 5. Convert Java bytecodes to classes.dex using D8
Write-Host "[4/6] Converting classes to DEX with d8..." -ForegroundColor Yellow
$ClassFiles = Get-ChildItem -Recurse -Path "$BuildDir\classes" -Filter "*.class" | ForEach-Object { $_.FullName }
& $D8 --lib $AndroidJar --min-api 24 --output "$BuildDir\dex" $ClassFiles
if ($LASTEXITCODE -ne 0) { throw "d8 compilation failed" }

# 6. Add classes.dex to base.apk using .NET ZipArchive
Write-Host "[5/6] Packing classes.dex into APK..." -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open("$BuildDir\base.apk", [System.IO.Compression.ZipArchiveMode]::Update)
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$BuildDir\dex\classes.dex", "classes.dex")
$zip.Dispose()

# 7. Zipalign the APK
Write-Host "[6/6] Zipaligning and Signing APK..." -ForegroundColor Yellow
$AlignedApk = "$BuildDir\SUO-Link-aligned.apk"
& $Zipalign -v -p 4 "$BuildDir\base.apk" $AlignedApk | Out-Null
if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }

# 8. Create debug keystore if needed & Sign APK
$Keystore = "$AndroidDir\debug.keystore"
if (-not (Test-Path $Keystore)) {
    Write-Host "Creating debug keystore..." -ForegroundColor Gray
    & $Keytool -genkeypair -v -keystore $Keystore -storepass android -alias androiddebugkey -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=SUO Link, OU=CloudRedirect, O=CloudRedirect, L=Global, S=Global, C=US" | Out-Null
}

$FinalApk = "$BuildDir\SUO-Link.apk"
& $ApkSigner sign --ks $Keystore --ks-pass pass:android --key-pass pass:android --out $FinalApk $AlignedApk
if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }

# 9. Copy to delivery locations
$Deliveries = @(
    "$ProjectRoot\ui\Resources",
    "$ProjectRoot\ui\bin\publish",
    "$env:LOCALAPPDATA\CloudRedirect"
)

foreach ($dest in $Deliveries) {
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Force -Path $dest | Out-Null }
    Copy-Item -Force $FinalApk "$dest\SUO-Link.apk"
}

$ApkSize = (Get-Item $FinalApk).Length / 1KB
Write-Host "`nSUCCESS: SUO Link APK built successfully! ($([math]::Round($ApkSize, 1)) KB)" -ForegroundColor Green
Write-Host "Artifacts copied to:"
foreach ($dest in $Deliveries) {
    Write-Host " -> $dest\SUO-Link.apk" -ForegroundColor DarkGray
}
