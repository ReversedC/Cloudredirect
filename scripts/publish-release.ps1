param(
    [string]$ReleaseBody = "Automated release of CloudRedirect"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

# 1. Determine Token
$token = $env:GITHUB_TOKEN
if (-not $token) {
    $remote = git remote get-url origin
    if ($remote -match 'ghp_[a-zA-Z0-9]+') {
        $token = $Matches[0]
    }
}

if (-not $token) {
    Write-Error "GitHub token not found in GITHUB_TOKEN environment variable or git remote URL."
    exit 1
}

# 2. Determine Repo Owner and Name
$repoOwner = "mirzaarsyad74-cmyk"
$repoName = "Cloudredirect"

# 3. Read Version
$versionPropsPath = Join-Path $PSScriptRoot "..\Version.props"
[xml]$xml = Get-Content $versionPropsPath
$version = $xml.Project.PropertyGroup.ReleaseVersion.Trim()
$tagName = "v$version"
Write-Host "Target Release Version: $version ($tagName)"

# 4. Check executable path
$exePath = Join-Path $PSScriptRoot "..\ui\bin\publish\CloudRedirect.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "CloudRedirect.exe not found at $exePath. Please run dotnet publish first."
    exit 1
}

# 5. Compute SHA256
$hasher = [System.Security.Cryptography.SHA256]::Create()
$fileStream = [System.IO.File]::OpenRead($exePath)
$hashBytes = $hasher.ComputeHash($fileStream)
$fileStream.Close()
$sha256 = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()

$shaPath = "$exePath.sha256"
[System.IO.File]::WriteAllText($shaPath, $sha256)
Write-Host "Computed SHA256: $sha256"

# 6. GitHub API Headers
$headers = @{
    "Authorization" = "token $token"
    "User-Agent"    = "CloudRedirect-ReleaseScript"
    "Accept"        = "application/vnd.github.v3+json"
}

# 7. Check if release already exists
$releaseUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/tags/$tagName"
$release = $null
try {
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -Method Get
    Write-Host "Found existing release for $tagName (ID: $($release.id))"
} catch {
    Write-Host "No existing release for $tagName. Creating new release..."
}

if (-not $release) {
    $createUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases"
    $payload = @{
        tag_name         = $tagName
        target_commitish = "master"
        name             = "CloudRedirect $tagName"
        body             = "$ReleaseBody ($tagName)"
        draft            = $false
        prerelease       = $false
    } | ConvertTo-Json

    $release = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $payload -ContentType "application/json"
    Write-Host "Created release $tagName (ID: $($release.id))"
}

# 8. Upload Assets
$releaseId = $release.id

# Delete existing assets if they exist
$existingAssetsUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/$releaseId/assets"
$currentAssets = Invoke-RestMethod -Uri $existingAssetsUrl -Headers $headers -Method Get
foreach ($asset in $currentAssets) {
    if ($asset.name -eq "CloudRedirect.exe" -or $asset.name -eq "CloudRedirect.exe.sha256" -or $asset.name -eq "CloudRedirect-Setup.exe" -or $asset.name -eq "CloudRedirect-Stream.apk" -or $asset.name -eq "GameHub-TouchHUD.apk") {
        Write-Host "Deleting old asset: $($asset.name)..."
        Invoke-RestMethod -Uri $asset.url -Headers $headers -Method Delete
    }
}

# Function to upload file using .NET HttpClient for binary safety
function Upload-Asset($filePath, $assetName, $contentType) {
    Write-Host "Uploading $assetName ($contentType)..."
    $uploadUri = "https://uploads.github.com/repos/$repoOwner/$repoName/releases/$releaseId/assets?name=$assetName"
    
    $bytes = [System.IO.File]::ReadAllBytes($filePath)
    
    $httpClient = [System.Net.Http.HttpClient]::new()
    $httpClient.DefaultRequestHeaders.Add("Authorization", "token $token")
    $httpClient.DefaultRequestHeaders.Add("User-Agent", "CloudRedirect-ReleaseScript")
    
    $content = [System.Net.Http.ByteArrayContent]::new($bytes)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($contentType)
    
    $response = $httpClient.PostAsync($uploadUri, $content).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) {
        $errBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Write-Error "Failed to upload $assetName. Status: $($response.StatusCode). Details: $errBody"
        exit 1
    }
    Write-Host "Successfully uploaded $assetName!"
}

Upload-Asset $exePath "CloudRedirect.exe" "application/octet-stream"
Upload-Asset $shaPath "CloudRedirect.exe.sha256" "text/plain"

$apkPath = Join-Path $PSScriptRoot "..\ui\bin\publish\CloudRedirect-Stream.apk"
if (Test-Path $apkPath) {
    Upload-Asset $apkPath "CloudRedirect-Stream.apk" "application/vnd.android.package-archive"
}

$gamehubApkPath = Join-Path $PSScriptRoot "..\ui\bin\publish\GameHub-TouchHUD.apk"
if (Test-Path $gamehubApkPath) {
    Upload-Asset $gamehubApkPath "GameHub-TouchHUD.apk" "application/vnd.android.package-archive"
}


Write-Host "Release $tagName published successfully with assets!"
