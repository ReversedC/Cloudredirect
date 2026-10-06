param(
    [Parameter(Mandatory=$false)]
    [string]$UserToken = $env:DISCORD_USER_TOKEN,
    [string]$ChannelId = "1495014736515829760",
    [string]$Version,
    [string]$Changelog = "Latest release updates and improvements.",
    [string]$ExePath,
    [switch]$SkipFileUpload
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

# 1. Determine User Token
if (-not $UserToken) {
    $savedTokenFile = Join-Path $PSScriptRoot ".discord_user_token"
    if (Test-Path $savedTokenFile) {
        $UserToken = (Get-Content $savedTokenFile -ErrorAction SilentlyContinue).Trim()
    }
}

if (-not $UserToken) {
    Write-Host "Discord user token not provided."
    Write-Host "Usage: .\post-to-discord-user.ps1 -UserToken '<YOUR_TOKEN>' [-Changelog '<notes>']"
    Write-Host "Or save it once in scripts/.discord_user_token or DISCORD_USER_TOKEN environment variable."
    exit 1
}

# 2. Determine Version
if (-not $Version) {
    $versionPropsPath = Join-Path $PSScriptRoot "..\Version.props"
    if (Test-Path $versionPropsPath) {
        [xml]$xml = Get-Content $versionPropsPath
        $Version = $xml.Project.PropertyGroup.ReleaseVersion.Trim()
        if ($xml.Project.PropertyGroup.ReleasePrerelease) {
            $Version += $xml.Project.PropertyGroup.ReleasePrerelease.Trim()
        }
    } else {
        $Version = "Latest"
    }
}

# 3. Determine Exe Path
if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot "..\ui\bin\publish\CloudRedirect.exe"
}

# 4. Compute SHA256 if Exe exists
$sha256 = ""
if (Test-Path $ExePath) {
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($ExePath)
    $hashBytes = $hasher.ComputeHash($stream)
    $stream.Close()
    $sha256 = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()
}

$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.DefaultRequestHeaders.Add("Authorization", $UserToken)
$httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")

# 5. Fetch @me to get the user's ID
Write-Host "Authenticating with Discord API..."
$meResp = $httpClient.GetAsync("https://discord.com/api/v9/users/@me").GetAwaiter().GetResult()
if (-not $meResp.IsSuccessStatusCode) {
    $err = $meResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Error "Invalid User Token or unauthorized. Status: $($meResp.StatusCode). Details: $err"
    exit 1
}
$meJson = $meResp.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
$myId = $meJson.id
$myUsername = $meJson.username
Write-Host "Logged in as: $myUsername ($myId)"

# 6. Look for previous release messages by this user in the channel and delete them
Write-Host "Searching channel $ChannelId for previous release posts..."
$msgsResp = $httpClient.GetAsync("https://discord.com/api/v9/channels/$ChannelId/messages?limit=25").GetAwaiter().GetResult()
if ($msgsResp.IsSuccessStatusCode) {
    $msgsJson = $msgsResp.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $oldPosts = $msgsJson | Where-Object { 
        $_.author.id -eq $myId -and ($_.content -match "CloudRedirect" -or $_.attachments.Count -gt 0)
    }

    foreach ($old in $oldPosts) {
        Write-Host "Deleting old message ID $($old.id)..."
        $delResp = $httpClient.DeleteAsync("https://discord.com/api/v9/channels/$ChannelId/messages/$($old.id)").GetAwaiter().GetResult()
        if ($delResp.IsSuccessStatusCode) {
            Write-Host "Deleted old post successfully."
        }
        Start-Sleep -Milliseconds 500
    }
} else {
    Write-Host "Could not list messages (Status: $($msgsResp.StatusCode)). Skipping deletion."
}

# 7. Build Message Content
$releaseUrl = "https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/tag/v$Version"
$downloadUrl = "https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/download/v$Version/CloudRedirect.exe"

$caption = @"
🚀 **CloudRedirect v$Version is now available!**

📋 **Changelog:**
$Changelog

🔗 **GitHub Release:** $releaseUrl
📥 **Direct Download:** $downloadUrl
"@

if ($sha256) {
    $caption += "`n" + "**SHA-256:** " + $sha256
}

$postUrl = "https://discord.com/api/v9/channels/$ChannelId/messages"

# 8. Check if we should attach CloudRedirect.exe directly
$canAttach = (-not $SkipFileUpload) -and (Test-Path $ExePath)
if ($canAttach) {
    $fileInfo = Get-Item $ExePath
    if ($fileInfo.Length -gt 25MB) {
        Write-Host "File size ($([math]::Round($fileInfo.Length / 1MB, 2)) MB) exceeds 25 MB limit. Skipping file attachment."
        $canAttach = $false
    }
}

$response = $null
if ($canAttach) {
    Write-Host "Uploading CloudRedirect.exe ($([math]::Round($fileInfo.Length / 1MB, 2)) MB) to Discord channel..."
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()

    # Part 1: payload_json
    $payloadObj = @{ content = $caption }
    $payloadJson = $payloadObj | ConvertTo-Json
    $jsonContent = [System.Net.Http.StringContent]::new($payloadJson, [System.Text.Encoding]::UTF8, "application/json")
    $multipart.Add($jsonContent, "payload_json")

    # Part 2: file
    $fileBytes = [System.IO.File]::ReadAllBytes($ExePath)
    $fileContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
    $multipart.Add($fileContent, "files[0]", "CloudRedirect.exe")

    $response = $httpClient.PostAsync($postUrl, $multipart).GetAwaiter().GetResult()
} else {
    Write-Host "Posting message to Discord channel..."
    $payloadObj = @{ content = $caption }
    $payloadJson = $payloadObj | ConvertTo-Json
    $jsonContent = [System.Net.Http.StringContent]::new($payloadJson, [System.Text.Encoding]::UTF8, "application/json")
    $response = $httpClient.PostAsync($postUrl, $jsonContent).GetAwaiter().GetResult()
}

if (-not $response.IsSuccessStatusCode) {
    $err = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Error "Failed to post message. Status: $($response.StatusCode). Error: $err"
    exit 1
}

$respBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
$postedMsg = $respBody | ConvertFrom-Json
Write-Host "Successfully posted CloudRedirect v$Version to Discord! (Message ID: $($postedMsg.id))"
