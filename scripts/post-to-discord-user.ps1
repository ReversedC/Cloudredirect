param(
    [Parameter(Mandatory=$false)]
    [string]$UserToken = $env:DISCORD_USER_TOKEN,
    [string]$ChannelId = "1495014736515829760",
    [string]$Version,
    [string]$Changelog = "Latest release updates and improvements.",
    [string]$ExePath,
    [string]$GuideGifPath,
    [string]$PhoneGuideGifPath,
    [switch]$SkipFileUpload,
    [switch]$SkipGuides
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

# 3. Determine Exe and Guide Paths
if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot "..\ui\bin\publish\CloudRedirect.exe"
}

if (-not $GuideGifPath) {
    $candidateGuidePaths = @(
        (Join-Path $PSScriptRoot "..\assets\guides\guide.gif"),
        (Join-Path $PSScriptRoot "..\docs\guide.gif"),
        "C:\Users\admin\Downloads\guide\guide.gif"
    )
    foreach ($cand in $candidateGuidePaths) {
        if (Test-Path $cand) { $GuideGifPath = $cand; break }
    }
}

if (-not $PhoneGuideGifPath) {
    $candidatePhonePaths = @(
        (Join-Path $PSScriptRoot "..\assets\guides\setup_wizard_phone_copy_paste_guide.gif"),
        (Join-Path $PSScriptRoot "..\docs\setup_wizard_phone_copy_paste_guide.gif"),
        "C:\Users\admin\Downloads\guide\setup_wizard_phone_copy_paste_guide.gif"
    )
    foreach ($cand in $candidatePhonePaths) {
        if (Test-Path $cand) { $PhoneGuideGifPath = $cand; break }
    }
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

# 7. Build Pretty Discord Content (NO GitHub links or repo mentions)
$eCloud = [char]::ConvertFromUtf32(0x2601)  # ☁
$eClip  = [char]::ConvertFromUtf32(0x1F4CB) # 📋
$eBox   = [char]::ConvertFromUtf32(0x1F4E6) # 📦
$eBolt  = [char]::ConvertFromUtf32(0x26A1)  # ⚡
$eBook  = [char]::ConvertFromUtf32(0x1F4D6) # 📖
$eSpark = [char]::ConvertFromUtf32(0x2728)  # ✨
$ePhone = [char]::ConvertFromUtf32(0x1F4F1) # 📱

$cleanChangelog = ($Changelog -split "`r?`n" | Where-Object { $_.Trim() -ne "" } | ForEach-Object {
    $line = $_.Trim()
    if (-not $line.StartsWith("-") -and -not $line.StartsWith("*") -and -not $line.StartsWith(">") -and -not $line.StartsWith([char]0x2022)) {
        ([char]0x2022) + " " + $line
    } else {
        $line
    }
}) -join "`n"

if (-not $cleanChangelog) {
    $cleanChangelog = ([char]0x2022) + " General performance enhancements and stability improvements."
}

$titleLine = "# " + $eCloud + " CloudRedirect Companion ``v" + $Version + "``"
$caption = $titleLine + "`n*Steam Cloud save redirection & companion app*`n`n" + `
"### " + $eClip + " What's New`n>>> " + $cleanChangelog + "`n`n" + `
"### " + $eBox + " Package Details`n" + `
"> **Platform:** Windows x64`n" + `
"> **Distribution:** Standalone Single-File Executable"

if ($sha256) {
    $caption += "`n> **SHA-256:** ``" + $sha256 + "``"
}

# Add guide highlights if GIFs are present
$hasGuides = (-not $SkipGuides) -and (($GuideGifPath -and (Test-Path $GuideGifPath)) -or ($PhoneGuideGifPath -and (Test-Path $PhoneGuideGifPath)))
if ($hasGuides) {
    $caption += "`n`n### " + $eBook + " Visual Setup Guides (Auto-Playing Animations Below)"
    if ($GuideGifPath -and (Test-Path $GuideGifPath)) {
        $caption += "`n> " + $eSpark + " **Google Drive Setup:** See ``guide.gif`` walkthrough below."
    }
    if ($PhoneGuideGifPath -and (Test-Path $PhoneGuideGifPath)) {
        $caption += "`n> " + $ePhone + " **Phone Sign-In / QR Code:** See ``setup_wizard_phone_copy_paste_guide.gif`` below."
    }
}

$caption += "`n`n-# " + $eBolt + " Download the attached CloudRedirect.exe below to update."

$postUrl = "https://discord.com/api/v9/channels/$ChannelId/messages"

# 8. Check Attachments
$canAttachExe = (-not $SkipFileUpload) -and (Test-Path $ExePath)
if ($canAttachExe) {
    $fileInfo = Get-Item $ExePath
    if ($fileInfo.Length -gt 25MB) {
        Write-Host "File size ($([math]::Round($fileInfo.Length / 1MB, 2)) MB) exceeds 25 MB limit. Skipping exe attachment."
        $canAttachExe = $false
    }
}

$hasAttachments = $canAttachExe -or ($hasGuides)

$response = $null
if ($hasAttachments) {
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $fileIndex = 0

    # Part 1: payload_json
    $payloadObj = @{ content = $caption }
    $payloadJson = $payloadObj | ConvertTo-Json
    $jsonContent = [System.Net.Http.StringContent]::new($payloadJson, [System.Text.Encoding]::UTF8, "application/json")
    $multipart.Add($jsonContent, "payload_json")

    # Part 2: CloudRedirect.exe
    if ($canAttachExe) {
        Write-Host "Attaching CloudRedirect.exe ($([math]::Round((Get-Item $ExePath).Length / 1MB, 2)) MB)..."
        $fileBytes = [System.IO.File]::ReadAllBytes($ExePath)
        $fileContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
        $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
        $multipart.Add($fileContent, "files[$fileIndex]", "CloudRedirect.exe")
        $fileIndex++
    }

    # Part 3: guide.gif (Content-Type: image/gif for native Discord autoplay)
    if (-not $SkipGuides -and $GuideGifPath -and (Test-Path $GuideGifPath)) {
        Write-Host "Attaching guide.gif ($([math]::Round((Get-Item $GuideGifPath).Length / 1KB, 1)) KB)..."
        $guideBytes = [System.IO.File]::ReadAllBytes($GuideGifPath)
        $guideContent = [System.Net.Http.ByteArrayContent]::new($guideBytes)
        $guideContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("image/gif")
        $multipart.Add($guideContent, "files[$fileIndex]", "guide.gif")
        $fileIndex++
    }

    # Part 4: setup_wizard_phone_copy_paste_guide.gif (Content-Type: image/gif for native Discord autoplay)
    if (-not $SkipGuides -and $PhoneGuideGifPath -and (Test-Path $PhoneGuideGifPath)) {
        Write-Host "Attaching setup_wizard_phone_copy_paste_guide.gif ($([math]::Round((Get-Item $PhoneGuideGifPath).Length / 1KB, 1)) KB)..."
        $phoneBytes = [System.IO.File]::ReadAllBytes($PhoneGuideGifPath)
        $phoneContent = [System.Net.Http.ByteArrayContent]::new($phoneBytes)
        $phoneContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("image/gif")
        $multipart.Add($phoneContent, "files[$fileIndex]", "setup_wizard_phone_copy_paste_guide.gif")
        $fileIndex++
    }

    Write-Host "Uploading message with $fileIndex file attachment(s) to Discord..."
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
