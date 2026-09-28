using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CloudRedirect.Resources;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Detects the Steam installation path via the Windows registry or well-known paths.
/// </summary>
public static class SteamDetector
{
    private static readonly object _cacheLock = new();
    private static string? _cachedPath;

    /// <summary>
    /// Supported Steam client versions our patches and RVAs target. Index 0 is the newest.
    /// </summary>
    public static readonly long[] SupportedSteamVersions = { 1782866176, 1782533657, 1782437068, 1782428855, 1782344391, 1782257239, 1781041600, 1780352834, 1779918128, 1779486452, 1778281814, 1778003620 };

    public static long ExpectedSteamVersion => SupportedSteamVersions[0];

    public static bool IsSupportedSteamVersion(long version)
    {
        foreach (var v in SupportedSteamVersions)
            if (v == version) return true;
        return false;
    }

    /// <summary>
    /// Returns the Steam installation directory, or null if not found.
    /// Results are cached after the first successful lookup.
    /// </summary>
    public static string? FindSteamPath()
    {
        lock (_cacheLock)
        {
            if (_cachedPath != null)
                return _cachedPath;
        }

        // Resolve outside the lock to avoid stalling on slow filesystem lookups.
        var resolved = NormalizeToSteamRoot(TryRegistry())
                       ?? NormalizeToSteamRoot(TryKnownPaths());

        lock (_cacheLock)
        {
            // Another thread may have resolved while we were outside the lock;
            // prefer the already-cached value to keep a single stable result.
            if (_cachedPath != null)
                return _cachedPath;
            _cachedPath = resolved;
            return _cachedPath;
        }
    }

    /// <summary>
    /// Manually override the Steam path (e.g. from a Browse dialog).
    /// Validates that the directory exists before accepting.
    /// </summary>
    public static bool SetSteamPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        var root = NormalizeToSteamRoot(path);
        if (root == null)
            return false;
        lock (_cacheLock) { _cachedPath = root; }
        return true;
    }

    /// <summary>
    /// Reads the installed Steam client version from the manifest file.
    /// Returns null if the manifest is missing or unparseable.
    /// </summary>
    public static long? GetSteamVersion()
    {
        var steamPath = FindSteamPath();
        if (steamPath == null) return null;
        return GetSteamVersion(steamPath);
    }

    /// <summary>
    /// Reads the installed Steam client version from the manifest file at a given path.
    /// </summary>
    public static long? GetSteamVersion(string steamPath)
    {
        try
        {
            var manifest = Path.Combine(steamPath, "package", "steam_client_win64.manifest");
            if (!File.Exists(manifest)) return null;
            foreach (var line in File.ReadLines(manifest))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"version\""))
                    continue;
                // format: "version"		"1777411435"
                var last = trimmed.LastIndexOf('"');
                var secondLast = trimmed.LastIndexOf('"', last - 1);
                if (last > secondLast && secondLast >= 0)
                {
                    var val = trimmed[(secondLast + 1)..last];
                    if (long.TryParse(val, out var ver))
                        return ver;
                }
            }
        }
        catch
        {
            // Version parse can fail if manifest is malformed — not critical
        }
        return null;
    }

    public sealed record SteamVersionDetails(
        string DisplayVersion,
        string Branch,
        bool IsBeta,
        long? BuildTimestamp,
        DateTime? BuildDate,
        string? ExeVersion,
        string? PackageVersion = null,
        string? ClientBuildDateStr = null,
        string? WebBuildDateStr = null,
        string? ApiVersion = null);

    /// <summary>
    /// Detects Steam's client version, build date, branch (Stable or Beta), web build date, and API version.
    /// Matches the fields displayed in the official Steam "About Steam" dialog.
    /// </summary>
    public static SteamVersionDetails GetSteamVersionDetails(string? steamPath = null)
    {
        steamPath ??= FindSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            return new SteamVersionDetails("Not Found", "Unknown", false, null, null, null);
        }

        // 1. Exe File Version
        string? exeVersion = null;
        var exePath = Path.Combine(steamPath, "steam.exe");
        if (File.Exists(exePath))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                exeVersion = vi.FileVersion;
            }
            catch { }
        }

        // 2. Build Timestamp & Package Version from manifest
        long? buildTimestamp = GetSteamVersion(steamPath);
        DateTime? buildDate = null;
        string? dateStr = null;
        if (buildTimestamp.HasValue && buildTimestamp.Value > 0)
        {
            try
            {
                var dto = DateTimeOffset.FromUnixTimeSeconds(buildTimestamp.Value).ToLocalTime();
                buildDate = dto.DateTime;
                dateStr = dto.ToString("MMM dd, yyyy");
            }
            catch { }
        }

        string packageVersion = buildTimestamp.HasValue ? buildTimestamp.Value.ToString() : "1788652215";

        // 3. Channel (Stable vs Beta)
        bool isBeta = false;
        var betaFile = Path.Combine(steamPath, "package", "beta");
        if (File.Exists(betaFile))
        {
            try
            {
                var text = File.ReadAllText(betaFile).Trim();
                if (!string.IsNullOrEmpty(text))
                    isBeta = true;
            }
            catch { isBeta = true; }
        }

        if (!isBeta)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var betaVal = key?.GetValue("Beta") as string;
                if (!string.IsNullOrEmpty(betaVal))
                    isBeta = true;
            }
            catch { }
        }

        string branch = isBeta ? "Beta Client" : "Stable Client";

        // 4. Steam Client Build Date
        string? clientBuildDateStr = null;
        try
        {
            var clientDll = Path.Combine(steamPath, "steamclient64.dll");
            var checkFile = File.Exists(clientDll) ? clientDll : exePath;
            if (File.Exists(checkFile))
            {
                var fi = new FileInfo(checkFile);
                clientBuildDateStr = fi.LastWriteTime.ToString("ddd, MMM dd, yyyy hh:mm tt");
            }
        }
        catch { }

        // 5. Steam Web Build Date
        string? webBuildDateStr = null;
        try
        {
            var webJs = Path.Combine(steamPath, "steamui", "library.js");
            if (File.Exists(webJs))
            {
                var fi = new FileInfo(webJs);
                webBuildDateStr = fi.LastWriteTime.ToString("ddd, MMM dd, yyyy hh:mm tt");
            }
            else if (!string.IsNullOrEmpty(clientBuildDateStr))
            {
                webBuildDateStr = clientBuildDateStr;
            }
        }
        catch { }

        // 6. Steam API Version (highest SteamClientXXX export in steamclient64.dll)
        string apiVersion = "SteamClient023";
        try
        {
            var clientDll = Path.Combine(steamPath, "steamclient64.dll");
            if (!File.Exists(clientDll))
                clientDll = Path.Combine(steamPath, "steamclient.dll");

            if (File.Exists(clientDll))
            {
                using var fs = new FileStream(clientDll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[Math.Min(fs.Length, 10 * 1024 * 1024)];
                int read = fs.Read(buf, 0, buf.Length);
                var text = System.Text.Encoding.Latin1.GetString(buf, 0, read);
                var matches = System.Text.RegularExpressions.Regex.Matches(text, @"SteamClient\d{3}");
                if (matches.Count > 0)
                {
                    apiVersion = matches.Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => m.Value)
                        .OrderByDescending(v => v)
                        .FirstOrDefault() ?? "SteamClient023";
                }
            }
        }
        catch { }

        // Formatted display version
        string displayVersion;
        if (!string.IsNullOrEmpty(exeVersion) && !string.IsNullOrEmpty(dateStr))
            displayVersion = $"{exeVersion} ({dateStr})";
        else if (!string.IsNullOrEmpty(exeVersion))
            displayVersion = exeVersion;
        else if (!string.IsNullOrEmpty(dateStr))
            displayVersion = $"Build {buildTimestamp} ({dateStr})";
        else
            displayVersion = "Detected";

        return new SteamVersionDetails(
            displayVersion,
            branch,
            isBeta,
            buildTimestamp,
            buildDate,
            exeVersion,
            packageVersion,
            clientBuildDateStr,
            webBuildDateStr,
            apiVersion);
    }

    /// <summary>Walks up from <paramref name="path"/> to find the directory containing steam.exe, or null.</summary>
    private static string? NormalizeToSteamRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var dir = new DirectoryInfo(path);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "steam.exe")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
            // Malformed path -- fall through to null
        }

        return null;
    }

    private static string? TryRegistry()
    {
        try
        {
            // 64-bit Steam
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam");
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;

            // 32-bit Steam
            using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            path = key32?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;

            // Current user
            using var keyUser = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            path = keyUser?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;
        }
        catch
        {
            // Registry access can fail in sandboxed/restricted environments
        }

        return null;
    }

    private static string? TryKnownPaths()
    {
        string[] candidates =
        [
            @"C:\Games\Steam",
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            @"D:\Steam",
            @"D:\Games\Steam",
        ];

        foreach (var path in candidates)
        {
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Returns the path to the CloudRedirect config directory (%AppData%/CloudRedirect).
    /// Per-user so each Windows account has its own provider settings.
    /// </summary>
    public static string GetConfigDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CloudRedirect");
    }

    /// <summary>
    /// Returns the path to config.json (%AppData%/CloudRedirect/config.json).
    /// </summary>
    public static string GetConfigFilePath()
    {
        return Path.Combine(GetConfigDir(), "config.json");
    }

    /// <summary>
    /// Returns the path to the manifest pin config in the Steam folder
    /// (per-system, not per-user). Returns null if Steam isn't found.
    /// </summary>
    public static string? GetPinConfigPath()
    {
        var steamPath = FindSteamPath();
        if (steamPath == null) return null;
        return Path.Combine(steamPath, "cloud_redirect", "config.json");
    }

    /// <summary>
    /// Returns the log file path, or null if Steam isn't found.
    /// </summary>
    public static string? GetLogPath()
    {
        var steamPath = FindSteamPath();
        if (steamPath == null) return null;
        return Path.Combine(steamPath, "cloud_redirect.log");
    }

    /// <summary>
    /// Returns true if any Steam process is currently running.
    /// </summary>
    public static bool IsSteamRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName("steam");
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if Steam is running and prompts the user to close it.
    /// Returns true if Steam is not running (safe to proceed), false if the user declined or Steam is still running.
    /// </summary>
    public static async Task<bool> EnsureSteamClosedAsync()
    {
        if (!IsSteamRunning())
            return true;

        await Dialog.ShowWarningAsync(S.Get("Steam_IsRunningTitle"),
            S.Get("Steam_IsRunningMessage"));

        return false;
    }

    /// <summary>
    /// Reads the "mode" value from settings.json. Returns null if unset or unreadable.
    /// </summary>
    public static string? ReadModeSetting()
    {
        try
        {
            var path = Path.Combine(GetConfigDir(), "settings.json");
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("mode", out var prop))
                return prop.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Reads and parses config.json. Returns null if file doesn't exist or can't be parsed.
    /// </summary>
    public static CloudConfig? ReadConfig()
    {
        var configPath = GetConfigFilePath();
        if (!File.Exists(configPath)) return null;

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("provider", out var providerProp))
                return null;
            var provider = providerProp.GetString();
            if (provider == null) return null;

            string? tokenPath = null;
            if (root.TryGetProperty("token_path", out var tp))
                tokenPath = tp.GetString();

            string? syncPath = null;
            if (root.TryGetProperty("sync_path", out var sp))
                syncPath = sp.GetString();

            return new CloudConfig(provider, tokenPath, syncPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Counts local Lua files in config/stplug-in/*.lua and cloud-synced Lua files from .sync_state / LuaManifest.json.
    /// </summary>
    public static (int LocalCount, int CloudCount) CountLuaFiles(string? steamPath)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return (0, 0);

        int localCount = 0;
        int cloudCount = 0;

        try
        {
            var luaDir = Path.Combine(steamPath, "config", "stplug-in");
            if (Directory.Exists(luaDir))
            {
                localCount = Directory.GetFiles(luaDir, "*.lua").Length;

                var syncStatePath = Path.Combine(luaDir, ".sync_state");
                if (File.Exists(syncStatePath))
                {
                    var lines = File.ReadAllLines(syncStatePath);
                    // First line is timestamp; subsequent lines are filenames
                    cloudCount = System.Math.Max(0, lines.Length - 1);
                }
            }

            // If .sync_state wasn't found or was 0, check LuaManifest.json in local cloud storage cache
            if (cloudCount == 0)
            {
                var storageDir = Path.Combine(steamPath, "cloud_redirect", "storage");
                if (Directory.Exists(storageDir))
                {
                    foreach (var accountDir in Directory.GetDirectories(storageDir))
                    {
                        var manifestPath = Path.Combine(accountDir, "0", "LuaManifest.json");
                        if (File.Exists(manifestPath))
                        {
                            try
                            {
                                var json = File.ReadAllText(manifestPath);
                                using var doc = System.Text.Json.JsonDocument.Parse(json);
                                int active = 0;
                                foreach (var prop in doc.RootElement.EnumerateObject())
                                {
                                    bool isDel = prop.Value.TryGetProperty("del", out var d) && d.GetInt64() > 0;
                                    if (!isDel) active++;
                                }
                                if (active > cloudCount) cloudCount = active;
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch { }

        return (localCount, cloudCount);
    }

    private static System.Collections.Generic.HashSet<uint>? _cachedLuaAppIds;
    private static DateTime _lastLuaAppIdsCheck = DateTime.MinValue;
    private static readonly object _luaCacheLock = new();

    /// <summary>
    /// Checks whether an AppID belongs to a Lua-unlocked game in config/stplug-in.
    /// Only Lua games have their cloud saves intercepted and redirected by CloudRedirect.
    /// </summary>
    private static readonly object _interceptCacheLock = new();
    private static System.Collections.Generic.HashSet<uint>? _cachedInterceptAppIds;
    private static DateTime _lastInterceptAppIdsCheck = DateTime.MinValue;

    /// <summary>
    /// Checks whether an AppID is configured for Zero-Lua interception (via cloud_redirect/config.json or intercept_apps.txt).
    /// </summary>
    public static bool IsInterceptApp(uint appId, string? steamPath = null)
    {
        if (appId == 0) return false;
        var apps = GetInterceptAppIds(steamPath);
        return apps.Contains(appId);
    }

    /// <summary>
    /// Gets all AppIDs explicitly configured for Zero-Lua interception.
    /// </summary>
    public static System.Collections.Generic.HashSet<uint> GetInterceptAppIds(string? steamPath = null)
    {
        lock (_interceptCacheLock)
        {
            if (_cachedInterceptAppIds != null && (DateTime.UtcNow - _lastInterceptAppIdsCheck).TotalSeconds < 3)
                return _cachedInterceptAppIds;
        }

        steamPath ??= FindSteamPath();
        var set = new System.Collections.Generic.HashSet<uint>();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return set;

        var cloudDir = Path.Combine(steamPath, "cloud_redirect");

        // 1. Read cloud_redirect/config.json
        try
        {
            var cfgPath = Path.Combine(cloudDir, "config.json");
            if (File.Exists(cfgPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
                if (doc.RootElement.TryGetProperty("intercept_apps", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var elem in arr.EnumerateArray())
                    {
                        if (elem.TryGetUInt32(out var id) && id > 0)
                            set.Add(id);
                        else if (elem.ValueKind == System.Text.Json.JsonValueKind.String && uint.TryParse(elem.GetString(), out var strId) && strId > 0)
                            set.Add(strId);
                    }
                }
            }
        }
        catch { }

        // 2. Read cloud_redirect/intercept_apps.txt
        try
        {
            var txtPath = Path.Combine(cloudDir, "intercept_apps.txt");
            if (File.Exists(txtPath))
            {
                foreach (var line in File.ReadLines(txtPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    if (uint.TryParse(trimmed, out var txtId) && txtId > 0)
                        set.Add(txtId);
                }
            }
        }
        catch { }

        lock (_interceptCacheLock)
        {
            _cachedInterceptAppIds = set;
            _lastInterceptAppIdsCheck = DateTime.UtcNow;
        }

        return set;
    }

    /// <summary>
    /// Adds an AppID to Zero-Lua interception (cloud_redirect/intercept_apps.txt and config.json)
    /// without creating any .lua files in config/stplug-in/.
    /// </summary>
    public static void AddInterceptApp(uint appId, string? steamPath = null)
    {
        if (appId == 0) return;
        steamPath ??= FindSteamPath();
        if (string.IsNullOrEmpty(steamPath)) return;

        var cloudDir = Path.Combine(steamPath, "cloud_redirect");
        Directory.CreateDirectory(cloudDir);

        // Append to intercept_apps.txt
        try
        {
            var txtPath = Path.Combine(cloudDir, "intercept_apps.txt");
            var existing = File.Exists(txtPath) ? File.ReadAllLines(txtPath).Select(l => l.Trim()).ToHashSet() : new System.Collections.Generic.HashSet<string>();
            if (!existing.Contains(appId.ToString()))
            {
                File.AppendAllText(txtPath, appId.ToString() + Environment.NewLine);
            }
        }
        catch { }

        // Update config.json
        try
        {
            var cfgPath = Path.Combine(cloudDir, "config.json");
            System.Text.Json.Nodes.JsonObject rootObj;
            if (File.Exists(cfgPath))
            {
                var text = File.ReadAllText(cfgPath);
                rootObj = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            }
            else
            {
                rootObj = new System.Text.Json.Nodes.JsonObject();
            }

            if (!rootObj.ContainsKey("cloud_redirect")) rootObj["cloud_redirect"] = true;

            System.Text.Json.Nodes.JsonArray arr;
            if (rootObj.TryGetPropertyValue("intercept_apps", out var existingArrNode) && existingArrNode is System.Text.Json.Nodes.JsonArray existingArr)
            {
                arr = existingArr;
            }
            else
            {
                arr = new System.Text.Json.Nodes.JsonArray();
                rootObj["intercept_apps"] = arr;
            }

            bool alreadyIn = false;
            foreach (var item in arr)
            {
                if (item != null && uint.TryParse(item.ToString(), out var val) && val == appId)
                {
                    alreadyIn = true;
                    break;
                }
            }

            if (!alreadyIn)
            {
                arr.Add(appId);
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(cfgPath, rootObj.ToJsonString(options));
            }
        }
        catch { }

        lock (_interceptCacheLock)
        {
            _cachedInterceptAppIds?.Add(appId);
        }
    }

    /// <summary>
    /// Checks whether Steam Cloud rejected upload for this AppID (returns true if Upload Access Denied or syncstate 3).
    /// </summary>
    public static bool HasCloudAccessDenied(uint appId, string? steamPath = null)
    {
        if (appId == 0) return false;
        steamPath ??= FindSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return false;

        // 1. Check cloud_log.txt tail
        try
        {
            var logPath = Path.Combine(steamPath, "logs", "cloud_log.txt");
            if (File.Exists(logPath))
            {
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long len = fs.Length;
                long offset = Math.Max(0, len - 128 * 1024);
                fs.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, System.Text.Encoding.Latin1);
                var tail = reader.ReadToEnd();
                if (tail.Contains($"[AppID {appId}] Upload Access Denied") ||
                    (tail.Contains($"[AppID {appId}]") && tail.Contains("result Access Denied")))
                {
                    return true;
                }
            }
        }
        catch { }

        // 2. Check userdata/*/{appId}/remotecache.vdf for syncstate 3
        try
        {
            var userDir = Path.Combine(steamPath, "userdata");
            if (Directory.Exists(userDir))
            {
                foreach (var accountDir in Directory.EnumerateDirectories(userDir))
                {
                    var vdf = Path.Combine(accountDir, appId.ToString(), "remotecache.vdf");
                    if (File.Exists(vdf))
                    {
                        var text = File.ReadAllText(vdf);
                        if (text.Contains("\"syncstate\"") && System.Text.RegularExpressions.Regex.IsMatch(text, @"""syncstate""\s+""3"""))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Checks whether steam_appid.txt exists in the game installation directory (typical marker for depot-unlocked games).
    /// </summary>
    public static bool HasAppIdTxt(uint appId, string? steamPath = null)
    {
        if (appId == 0) return false;
        steamPath ??= FindSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return false;

        try
        {
            var installDir = AppCloudConfig.FindGameInstallDir(steamPath, appId);
            if (!string.IsNullOrEmpty(installDir) && File.Exists(Path.Combine(installDir, "steam_appid.txt")))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Safely updates remotecache.vdf for an AppID, clearing the yellow "Unable to sync" state (syncstate 3 -> 1).
    /// </summary>
    public static bool FixRemoteCacheSyncState(uint appId, string? steamPath = null)
    {
        if (appId == 0) return false;
        steamPath ??= FindSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return false;

        bool updated = false;
        try
        {
            var userDir = Path.Combine(steamPath, "userdata");
            if (Directory.Exists(userDir))
            {
                foreach (var accountDir in Directory.EnumerateDirectories(userDir))
                {
                    var vdf = Path.Combine(accountDir, appId.ToString(), "remotecache.vdf");
                    if (File.Exists(vdf))
                    {
                        var text = File.ReadAllText(vdf);
                        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"""syncstate""\s+""3"""))
                        {
                            var fixedText = System.Text.RegularExpressions.Regex.Replace(text, @"""syncstate""\s+""3""", "\"syncstate\"\t\t\"1\"");
                            File.WriteAllText(vdf, fixedText, System.Text.Encoding.UTF8);
                            updated = true;
                        }
                    }
                }
            }
        }
        catch { }

        return updated;
    }

    public static bool IsLuaGame(uint appId, string? steamPath = null)
    {
        if (appId == 0) return false;
        var luaApps = GetLuaAppIds(steamPath);
        return luaApps.Contains(appId);
    }

    /// <summary>
    /// Gets all AppIDs defined in config/stplug-in/*.lua (cached for 3 seconds).
    /// Matches self-unlocking or addappid rules.
    /// </summary>
    public static System.Collections.Generic.HashSet<uint> GetLuaAppIds(string? steamPath = null)
    {
        lock (_luaCacheLock)
        {
            if (_cachedLuaAppIds != null && (DateTime.UtcNow - _lastLuaAppIdsCheck).TotalSeconds < 3)
                return _cachedLuaAppIds;
        }

        steamPath ??= FindSteamPath();
        var set = new System.Collections.Generic.HashSet<uint>();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return set;

        var luaDir = Path.Combine(steamPath, "config", "stplug-in");
        if (Directory.Exists(luaDir))
        {
            var addAppIdRegex = new System.Text.RegularExpressions.Regex(@"addappid\s*\(\s*(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            try
            {
                foreach (var file in Directory.EnumerateFiles(luaDir, "*.lua"))
                {
                    var stem = Path.GetFileNameWithoutExtension(file);
                    if (uint.TryParse(stem, out var fileAppId) && fileAppId > 0)
                    {
                        set.Add(fileAppId);
                    }

                    try
                    {
                        foreach (var line in File.ReadLines(file))
                        {
                            var trimmed = line.TrimStart();
                            if (trimmed.StartsWith("--", StringComparison.Ordinal)) continue;

                            var match = addAppIdRegex.Match(trimmed);
                            if (match.Success && uint.TryParse(match.Groups[1].Value, out var innerAppId) && innerAppId > 0)
                            {
                                set.Add(innerAppId);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        lock (_luaCacheLock)
        {
            _cachedLuaAppIds = set;
            _lastLuaAppIdsCheck = DateTime.UtcNow;
        }

        return set;
    }

    /// <summary>
    /// Reads Lua sync configuration (sync_luas, sync_luas_backup, sync_luas_restore).
    /// </summary>
    public static (bool SyncLuas, bool SyncBackup, bool SyncRestore) ReadLuaSyncConfig()
    {
        try
        {
            var configPath = GetConfigFilePath();
            if (!File.Exists(configPath)) return (true, true, false);
            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool syncLuas = !root.TryGetProperty("sync_luas", out var sl) || sl.ValueKind != System.Text.Json.JsonValueKind.False;
            bool backup = !root.TryGetProperty("sync_luas_backup", out var b) || b.ValueKind != System.Text.Json.JsonValueKind.False;
            bool restore = root.TryGetProperty("sync_luas_restore", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.True;
            return (syncLuas, backup, restore);
        }
        catch
        {
            return (true, true, false);
        }
    }

    /// <summary>
    /// Saves Lua sync configuration (sync_luas, sync_luas_backup, sync_luas_restore).
    /// </summary>
    public static void SaveLuaSyncConfig(bool backup, bool restore)
    {
        try
        {
            var configPath = GetConfigFilePath();
            bool syncLuas = backup || restore;
            ConfigHelper.SaveConfig(configPath,
                new[] { "sync_luas", "sync_luas_backup", "sync_luas_restore" },
                writer =>
                {
                    writer.WriteBoolean("sync_luas", syncLuas);
                    writer.WriteBoolean("sync_luas_backup", backup);
                    writer.WriteBoolean("sync_luas_restore", restore);
                });
        }
        catch { }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, string> _appNameCache = new();

    /// <summary>
    /// Gets the friendly game name for an AppID by reading appmanifest_<AppID>.acf in Steam libraries.
    /// Falls back to AppID if not found locally.
    /// </summary>
    public static string GetGameName(string? steamPath, uint appId)
    {
        if (appId == 0) return "General Save Data";
        if (_appNameCache.TryGetValue(appId, out var cached))
            return cached;

        if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
        {
            var libraryPaths = GetLibraryFolderPaths(steamPath);
            foreach (var libPath in libraryPaths)
            {
                var manifestPath = Path.Combine(libPath, "steamapps", $"appmanifest_{appId}.acf");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        foreach (var line in File.ReadLines(manifestPath))
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("\"name\"", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = trimmed.Split('"');
                                if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                                {
                                    var name = parts[3].Trim();
                                    _appNameCache[appId] = name;
                                    return name;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        var fallback = $"App {appId}";
        _appNameCache[appId] = fallback;
        return fallback;
    }

    private static List<string> GetLibraryFolderPaths(string steamPath)
    {
        var paths = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return paths;

        try
        {
            foreach (var line in File.ReadLines(vdfPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = trimmed.Split('"');
                if (parts.Length >= 4)
                {
                    var p = parts[3].Replace("\\\\", "\\");
                    if (Directory.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                        paths.Add(p);
                }
            }
        }
        catch { }
        return paths;
    }

    /// <summary>
    /// Scans cloud_redirect/storage to discover the most recently backed up game, its AppID, and stats.
    /// </summary>
    public static LastBackupInfo? GetLastBackupInfo(string? steamPath)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return null;

        var storageDir = Path.Combine(steamPath, "cloud_redirect", "storage");
        if (!Directory.Exists(storageDir))
            return null;

        uint latestAppId = 0;
        DateTime latestTime = DateTime.MinValue;
        string? latestAppDir = null;

        try
        {
            foreach (var accountDir in Directory.GetDirectories(storageDir))
            {
                foreach (var appDir in Directory.GetDirectories(accountDir))
                {
                    var folderName = Path.GetFileName(appDir);
                    if (folderName == "0" || !uint.TryParse(folderName, out var appId))
                        continue;

                    // Determine newest modification in app folder
                    DateTime appTime = Directory.GetLastWriteTime(appDir);
                    var cnFile = Path.Combine(appDir, "cn.cloudredirect");
                    if (File.Exists(cnFile))
                    {
                        var cnTime = File.GetLastWriteTime(cnFile);
                        if (cnTime > appTime) appTime = cnTime;
                    }

                    if (appTime > latestTime)
                    {
                        latestTime = appTime;
                        latestAppId = appId;
                        latestAppDir = appDir;
                    }
                }
            }

            if (latestAppId == 0 || latestAppDir == null)
                return null;

            int fileCount = 0;
            long totalBytes = 0;
            string? firstSaveName = null;

            var files = Directory.GetFiles(latestAppDir, "*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                var fname = Path.GetFileName(f);
                if (fname is "cn.cloudredirect" or "cn.dat" or "state.cloudredirect" or "manifest.cloudredirect" or "root_token.dat")
                    continue;

                fileCount++;
                var fi = new FileInfo(f);
                totalBytes += fi.Length;
                firstSaveName ??= fname;
            }

            var gameName = GetGameName(steamPath, latestAppId);
            return new LastBackupInfo(latestAppId, gameName, latestTime, fileCount, totalBytes, firstSaveName);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record LastBackupInfo(
    uint AppId,
    string GameName,
    DateTime BackupTime,
    int FileCount,
    long TotalBytes,
    string? PrimaryFileName);

/// <summary>
/// Parsed contents of cloud_redirect/config.json.
/// </summary>
public record CloudConfig(string Provider, string? TokenPath, string? SyncPath)
{
    public string DisplayName => Provider switch
    {
        "gdrive" => S.Get("Provider_GoogleDrive"),
        "onedrive" => S.Get("Provider_OneDrive"),
        "r2" => S.Get("Provider_R2"),
        "s3" => S.Get("Provider_S3"),
        "folder" => S.Get("Provider_FolderNetworkDrive"),
        "local" => S.Get("Provider_LocalOnly"),
        _ => Provider
    };

    public bool IsFolder => Provider == "folder";
    public bool IsLocal => Provider == "local";
}
