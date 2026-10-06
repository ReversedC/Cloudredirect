param(
    [string]$WebhookUrl = $env:DISCORD_WEBHOOK_URL,
    [string]$Version,
    [string]$Changelog = "Latest release updates and improvements.",
    [string]$ExePath,
    [switch]$SkipFileUpload
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
        value  = "`$sha256`"
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

# 7. Send Request (Multipart if attaching Exe, JSON otherwise)
$canAttach = (-not $SkipFileUpload) -and (Test-Path $ExePath)
if ($canAttach) {
    $fileInfo = Get-Item $ExePath
    # Discord file upload limit check (25MB)
    if ($fileInfo.Length -gt 25MB) {
        Write-Host "File size ($([math]::Round($fileInfo.Length / 1MB, 2)) MB) exceeds Discord limit (25 MB). Skipping direct attachment."
        $canAttach = $false
    }
}

$response = $null
if ($canAttach) {
    Write-Host "Uploading CloudRedirect.exe ($([math]::Round($fileInfo.Length / 1MB, 2)) MB) and changelog to Discord via Webhook..."
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()

    # Part 1: payload_json
    $jsonContent = [System.Net.Http.StringContent]::new($payloadJson, [System.Text.Encoding]::UTF8, "application/json")
    $multipart.Add($jsonContent, "payload_json")

    # Part 2: file
    $fileBytes = [System.IO.File]::ReadAllBytes($ExePath)
    $fileContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
    $multipart.Add($fileContent, "files[0]", "CloudRedirect.exe")

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
