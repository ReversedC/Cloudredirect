param(
    [string]$WebhookUrl = $env:DISCORD_WEBHOOK_URL,
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

# 1. Check Webhook URL
if (-not $WebhookUrl) {
    # Check if saved in scripts/.discord_webhook_url
    $savedWebhookFile = Join-Path $PSScriptRoot ".discord_webhook_url"
    if (Test-Path $savedWebhookFile) {
        $WebhookUrl = (Get-Content $savedWebhookFile).Trim()
    }
}

if (-not $WebhookUrl) {
    Write-Host "Discord webhook URL not provided."
    Write-Host "Usage: .\post-to-discord.ps1 -WebhookUrl '<URL>' [-Changelog '<notes>']"
    Write-Host "Or save it once in scripts/.discord_webhook_url or DISCORD_WEBHOOK_URL environment variable."
    exit 0
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

# 5. Optional: Try to delete previous message if we saved its ID
$lastMsgFile = Join-Path $PSScriptRoot ".last_discord_message_id"
if (Test-Path $lastMsgFile) {
    $lastMsgId = (Get-Content $lastMsgFile -ErrorAction SilentlyContinue).Trim()
    if ($lastMsgId) {
        try {
            Write-Host "Checking for previous message ($lastMsgId)..."
            # Strip query params from webhook url if present
            $baseWebhook = $WebhookUrl.Split('?')[0]
            $deleteUrl = "$baseWebhook/messages/$lastMsgId"
            $delResp = $httpClient.DeleteAsync($deleteUrl).GetAwaiter().GetResult()
            if ($delResp.IsSuccessStatusCode) {
                Write-Host "Old Discord release message ($lastMsgId) deleted successfully."
            } else {
                Write-Host "Old message could not be deleted (status: $($delResp.StatusCode)). It may have been manually deleted."
            }
        } catch {
            Write-Host "Could not delete old message. Continuing..."
        }
    }
}

# 6. Build Discord Message Payload
$embed = @{
    title       = "CloudRedirect v$Version is now available!"
    description = "**Changelog:**`n$Changelog"
    color       = 1752220 # Steam cyan/teal accent
    fields      = @(
        @{
            name   = "Version"
            value  = "v$Version"
            inline = $true
        },
        @{
            name   = "Platform"
            value  = "Windows x64"
            inline = $true
        }
    )
    footer      = @{
        text = "CloudRedirect • Steam Cloud save redirection & companion"
    }
    timestamp   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}

if ($sha256) {
    $embed.fields += @{
        name   = "SHA-256 Checksum"
        value  = ('`' + $sha256 + '`')
        inline = $false
    }
}

$hasGuides = (-not $SkipGuides) -and (($GuideGifPath -and (Test-Path $GuideGifPath)) -or ($PhoneGuideGifPath -and (Test-Path $PhoneGuideGifPath)))
if ($hasGuides) {
    $bullet = [char]::ConvertFromUtf32(0x2022)
    $guideDesc = ""
    if ($GuideGifPath -and (Test-Path $GuideGifPath)) {
        $guideDesc += "$bullet **Google Drive Sync:** See ``guide.gif`` walkthrough attached below.`n"
    }
    if ($PhoneGuideGifPath -and (Test-Path $PhoneGuideGifPath)) {
        $guideDesc += "$bullet **Phone Sign-In / QR Code:** See ``setup_wizard_phone_copy_paste_guide.gif`` below.`n"
    }
    $embed.fields += @{
        name   = "Animated Setup Guides"
        value  = $guideDesc.TrimEnd()
        inline = $false
    }
}

$payloadObj = @{
    content = "🚀 **CloudRedirect v$Version is now available!**"
    embeds  = @($embed)
}

$payloadJson = $payloadObj | ConvertTo-Json -Depth 5

# Ensure ?wait=true is attached so Discord returns message object with id
$postUrl = $WebhookUrl.Split('?')[0] + "?wait=true"

# 7. Send Request (Multipart if attaching Exe or Guides, JSON otherwise)
$canAttachExe = (-not $SkipFileUpload) -and (Test-Path $ExePath)
if ($canAttachExe) {
    $fileInfo = Get-Item $ExePath
    # Discord file upload limit check (25MB)
    if ($fileInfo.Length -gt 25MB) {
        Write-Host "File size ($([math]::Round($fileInfo.Length / 1MB, 2)) MB) exceeds Discord limit (25 MB). Skipping direct attachment."
        $canAttachExe = $false
    }
}

$hasAttachments = $canAttachExe -or ($hasGuides)

$response = $null
if ($hasAttachments) {
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $fileIndex = 0

    # Part 1: payload_json
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

    Write-Host "Uploading announcement with $fileIndex file attachment(s) to Discord via Webhook..."
    $response = $httpClient.PostAsync($postUrl, $multipart).GetAwaiter().GetResult()
} else {
    Write-Host "Posting announcement to Discord via Webhook..."
    $jsonContent = [System.Net.Http.StringContent]::new($payloadJson, [System.Text.Encoding]::UTF8, "application/json")
    $response = $httpClient.PostAsync($postUrl, $jsonContent).GetAwaiter().GetResult()
}

if (-not $response.IsSuccessStatusCode) {
    $err = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Error "Failed to post to Discord. Status: $($response.StatusCode). Error: $err"
    exit 1
}

$resBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
$postedMsg = $resBody | ConvertFrom-Json
if ($postedMsg.id) {
    Set-Content -Path $lastMsgFile -Value $postedMsg.id
    Write-Host "Successfully posted to Discord! (Message ID: $($postedMsg.id))"
} else {
    Write-Host "Successfully posted to Discord!"
}
