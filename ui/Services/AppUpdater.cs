using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Checks GitHub for a newer release and, if found, downloads the .exe asset,
/// validates it, swaps the running executable, and relaunches.
/// </summary>
internal static class AppUpdater
{
    private const string RepoOwner = "mirzaarsyad74-cmyk";
    private const string RepoName = "Cloudredirect";
    // Uses /releases (not /releases/latest) so prerelease/draft flags and tag
    // suffixes can be filtered client-side.
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";

    private static readonly string[] PrereleaseTagSuffixes =
        { "-test", "-pre", "-rc", "-beta", "-alpha" };

    public static string GetCurrentVersionString()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
    }

    internal static bool IsBetaTag(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return false;
        var tag = tagName.Trim();
        if (tag.EndsWith("B", StringComparison.OrdinalIgnoreCase))
            return true;
        if (tag.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains(".beta", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    internal static bool IsPrereleaseTag(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return false;
        if (IsBetaTag(tagName)) return true;
        foreach (var suffix in PrereleaseTagSuffixes)
        {
            if (tagName.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static bool TryParseVersion(string versionStr, out Version baseVer, out bool isBeta)
    {
        baseVer = new Version(0, 0, 0);
        isBeta = false;
        if (string.IsNullOrWhiteSpace(versionStr)) return false;

        var s = versionStr.Trim().TrimStart('v');
        if (s.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            isBeta = true;
            s = s.Substring(0, s.Length - 1).Trim();
        }
        else if (s.Contains("-beta", StringComparison.OrdinalIgnoreCase))
        {
            isBeta = true;
            s = s.Replace("-beta", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        else if (s.Contains("-rc", StringComparison.OrdinalIgnoreCase))
        {
            isBeta = true;
            s = s.Replace("-rc", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        if (Version.TryParse(s, out var v))
        {
            baseVer = v;
            return true;
        }
        return false;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static AppUpdater()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("CloudRedirect-AutoUpdate");
    }

    /// <summary>
    /// Result of checking for an update.
    /// </summary>
    internal sealed class CheckResult
    {
        public bool UpdateAvailable { get; init; }
        public string? TagName { get; init; }
        public string? DownloadUrl { get; init; }
        public string? AssetName { get; init; }
        /// <summary>Release body (markdown changelog) from GitHub.</summary>
        public string? Body { get; init; }
        /// <summary>URL to the GitHub release page.</summary>
        public string? HtmlUrl { get; init; }
    }

    /// <summary>
    /// Checks GitHub releases for a newer version based on the active branch (Main vs Beta).
    /// </summary>
    internal static async Task<CheckResult?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
            var releases = doc.RootElement;

            if (releases.GetArrayLength() == 0) return null;

            var localVerStr = GetCurrentVersionString();
            TryParseVersion(localVerStr, out var localBaseVer, out bool localIsBeta);
            App.LogStartup($"AppUpdater.CheckAsync: localVerStr={localVerStr}, localBaseVer={localBaseVer}, localIsBeta={localIsBeta}");

            var targetBranch = AppSettings.UpdateBranch; // "main" or "beta"
            bool wantBeta = string.Equals(targetBranch, "beta", StringComparison.OrdinalIgnoreCase);

            JsonElement root = default;
            string tagName = "";
            Version? remoteBaseVersion = null;
            bool remoteIsBeta = false;
            bool foundCandidate = false;

            // 1. Search for best release candidate for the chosen branch
            foreach (var rel in releases.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
                    continue;

                var candidateTag = rel.GetProperty("tag_name").GetString() ?? "";
                bool isPrereleaseRel = rel.TryGetProperty("prerelease", out var prProp) && prProp.ValueKind == JsonValueKind.True;

                if (!TryParseVersion(candidateTag, out var candBaseVer, out bool candIsBeta))
                    continue;

                candIsBeta = candIsBeta || isPrereleaseRel || IsBetaTag(candidateTag);

                if (wantBeta)
                {
                    // Beta channel: look for beta releases (ending with B or marked prerelease)
                    if (!candIsBeta) continue;
                }
                else
                {
                    // Main channel: only accept stable non-beta releases
                    if (candIsBeta || IsPrereleaseTag(candidateTag)) continue;
                }

                root = rel;
                tagName = candidateTag;
                remoteBaseVersion = candBaseVer;
                remoteIsBeta = candIsBeta;
                foundCandidate = true;
                break;
            }

            // Fallback for Beta channel: if no beta release exists yet, use the latest stable release
            if (!foundCandidate && wantBeta)
            {
                foreach (var rel in releases.EnumerateArray())
                {
                    if (rel.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
                        continue;
                    var candidateTag = rel.GetProperty("tag_name").GetString() ?? "";
                    if (!TryParseVersion(candidateTag, out var candBaseVer, out bool candIsBeta))
                        continue;
                    root = rel;
                    tagName = candidateTag;
                    remoteBaseVersion = candBaseVer;
                    remoteIsBeta = candIsBeta;
                    foundCandidate = true;
                    break;
                }
            }

            if (!foundCandidate || remoteBaseVersion == null) return null;

            // Find the GUI exe asset and hash file (exact match to avoid grabbing the CLI exe)
            if (!root.TryGetProperty("assets", out var assets))
                return null;

            string? downloadUrl = null;
            string? assetName = null;
            string? sha256Url = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals("CloudRedirect.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                }
                else if (name.Equals("CloudRedirect.exe.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    sha256Url = asset.GetProperty("browser_download_url").GetString();
                }
            }

            if (downloadUrl == null) return null;

            // Compare local exe hash against published hash; skip if exact match
            var localExePath = GetAppExecutablePath();
            bool hashIdentical = false;
            if (sha256Url != null && !string.IsNullOrEmpty(localExePath) && File.Exists(localExePath))
            {
                try
                {
                    var remoteHash = (await Http.GetStringAsync(sha256Url)).Trim();
                    if (remoteHash.Length == 64)
                    {
                        var localHash = ComputeFileSHA256(localExePath);
                        if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                            hashIdentical = true;
                    }
                }
                catch { /* hash check failed, fall through to version comparison */ }
            }

            if (hashIdentical)
                return new CheckResult { UpdateAvailable = false };

            bool updateAvailable;
            if (wantBeta)
            {
                // If user is currently running a main build, switching to beta is an update
                if (!localIsBeta)
                {
                    updateAvailable = true;
                }
                else
                {
                    // Both local and remote are beta
                    updateAvailable = remoteBaseVersion > localBaseVer;
                }
            }
            else
            {
                // On Main channel:
                // If user is currently running a beta build, switching to main is an update
                if (localIsBeta)
                {
                    updateAvailable = true;
                }
                else
                {
                    // Both local and remote are main
                    updateAvailable = remoteBaseVersion > localBaseVer;
                }
            }

            App.LogStartup($"AppUpdater.CheckAsync: remoteBaseVersion={remoteBaseVersion}, localBaseVer={localBaseVer}, wantBeta={wantBeta}, updateAvailable={updateAvailable}");

            if (!updateAvailable)
                return new CheckResult { UpdateAvailable = false };

            var body = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? ""
                : "";
            var htmlUrl = root.TryGetProperty("html_url", out var htmlProp)
                ? htmlProp.GetString()
                : null;

            return new CheckResult
            {
                UpdateAvailable = true,
                TagName = tagName,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                Body = body,
                HtmlUrl = htmlUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeFileSHA256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Hard caps on a downloaded update payload. Self-contained single-file with bundled
    // .NET 8 desktop runtime is ~75 MB; 150 MB leaves ample headroom while ensuring
    // a hostile or corrupted response cannot exhaust memory or disk before validation rejects it.
    private const long MinUpdateBytes = 1L * 1024 * 1024;
    private const long MaxUpdateBytes = 150L * 1024 * 1024;

    /// <summary>
    /// Downloads the update, validates it, swaps the running exe, and relaunches.
    /// Returns an error message on failure, or null on success (the process will exit).
    /// <paramref name="onProgress"/> receives values 0-100 for download progress, or -1 for non-download steps.
    /// </summary>
    internal static async Task<string?> DownloadAndApplyAsync(string downloadUrl, Action<int, string>? onProgress = null)
    {
        App.LogStartup($"DownloadAndApplyAsync started for: {downloadUrl}");
        var tempPath = Path.Combine(Path.GetTempPath(), $"CloudRedirect_{Guid.NewGuid():N}.exe");
        try
        {
            onProgress?.Invoke(0, "Downloading update...");

            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            // Reject obviously bogus Content-Length up front so we never start writing
            // a 4 GB "update" to a temp file before discovering it.
            if (totalBytes > MaxUpdateBytes)
                return $"Downloaded file has suspicious size ({totalBytes} bytes)";

            // Stream to disk with a bounded buffer; cap running total even when the
            // server omitted or lied about Content-Length.
            using var stream = await response.Content.ReadAsStreamAsync();
            long bytesRead = 0;
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    bytesRead += read;
                    if (bytesRead > MaxUpdateBytes)
                        return $"Downloaded file has suspicious size (>{MaxUpdateBytes} bytes)";
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    if (totalBytes > 0)
                    {
                        var pct = (int)(bytesRead * 100 / totalBytes);
                        onProgress?.Invoke(pct, $"Downloading... {pct}%");
                    }
                }
            }

            // Validate: size between 1 MB and 50 MB (framework-dependent single-file ~8 MB)
            if (bytesRead < MinUpdateBytes || bytesRead > MaxUpdateBytes)
                return $"Downloaded file has suspicious size ({bytesRead} bytes)";

            // Validate: MZ header (PE executable). Read just the first two bytes back
            // from the temp file rather than holding the whole payload in memory.
            byte[] mz = new byte[2];
            await using (var verify = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (await verify.ReadAsync(mz.AsMemory(0, 2)) < 2 || mz[0] != (byte)'M' || mz[1] != (byte)'Z')
                    return "Downloaded file is not a valid executable";
            }

            var stagedDir = SteamDetector.GetConfigDir();
            var stagedPath = Path.Combine(stagedDir, "staged_update.exe");

            try
            {
                if (!Directory.Exists(stagedDir))
                    Directory.CreateDirectory(stagedDir);
                if (File.Exists(stagedPath))
                    File.Delete(stagedPath);
                File.Move(tempPath, stagedPath);
            }
            catch (Exception ex)
            {
                return $"Could not stage update file: {ex.Message}";
            }

            // If a game is currently playing, wait for it to exit before auto-restarting
            if (IsAnyGameRunning())
            {
                onProgress?.Invoke(100, "Update ready. Waiting for game to close before restarting...");
                TrayIconService.Instance.ShowNotification(
                    "CloudRedirect Update Ready",
                    "Update is downloaded. CloudRedirect will automatically restart once your active game closes.");

                ActiveGameTrackerService.OnActiveGameChanged += HandleGameExitForUpdate;
                return null;
            }

            onProgress?.Invoke(100, "Applying update and restarting...");
            return ApplyStagedAndRelaunch(stagedPath);
        }
        catch (Exception ex)
        {
            return $"Update failed: {ex.Message}";
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    public static bool IsAnyGameRunning()
    {
        if (ActiveGameTrackerService.CurrentGame != null)
            return true;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false);
            var val = key?.GetValue("RunningAppId");
            if (val is int intVal && intVal > 0) return true;
            if (val is long longVal && longVal > 0) return true;
        }
        catch { }

        return false;
    }

    private static void HandleGameExitForUpdate(ActiveGameInfo? game)
    {
        if (game == null && !IsAnyGameRunning())
        {
            ActiveGameTrackerService.OnActiveGameChanged -= HandleGameExitForUpdate;

            // Wait 2.5s for save sync to complete cleanly before restarting
            Task.Delay(2500).ContinueWith(_ =>
            {
                var stagedPath = Path.Combine(SteamDetector.GetConfigDir(), "staged_update.exe");
                if (File.Exists(stagedPath) && !IsAnyGameRunning())
                {
                    ApplyStagedAndRelaunch(stagedPath);
                }
            });
        }
    }

    public static string? GetAppExecutablePath()
    {
        var launcherPath = Environment.GetEnvironmentVariable("CLOUDREDIRECT_LAUNCHER_PATH");
        if (!string.IsNullOrEmpty(launcherPath) && File.Exists(launcherPath) &&
            launcherPath.EndsWith("CloudRedirect.exe", StringComparison.OrdinalIgnoreCase))
        {
            return launcherPath;
        }

        // Check if CloudRedirect.exe is side-by-side with the current process
        var processDir = AppContext.BaseDirectory;
        var sideBySideLauncher = Path.Combine(processDir, "CloudRedirect.exe");
        if (File.Exists(sideBySideLauncher))
        {
            return sideBySideLauncher;
        }

        // Check user's Downloads\Programs folder
        try
        {
            var downloadsLauncher = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Programs", "CloudRedirect.exe");
            if (File.Exists(downloadsLauncher))
            {
                return downloadsLauncher;
            }
        }
        catch { }

        // Fallback: Default to CloudRedirect.exe in the application base directory (never CloudRedirect.Core.exe)
        return Path.Combine(AppContext.BaseDirectory, "CloudRedirect.exe");
    }

    /// <summary>
    /// Restarts the application using the launcher executable.
    /// </summary>
    public static void RestartApp()
    {
        var targetLauncher = GetAppExecutablePath();
        if (string.IsNullOrEmpty(targetLauncher) || !File.Exists(targetLauncher))
        {
            targetLauncher = Path.Combine(AppContext.BaseDirectory, "CloudRedirect.exe");
        }

        if (targetLauncher.EndsWith("CloudRedirect.Core.exe", StringComparison.OrdinalIgnoreCase))
        {
            targetLauncher = Path.Combine(Path.GetDirectoryName(targetLauncher) ?? AppContext.BaseDirectory, "CloudRedirect.exe");
        }

        int currentPid = Environment.ProcessId;
        string relaunchArgs = $"--update-from-pid {currentPid}";

        try
        {
            if (File.Exists(targetLauncher))
            {
                Process.Start(new ProcessStartInfo(targetLauncher, relaunchArgs) { UseShellExecute = true });
            }
            else
            {
                var current = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CloudRedirect.exe");
                Process.Start(new ProcessStartInfo(current, relaunchArgs) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            App.LogStartup($"RestartApp failed: {ex.Message}");
        }

        Environment.Exit(0);
    }

    public static string? ApplyStagedAndRelaunch(string stagedExePath)
    {
        App.LogStartup($"ApplyStagedAndRelaunch called with: {stagedExePath}");
        try
        {
            var targetLauncher = GetAppExecutablePath();
            if (string.IsNullOrEmpty(targetLauncher))
            {
                targetLauncher = Path.Combine(AppContext.BaseDirectory, "CloudRedirect.exe");
            }

            // Safety guard: The downloaded release asset is always the launcher (CloudRedirect.exe).
            // Under NO circumstance should we overwrite CloudRedirect.Core.exe with the launcher bundle!
            if (targetLauncher.EndsWith("CloudRedirect.Core.exe", StringComparison.OrdinalIgnoreCase))
            {
                targetLauncher = Path.Combine(Path.GetDirectoryName(targetLauncher) ?? AppContext.BaseDirectory, "CloudRedirect.exe");
            }

            var dir = Path.GetDirectoryName(targetLauncher);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var backupPath = targetLauncher + ".old";

            // Cross-volume safe copy: stagedExePath may be on C: while targetLauncher is on D: or network drive.
            // File.Copy works across different volumes, whereas File.Move throws IOException.
            bool updated = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(targetLauncher))
                    {
                        try
                        {
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                            File.Copy(targetLauncher, backupPath, overwrite: true);
                        }
                        catch { }
                    }

                    File.Copy(stagedExePath, targetLauncher, overwrite: true);
                    updated = true;
                    try { File.Delete(stagedExePath); } catch { }
                    try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                    break;
                }
                catch
                {
                    Thread.Sleep(300);
                }
            }

            int currentPid = Environment.ProcessId;

            if (!updated)
            {
                // Fallback: spawn external updater batch to swap after this process terminates
                var updaterCmd = Path.Combine(Path.GetTempPath(), "cloudredirect_update.cmd");
                var batchContent = $"@echo off\r\ntimeout /t 1 /nobreak >nul\r\ncopy /y \"{stagedExePath}\" \"{targetLauncher}\" >nul\r\ndel /f /q \"{stagedExePath}\" >nul\r\nstart \"\" \"{targetLauncher}\" --update-from-pid {currentPid}\r\ndel /f /q \"%~f0\"\r\n";
                File.WriteAllText(updaterCmd, batchContent);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{updaterCmd}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                Environment.Exit(0);
                return null;
            }

            // Relaunch the launcher (CloudRedirect.exe) passing --update-from-pid so it waits for us to cleanly exit.
            // It will unpack the updated CloudRedirect.Core.exe and start it cleanly.
            Process.Start(new ProcessStartInfo(targetLauncher, $"--update-from-pid {currentPid}") { UseShellExecute = true });
            Environment.Exit(0);
            return null;
        }
        catch (Exception ex)
        {
            return $"Failed to apply update: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks GitHub for a newer cloud_redirect.dll release asset.
    /// </summary>
    internal static async Task<(bool UpdateAvailable, byte[]? DllBytes)> CheckRemoteDllAsync(string deployedDllPath)
    {
        try
        {
            var json = await Http.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
            var releases = doc.RootElement;
            if (releases.GetArrayLength() == 0) return (false, null);

            var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (localVersion == null) return (false, null);

            JsonElement root = default;
            bool foundCandidate = false;
            foreach (var rel in releases.EnumerateArray())
            {
                if (rel.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True) continue;
                if (rel.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True) continue;
                var tag = rel.GetProperty("tag_name").GetString() ?? "";
                if (IsPrereleaseTag(tag)) continue;
                if (!TryParseVersion(tag, out var rVer, out _) || rVer <= localVersion) continue;

                root = rel;
                foundCandidate = true;
                break;
            }

            if (!foundCandidate || !root.TryGetProperty("assets", out var assets))
                return (false, null);

            string? dllUrl = null;
            string? sha256Url = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals("cloud_redirect.dll", StringComparison.OrdinalIgnoreCase))
                    dllUrl = asset.GetProperty("browser_download_url").GetString();
                else if (name.Equals("cloud_redirect.dll.sha256", StringComparison.OrdinalIgnoreCase))
                    sha256Url = asset.GetProperty("browser_download_url").GetString();
            }

            if (dllUrl == null) return (false, null);

            if (sha256Url != null && File.Exists(deployedDllPath))
            {
                var remoteHash = (await Http.GetStringAsync(sha256Url)).Trim();
                if (remoteHash.Length == 64)
                {
                    var localHash = ComputeFileSHA256(deployedDllPath);
                    if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                        return (false, null);
                }
            }

            var bytes = await Http.GetByteArrayAsync(dllUrl);
            if (bytes == null || bytes.Length == 0) return (false, null);

            if (sha256Url != null)
            {
                var remoteHash = (await Http.GetStringAsync(sha256Url)).Trim();
                using var sha = SHA256.Create();
                var downloadedHash = Convert.ToHexString(sha.ComputeHash(bytes));
                if (!string.Equals(downloadedHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                    return (false, null);
            }

            return (true, bytes);
        }
        catch
        {
            return (false, null);
        }
    }
}
