using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Service to manage, install, synchronize, and toggle the native Steam Millennium plugin
/// for CloudRedirect. Provides seamless integration of cloud save status and controls
/// inside the Steam client interface.
/// </summary>
public static class MillenniumPluginService
{
    public const string PluginName = "CloudRedirect";
    private const string ProtocolScheme = "cloudredirect";

    public static string? GetSteamPath()
    {
        return SteamDetector.FindSteamPath();
    }

    public static string? GetMillenniumDir()
    {
        var steam = GetSteamPath();
        if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam))
            return null;

        var millDir = Path.Combine(steam, "millennium");
        return Directory.Exists(millDir) ? millDir : null;
    }

    public static bool IsMillenniumInstalled()
    {
        var steam = GetSteamPath();
        if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam))
            return false;

        var millDll = Path.Combine(steam, "millennium", "lib", "millennium.dll");
        var wsockDll = Path.Combine(steam, "wsock32.dll");
        return File.Exists(millDll) && File.Exists(wsockDll);
    }

    public static string? GetPluginDir()
    {
        var millDir = GetMillenniumDir();
        return millDir != null ? Path.Combine(millDir, "plugins", PluginName) : null;
    }

    public static string? GetMillenniumConfigPath()
    {
        var millDir = GetMillenniumDir();
        return millDir != null ? Path.Combine(millDir, "config", "config.json") : null;
    }

    public static bool IsPluginInstalled()
    {
        var pluginDir = GetPluginDir();
        return pluginDir != null && File.Exists(Path.Combine(pluginDir, "plugin.json"));
    }

    /// <summary>
    /// Registers the cloudredirect:// URL scheme in Windows Registry (HKCU) so that
    /// clicks from Steam WebUI or Lua scripts can summon and control CloudRedirect.
    /// </summary>
    public static void RegisterUrlProtocol()
    {
        try
        {
            var exePath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolScheme}");
            if (key != null)
            {
                key.SetValue("", "URL:CloudRedirect Protocol");
                key.SetValue("URL Protocol", "");

                using var iconKey = key.CreateSubKey("DefaultIcon");
                iconKey?.SetValue("", $"\"{exePath}\",0");

                using var cmdKey = key.CreateSubKey(@"shell\open\command");
                cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] RegisterUrlProtocol failed: {ex}");
        }
    }

    /// <summary>
    /// Writes live status info to %APPDATA%\CloudRedirect\steam_plugin_status.json
    /// for consumption by the Millennium plugin.
    /// </summary>
    public static void UpdatePluginStatus(bool isRunning = true, int activeGameCount = 0, string provider = "Connected")
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var statusFile = Path.Combine(dir, "steam_plugin_status.json");
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var info = new
            {
                isRunning = isRunning,
                version = version,
                activeGameCount = activeGameCount,
                provider = provider,
                lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                appPath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath
            };

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(statusFile, json);
        }
        catch { }
    }

    /// <summary>
    /// Resolves the latest official Millennium Windows release ZIP download URL from GitHub.
    /// Falls back to the latest verified stable release URL if GitHub API is unreachable.
    /// </summary>
    public static async Task<string> ResolveLatestMillenniumDownloadUrlAsync()
    {
        const string fallbackUrl = "https://github.com/SteamClientHomebrew/Millennium/releases/download/v3.5.0/millennium-v3.5.0-windows-x86_64.zip";
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CloudRedirect/2.9");
            http.Timeout = TimeSpan.FromSeconds(8);
            var json = await http.GetStringAsync("https://api.github.com/repos/SteamClientHomebrew/Millennium/releases/latest");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString() ?? "";
                        if (name.Contains("windows", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlProp) && urlProp.GetString() is string url && !string.IsNullOrWhiteSpace(url))
                            {
                                return url;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] ResolveLatestMillenniumDownloadUrlAsync: {ex.Message}");
        }

        return fallbackUrl;
    }

    /// <summary>
    /// Automatically downloads and applies the Millennium framework to Steam if not already installed.
    /// Extracts wsock32.dll and the millennium/ directory directly into Steam root,
    /// then automatically deploys the CloudRedirect plugin.
    /// </summary>
    public static async Task<bool> EnsureMillenniumInstalledAsync(Action<string>? statusCallback = null)
    {
        try
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            {
                statusCallback?.Invoke("Steam installation path not found.");
                return false;
            }

            if (IsMillenniumInstalled())
            {
                DeployPlugin();
                return true;
            }

            statusCallback?.Invoke("Resolving latest Millennium release...");
            string downloadUrl = await ResolveLatestMillenniumDownloadUrlAsync();

            statusCallback?.Invoke("Downloading Millennium framework (~4MB)...");
            var tempZip = Path.Combine(Path.GetTempPath(), $"millennium_{Guid.NewGuid():N}.zip");
            try
            {
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("CloudRedirect/2.9");
                    http.Timeout = TimeSpan.FromMinutes(3);
                    using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    await using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                statusCallback?.Invoke("Applying Millennium to Steam directory...");
                using (var archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) // Directory entry
                        {
                            var dirPath = Path.Combine(steamPath, entry.FullName);
                            Directory.CreateDirectory(dirPath);
                            continue;
                        }

                        var destPath = Path.Combine(steamPath, entry.FullName);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        try
                        {
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MillenniumPluginService] ExtractToFile '{entry.FullName}' warning: {ex.Message}");
                        }
                    }
                }

                statusCallback?.Invoke("Deploying CloudRedirect plugin...");
                DeployPlugin();

                bool success = IsMillenniumInstalled();
                if (success)
                {
                    statusCallback?.Invoke("Millennium successfully applied to Steam!");
                }
                else
                {
                    statusCallback?.Invoke("Millennium applied, verifying installation...");
                }
                return success;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] EnsureMillenniumInstalledAsync failed: {ex}");
            statusCallback?.Invoke($"Installation error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Synchronizes the Millennium plugin state with user preference in AppSettings.
    /// If enabled and Millennium is not found, automatically downloads and applies Millennium to Steam!
    /// </summary>
    public static async Task SyncWithSettingsAsync(Action<string>? statusCallback = null)
    {
        try
        {
            if (AppSettings.EnableMillenniumPlugin)
            {
                if (!IsMillenniumInstalled())
                {
                    Debug.WriteLine("[MillenniumPluginService] Millennium not found. Auto-downloading and applying to Steam...");
                    bool installed = await EnsureMillenniumInstalledAsync(statusCallback);
                    if (installed)
                    {
                        DeployPlugin();
                        if (SteamDetector.IsSteamRunning() && !ActiveGameTrackerService.IsAnyGameActive())
                        {
                            SteamToastService.ShowAuto(
                                "CloudRedirect",
                                "Millennium framework auto-installed. Restarting Steam to load plugins..."
                            );
                            await SteamDetector.RestartSteamAsync();
                        }
                    }
                }
                else
                {
                    DeployPlugin();
                }
            }
            else
            {
                RemovePlugin();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] SyncWithSettings error: {ex}");
        }
    }

    public static void SyncWithSettings()
    {
        _ = Task.Run(() => SyncWithSettingsAsync());
    }

    private static string GetResourceContent(string exactResourceSuffix, string fallback)
    {
        try
        {
            var asm = typeof(MillenniumPluginService).Assembly;
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(exactResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] GetResourceContent failed for {exactResourceSuffix}: {ex}");
        }

        return fallback;
    }

    private static byte[]? GetResourceBytes(string exactResourceSuffix)
    {
        try
        {
            var asm = typeof(MillenniumPluginService).Assembly;
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(exactResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] GetResourceBytes failed for {exactResourceSuffix}: {ex}");
        }

        return null;
    }

    /// <summary>
    /// Deploys or updates the CloudRedirect Millennium plugin into Steam's millennium directory.
    /// </summary>
    public static bool DeployPlugin()
    {
        try
        {
            var pluginDir = GetPluginDir();
            if (string.IsNullOrEmpty(pluginDir))
                return false;

            var backendDir = Path.Combine(pluginDir, "backend");
            var distDir = Path.Combine(pluginDir, ".millennium", "Dist");

            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(backendDir);
            Directory.CreateDirectory(distDir);

            // 1. Write plugin.json
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), GetResourceContent("plugin.json", PluginJsonContent));

            // 2. Write backend/main.lua and additional backend modules
            File.WriteAllText(Path.Combine(backendDir, "main.lua"), GetResourceContent("backend.main.lua", BackendLuaContent));
            var taskbarLua = GetResourceContent("backend.taskbar.lua", "");
            if (!string.IsNullOrEmpty(taskbarLua))
                File.WriteAllText(Path.Combine(backendDir, "taskbar.lua"), taskbarLua);
            var ffiDefsLua = GetResourceContent("backend.ffi_defs.lua", "");
            if (!string.IsNullOrEmpty(ffiDefsLua))
                File.WriteAllText(Path.Combine(backendDir, "ffi_defs.lua"), ffiDefsLua);

            // 3. Write style.css
            File.WriteAllText(Path.Combine(pluginDir, "style.css"), GetResourceContent("style.css", StyleCssContent));

            // 4. Write index.js (root and .millennium/Dist) and webkit.js
            string rootJs = GetResourceContent("MillenniumPlugin.index.js", FrontendJsContent);
            string distJs = GetResourceContent("Dist.index.js", rootJs);
            File.WriteAllText(Path.Combine(pluginDir, "index.js"), rootJs);
            File.WriteAllText(Path.Combine(distDir, "index.js"), distJs);
            var webkitJs = GetResourceContent("Dist.webkit.js", "");
            if (string.IsNullOrEmpty(webkitJs))
                webkitJs = GetResourceContent("MillenniumPlugin.webkit.js", "");
            if (!string.IsNullOrEmpty(webkitJs))
                File.WriteAllText(Path.Combine(distDir, "webkit.js"), webkitJs);

            // 5. Deploy CloudRedirect.star single-binary archive package
            var millDir = GetMillenniumDir();
            if (!string.IsNullOrEmpty(millDir))
            {
                var starBytes = GetResourceBytes("CloudRedirect.star");
                if (starBytes != null && starBytes.Length > 0)
                {
                    var starPath = Path.Combine(millDir, "plugins", "CloudRedirect.star");
                    File.WriteAllBytes(starPath, starBytes);
                }
            }

            // 6. Update millennium/config/config.json enabledPlugins
            EnableInMillenniumConfig();

            // 7. Register URL scheme and write initial status
            RegisterUrlProtocol();
            UpdatePluginStatus(true);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] DeployPlugin failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Removes the CloudRedirect plugin from Steam's millennium directory.
    /// </summary>
    public static bool RemovePlugin()
    {
        try
        {
            var pluginDir = GetPluginDir();
            if (!string.IsNullOrEmpty(pluginDir) && Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, true);
            }

            var millDir = GetMillenniumDir();
            if (!string.IsNullOrEmpty(millDir))
            {
                var starPath = Path.Combine(millDir, "plugins", "CloudRedirect.star");
                if (File.Exists(starPath))
                {
                    File.Delete(starPath);
                }
            }

            DisableInMillenniumConfig();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] RemovePlugin failed: {ex}");
            return false;
        }
    }

    private static void EnableInMillenniumConfig()
    {
        try
        {
            var configPath = GetMillenniumConfigPath();
            if (string.IsNullOrEmpty(configPath))
                return;

            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            JsonNode? node = null;
            if (File.Exists(configPath))
            {
                try
                {
                    var text = File.ReadAllText(configPath);
                    node = JsonNode.Parse(text);
                }
                catch { }
            }

            node ??= new JsonObject();
            var pluginsObj = node["plugins"] as JsonObject;
            if (pluginsObj == null)
            {
                pluginsObj = new JsonObject();
                node["plugins"] = pluginsObj;
            }

            var enabledArray = pluginsObj["enabledPlugins"] as JsonArray;
            if (enabledArray == null)
            {
                enabledArray = new JsonArray();
                pluginsObj["enabledPlugins"] = enabledArray;
            }

            bool exists = false;
            foreach (var item in enabledArray)
            {
                if (string.Equals(item?.ToString(), PluginName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                enabledArray.Add(PluginName);
            }

            // Suppress the "Welcome to Millennium" first-launch popup dialog permanently
            var miscObj = node["misc"] as JsonObject;
            if (miscObj == null)
            {
                miscObj = new JsonObject();
                node["misc"] = miscObj;
            }
            miscObj["hasShownWelcomeModal"] = true;

            using var stream = File.Create(configPath);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            node.WriteTo(writer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MillenniumPluginService] EnableInMillenniumConfig error: {ex}");
            App.LogStartup($"[MillenniumPluginService] EnableInMillenniumConfig error: {ex}");
        }
    }

    private static void DisableInMillenniumConfig()
    {
        try
        {
            var configPath = GetMillenniumConfigPath();
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                return;

            var text = File.ReadAllText(configPath);
            var node = JsonNode.Parse(text);
            if (node == null) return;

            if (node["plugins"]?["enabledPlugins"] is JsonArray enabledArray)
            {
                bool modified = false;
                for (int i = enabledArray.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(enabledArray[i]?.ToString(), PluginName, StringComparison.OrdinalIgnoreCase))
                    {
                        enabledArray.RemoveAt(i);
                        modified = true;
                    }
                }

                if (modified)
                {
                    using var stream = File.Create(configPath);
                    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                    node.WriteTo(writer);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MillenniumPluginService] DisableInMillenniumConfig error: {ex}");
            App.LogStartup($"[MillenniumPluginService] DisableInMillenniumConfig error: {ex}");
        }
    }

    #region Embedded Plugin Assets

    private const string PluginJsonContent = @"{
  ""$schema"": ""https://raw.githubusercontent.com/SteamClientHomebrew/Millennium/main/src/sys/plugin-schema.json"",
  ""name"": ""CloudRedirect"",
  ""common_name"": ""CloudRedirect"",
  ""description"": ""Native Steam integration for CloudRedirect cloud save backup, sync, and status."",
  ""version"": ""1.0.0"",
  ""author"": ""CloudRedirect"",
  ""backendType"": ""lua""
}";

    private const string BackendLuaContent = @"local logger = nil
pcall(function() logger = require(""logger"") end)
local millennium = nil
pcall(function() millennium = require(""millennium"") end)

local function log_info(msg)
    if logger and logger.info then
        logger:info(""[CloudRedirect] "" .. tostring(msg))
    else
        print(""[CloudRedirect] "" .. tostring(msg))
    end
end

function launch_cloudredirect()
    log_info(""launch_cloudredirect invoked from Steam WebUI"")
    local res = os.execute('start """" ""cloudredirect://open""')
    return { success = true, result = res }
end

function trigger_backup()
    log_info(""trigger_backup invoked from Steam WebUI"")
    local res = os.execute('start """" ""cloudredirect://backup""')
    return { success = true, result = res }
end

function open_saves()
    log_info(""open_saves invoked from Steam WebUI"")
    local res = os.execute('start """" ""cloudredirect://saves""')
    return { success = true, result = res }
end

function get_status()
    local appdata = os.getenv(""APPDATA"")
    if not appdata then return { isRunning = true, status = ""active"" } end
    local status_file = appdata .. ""\\CloudRedirect\\steam_plugin_status.json""
    local f = io.open(status_file, ""r"")
    if f then
        local content = f:read(""""*a"""")
        f:close()
        return { isRunning = true, data = content }
    end
    return { isRunning = true, status = ""active"", message = ""CloudRedirect Active"" }
end

local function on_frontend_loaded()
    log_info(""Frontend UI loaded into Steam CEF successfully"")
end

local function on_load()
    log_info(""Backend Lua module loaded"")
    if millennium and millennium.ready then
        millennium.ready()
    end
end

local function on_unload()
    log_info(""Backend Lua module unloaded"")
end

return {
    on_frontend_loaded = on_frontend_loaded,
    on_load = on_load,
    on_unload = on_unload,
    launch_cloudredirect = launch_cloudredirect,
    trigger_backup = trigger_backup,
    open_saves = open_saves,
    get_status = get_status
}";

    private const string StyleCssContent = @"/* CloudRedirect Native Millennium Plugin Styles */

/* SuperNav Top Header Tab (STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT) */
#cloudredirect-supernav-item,
.cr-supernav-menu {
    cursor: pointer !important;
    user-select: none !important;
    position: relative !important;
    font-size: 18px;
    font-family: ""Motiva Sans"", ""Twemoji"", ""Noto Sans"", Helvetica, sans-serif;
    font-weight: 500;
    text-transform: uppercase;
    padding: 0 10px;
    height: inherit;
}

.cr-supernav-btn {
    cursor: pointer !important;
    white-space: nowrap !important;
    color: #dcdedf;
    transition: color 0.15s ease-out, text-shadow 0.15s ease-out !important;
}

#cloudredirect-supernav-item:hover .cr-supernav-btn,
.cr-supernav-menu:hover .cr-supernav-btn {
    color: #ffffff !important;
    text-shadow: 0 0 10px rgba(255, 255, 255, 0.45) !important;
}

.cr-supernav-label {
    font-family: inherit !important;
    font-size: inherit !important;
    font-weight: inherit !important;
    line-height: inherit !important;
    letter-spacing: inherit !important;
    text-transform: uppercase !important;
}

.cr-supernav-dot {
    display: inline-block !important;
    width: 6px !important;
    height: 6px !important;
    background: #a4d007 !important;
    border-radius: 50% !important;
    box-shadow: 0 0 6px #a4d007 !important;
    margin-left: 6px !important;
    vertical-align: middle !important;
    position: relative !important;
    top: -1px !important;
    line-height: normal !important;
}

/* Never show legacy titlebar header buttons in window controls */
#cloudredirect-header-btn,
.cr-nav-btn {
    display: none !important;
}

/* Suppress clipped dropdowns */
.cr-dropdown-menu {
    display: none !important;
}

.cr-status-dot {
    width: 7px;
    height: 7px;
    background: #a4d007;
    border-radius: 50%;
    box-shadow: 0 0 6px #a4d007;
    display: inline-block;
    flex-shrink: 0;
}

.cr-cloud-icon-svg {
    width: 14px;
    height: 14px;
    fill: currentColor;
    flex-shrink: 0;
}

/* Bottom Bar Button */
.cr-bottom-bar-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 28px;
    padding: 0 12px;
    background: #142230;
    border: 1px solid #274563;
    border-radius: 4px;
    color: #c6d4df;
    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
    user-select: none;
    transition: all 0.2s ease;
    margin: 0 6px;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
    z-index: 1000;
}

.cr-bottom-bar-btn:hover {
    background: #1c3247;
    border-color: #66c0f4;
    color: #ffffff;
    box-shadow: 0 0 10px rgba(102, 192, 244, 0.35);
}

/* Game Detail Page Badge */
.cr-game-badge {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    background: rgba(22, 34, 46, 0.85);
    border: 1px solid #2d4c6b;
    border-radius: 14px;
    padding: 4px 10px;
    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    font-size: 11px;
    font-weight: 600;
    color: #c6d4df;
    cursor: pointer;
    transition: all 0.2s ease;
    user-select: none;
    margin-left: 10px;
    vertical-align: middle;
}

.cr-game-badge:hover {
    background: #233b52;
    border-color: #66c0f4;
    color: #ffffff;
    box-shadow: 0 0 10px rgba(102, 192, 244, 0.4);
}

.cr-game-badge-check {
    color: #a4d007;
    font-weight: bold;
}
";

    private const string FrontendJsContent = @"const MILLENNIUM_IS_CLIENT_MODULE = true;
const pluginName = ""CloudRedirect"";

function InitializePlugins() {
    var _a;
    (_a = (window.PLUGIN_LIST || (window.PLUGIN_LIST = {})))[pluginName] || (_a[pluginName] = {});
    window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS || (window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS = {});
}
InitializePlugins();

const __call_server_method__ = (methodName, kwargs) => {
    try {
        if (typeof Millennium !== 'undefined' && Millennium.callServerMethod) {
            return Millennium.callServerMethod(pluginName, methodName, kwargs || {});
        }
    } catch (e) {
        console.warn('[CloudRedirect] CallServerMethod error:', e);
    }
    return Promise.resolve(null);
};

// Returns all active Steam documents (SharedJSContext, main desktop window SP Desktop_uid0, and popups)
function getAllSteamDocuments() {
    const docs = [];
    if (typeof document !== 'undefined' && document && document.body) {
        docs.push(document);
    }

    try {
        if (typeof g_PopupManager !== 'undefined' && g_PopupManager) {
            if (typeof g_PopupManager.GetPopups === 'function') {
                const popups = g_PopupManager.GetPopups();
                if (popups) {
                    for (const p of popups) {
                        const d = p?.window?.document || p?.m_popup?.window?.document || p?.m_popup?.document;
                        if (d && d.body && !docs.includes(d)) docs.push(d);
                    }
                }
            }
            if (typeof g_PopupManager.GetExistingPopup === 'function') {
                const sp = g_PopupManager.GetExistingPopup(""SP Desktop_uid0"");
                const d = sp?.window?.document || sp?.m_popup?.window?.document || sp?.m_popup?.document;
                if (d && d.body && !docs.includes(d)) docs.push(d);
            }
            if (g_PopupManager.m_mapPopups && g_PopupManager.m_mapPopups.data_) {
                g_PopupManager.m_mapPopups.data_.forEach(entry => {
                    const val = entry?.value_ || entry;
                    const d = val?.m_popup?.window?.document || val?.window?.document || val?.m_popup?.document;
                    if (d && d.body && !docs.includes(d)) docs.push(d);
                });
            }
        }
    } catch (e) {
        console.warn('[CloudRedirect] Error retrieving popup documents:', e);
    }

    try {
        if (typeof window !== 'undefined' && window.PLUGIN_LIST && window.PLUGIN_LIST.core && window.PLUGIN_LIST.core.mainWindow) {
            const d = window.PLUGIN_LIST.core.mainWindow.document;
            if (d && d.body && !docs.includes(d)) docs.push(d);
        }
    } catch (e) { }

    return docs;
}

var PluginEntryPointMain = function () {
    var millennium_main = (function (exports, client, reactDom, React) {
        'use strict';

        const cloudSvg = `<svg class=""cr-cloud-icon-svg"" viewBox=""0 0 24 24""><path d=""M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z""/></svg>`;

        function launchApp(doc) {
            __call_server_method__(""launch_cloudredirect"", {});
            try {
                const d = doc || document;
                const link = d.createElement('a');
                link.href = 'cloudredirect://open';
                d.body.appendChild(link);
                link.click();
                link.remove();
            } catch (e) { }
        }

        function ensureStyles(doc) {
            if (!doc || !doc.head) return;
            const existing = doc.getElementById('cr-millennium-styles');
            if (existing) {
                existing.remove();
            }
            const style = doc.createElement('style');
            style.id = 'cr-millennium-styles';
            style.textContent = `
                /* Never show legacy titlebar buttons in window controls */
                #cloudredirect-header-btn,
                .cr-nav-btn {
                    display: none !important;
                }

                /* Suppress any clipped dropdown containers */
                .cr-dropdown-menu {
                    display: none !important;
                }

                /* SuperNav Top Header Tab (STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT) */
                #cloudredirect-supernav-item,
                .cr-supernav-menu {
                    cursor: pointer !important;
                    user-select: none !important;
                    position: relative !important;
                    font-size: 18px;
                    font-family: ""Motiva Sans"", ""Twemoji"", ""Noto Sans"", Helvetica, sans-serif;
                    font-weight: 500;
                    text-transform: uppercase;
                    padding: 0 10px;
                    height: inherit;
                }

                .cr-supernav-btn {
                    cursor: pointer !important;
                    white-space: nowrap !important;
                    color: #dcdedf;
                    transition: color 0.15s ease-out, text-shadow 0.15s ease-out !important;
                }

                #cloudredirect-supernav-item:hover .cr-supernav-btn,
                .cr-supernav-menu:hover .cr-supernav-btn {
                    color: #ffffff !important;
                    text-shadow: 0 0 10px rgba(255, 255, 255, 0.45) !important;
                }

                .cr-supernav-label {
                    font-family: inherit !important;
                    font-size: inherit !important;
                    font-weight: inherit !important;
                    line-height: inherit !important;
                    letter-spacing: inherit !important;
                    text-transform: uppercase !important;
                }

                .cr-supernav-dot {
                    display: inline-block !important;
                    width: 6px !important;
                    height: 6px !important;
                    background: #a4d007 !important;
                    border-radius: 50% !important;
                    box-shadow: 0 0 6px #a4d007 !important;
                    margin-left: 6px !important;
                    vertical-align: middle !important;
                    position: relative !important;
                    top: -1px !important;
                    line-height: normal !important;
                }

                /* Bottom Bar Button (Next to Add Game / Steam Unlock) */
                .cr-bottom-bar-btn {
                    display: inline-flex;
                    align-items: center;
                    gap: 6px;
                    height: 28px;
                    padding: 0 12px;
                    background: #142230;
                    border: 1px solid #274563;
                    border-radius: 4px;
                    color: #c6d4df;
                    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
                    font-size: 12px;
                    font-weight: 600;
                    cursor: pointer;
                    user-select: none;
                    transition: all 0.2s ease;
                    margin: 0 6px;
                    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
                    z-index: 1000;
                }
                .cr-bottom-bar-btn:hover {
                    background: #1c3247;
                    border-color: #66c0f4;
                    color: #ffffff;
                    box-shadow: 0 0 10px rgba(102, 192, 244, 0.35);
                }

                .cr-status-dot {
                    width: 7px;
                    height: 7px;
                    background: #a4d007;
                    border-radius: 50%;
                    box-shadow: 0 0 6px #a4d007;
                    display: inline-block;
                    flex-shrink: 0;
                }

                .cr-cloud-icon-svg {
                    width: 14px;
                    height: 14px;
                    fill: currentColor;
                    flex-shrink: 0;
                }

                /* Game Detail Page Badge */
                .cr-game-badge {
                    display: inline-flex;
                    align-items: center;
                    gap: 6px;
                    background: rgba(22, 34, 46, 0.85);
                    border: 1px solid #2d4c6b;
                    border-radius: 14px;
                    padding: 4px 10px;
                    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
                    font-size: 11px;
                    font-weight: 600;
                    color: #c6d4df;
                    cursor: pointer;
                    transition: all 0.2s ease;
                    user-select: none;
                    margin-left: 10px;
                    vertical-align: middle;
                }
                .cr-game-badge:hover {
                    background: #233b52;
                    border-color: #66c0f4;
                    color: #ffffff;
                    box-shadow: 0 0 10px rgba(102, 192, 244, 0.4);
                }
                .cr-game-badge-check {
                    color: #a4d007;
                    font-weight: bold;
                }
            `;
            doc.head.appendChild(style);
        }

        // Helper to locate the exact SuperNav tab bar container in doc (Multi-language & URL resilient)
        function findSuperNavInfo(doc) {
            const allEls = doc.querySelectorAll('a, div, span, button');

            for (const el of allEls) {
                const href = (el.getAttribute && el.getAttribute('href')) || '';
                const isNavUrl = href.includes('store.steampowered.com') || href.includes('steamcommunity.com');
                const t = (el.textContent || '').trim().toUpperCase();
                const isNavText = t === 'COMMUNITY' || t === 'STORE' || t === 'LIBRARY' ||
                                  t === 'TOKO' || t === 'KOMUNITAS' || t === 'PERPUSTAKAAN' ||
                                  t === 'KEDAI' || t === 'KOMUNITI' ||
                                  t === 'TIENDA' || t === 'COMUNIDAD' || t === 'BIBLIOTECA';

                if (isNavUrl || isNavText) {
                    // Traverse up within 5 levels to locate the SuperNav container
                    let curr = el;
                    let depth = 0;
                    while (curr && curr !== doc.body && depth < 5) {
                        const parent = curr.parentElement;
                        if (!parent) break;
                        const pText = (parent.textContent || '').toUpperCase();
                        const hasStore = (parent.querySelector && parent.querySelector('a[href*=""store.steampowered.com""]')) ||
                                         pText.includes('STORE') || pText.includes('TOKO') || pText.includes('KEDAI') || pText.includes('TIENDA');
                        const hasCommunity = (parent.querySelector && parent.querySelector('a[href*=""steamcommunity.com""]')) ||
                                             pText.includes('COMMUNITY') || pText.includes('KOMUNITAS') || pText.includes('KOMUNITI') || pText.includes('COMUNIDAD');

                        if (hasStore && hasCommunity) {
                            const children = Array.from(parent.children);
                            let sampleTab = null;
                            let lastNavTab = null;

                            for (const child of children) {
                                const cText = (child.textContent || '').trim().toUpperCase();
                                const cTag = child.tagName;
                                // Ignore search inputs, window control buttons, or hidden spacers
                                if (child.querySelector && (child.querySelector('input') || child.querySelector('svg[class*=""close""]'))) {
                                    continue;
                                }
                                if (cText.length > 0 && !cText.includes('HTTP') && !cText.includes('🔍') && !cText.includes('SEARCH') && !cText.includes('✕')) {
                                    if (!sampleTab) sampleTab = child;
                                    lastNavTab = child;
                                }
                            }

                            if (!sampleTab) sampleTab = lastNavTab;

                            if (parent && lastNavTab) {
                                return {
                                    container: parent,
                                    sampleTab: sampleTab,
                                    insertAfter: lastNavTab
                                };
                            }
                        }
                        curr = parent;
                        depth++;
                    }
                }
            }

            return null;
        }

        function showSuperNavMenu(e, doc) {
            const oldMenu = doc.getElementById('cr-supernav-dropdown');
            if (oldMenu) oldMenu.remove();

            const menu = doc.createElement('div');
            menu.id = 'cr-supernav-dropdown';
            menu.style.position = 'fixed';
            menu.style.left = `${Math.min(e.clientX, (doc.defaultView?.innerWidth || 1200) - 230)}px`;
            menu.style.top = `${e.clientY + 8}px`;
            menu.style.background = '#1b2838';
            menu.style.border = '1px solid #3d4450';
            menu.style.borderRadius = '4px';
            menu.style.boxShadow = '0 8px 16px rgba(0, 0, 0, 0.6)';
            menu.style.zIndex = '999999';
            menu.style.minWidth = '210px';
            menu.style.padding = '6px 0';
            menu.style.color = '#c6d4df';
            menu.style.fontFamily = '""Motiva Sans"", sans-serif';
            menu.style.fontSize = '13px';

            const items = [
                {
                    label: '🚀 Open CloudRedirect App',
                    action: () => launchApp(doc)
                },
                {
                    label: '💾 Trigger Cloud Backup Now',
                    action: () => {
                        __call_server_method__(""trigger_backup"", {});
                        try {
                            const link = doc.createElement('a');
                            link.href = 'cloudredirect://backup';
                            doc.body.appendChild(link);
                            link.click();
                            link.remove();
                        } catch (err) { }
                    }
                },
                { separator: true },
                {
                    label: '🔃 Fast Reload Steam UI',
                    action: () => {
                        if (doc.defaultView) doc.defaultView.location.reload();
                        else window.location.reload();
                    }
                },
                {
                    label: '🔄 Quick Restart Steam',
                    action: () => {
                        try {
                            if (window.SteamClient?.User?.StartRestart) {
                                window.SteamClient.User.StartRestart(true);
                                return;
                            }
                        } catch (err) { }
                        window.location.reload();
                    }
                }
            ];

            items.forEach(item => {
                if (item.separator) {
                    const sep = doc.createElement('div');
                    sep.style.height = '1px';
                    sep.style.background = '#2a3f5a';
                    sep.style.margin = '4px 0';
                    menu.appendChild(sep);
                    return;
                }
                const btn = doc.createElement('div');
                btn.textContent = item.label;
                btn.style.padding = '8px 16px';
                btn.style.cursor = 'pointer';
                btn.style.transition = 'background 0.15s, color 0.15s';
                btn.onmouseenter = () => {
                    btn.style.background = '#2a475e';
                    btn.style.color = '#ffffff';
                };
                btn.onmouseleave = () => {
                    btn.style.background = 'transparent';
                    btn.style.color = '#c6d4df';
                };
                btn.onclick = (ev) => {
                    ev.stopPropagation();
                    menu.remove();
                    item.action();
                };
                menu.appendChild(btn);
            });

            const closeHandler = () => {
                menu.remove();
                doc.removeEventListener('click', closeHandler);
            };
            setTimeout(() => doc.addEventListener('click', closeHandler), 10);
            doc.body.appendChild(menu);
        }

        // Injects tab right into STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT row
        function injectSuperNavTab(doc) {
            if (!doc || !doc.body) return;

            const existing = doc.getElementById('cloudredirect-supernav-item');
            if (existing) {
                if (existing.parentNode) return;
                existing.remove();
            }

            const navInfo = findSuperNavInfo(doc);
            if (!navInfo || !navInfo.container || !navInfo.insertAfter) return;

            const { container, sampleTab, insertAfter } = navInfo;

            // Create the CloudRedirect SuperNav item
            const navItem = doc.createElement('div');
            navItem.id = 'cloudredirect-supernav-item';
            navItem.className = (sampleTab.className || '').replace(/\bactive\b/gi, '').trim() + ' cr-supernav-menu';
            navItem.setAttribute('role', 'button');
            navItem.setAttribute('tabindex', '0');
            navItem.title = 'CloudRedirect (Left click: Open | Right click: Menu / Reload / Restart)';

            // Find child button / inner element if present in sampleTab
            const sampleInner = sampleTab.querySelector('div, a, span') || sampleTab;
            const innerBtn = doc.createElement('div');
            innerBtn.className = (sampleInner.className || '').replace(/\bactive\b/gi, '').trim() + ' cr-supernav-btn';
            innerBtn.innerHTML = `
                <span class=""cr-supernav-label"">CLOUDREDIRECT</span>
                <span class=""cr-supernav-dot"" title=""Save Protection Active""></span>
            `;

            // Inherit computed typography dynamically without forcing disruptive inline height/display
            try {
                const sampleTarget = sampleInner || sampleTab;
                const win = doc.defaultView || window;
                if (win && sampleTarget) {
                    const computed = win.getComputedStyle(sampleTarget);
                    if (computed) {
                        if (computed.fontSize) {
                            innerBtn.style.fontSize = computed.fontSize;
                        }
                        if (computed.fontWeight) {
                            innerBtn.style.fontWeight = computed.fontWeight;
                        }
                        if (computed.fontFamily) {
                            innerBtn.style.fontFamily = computed.fontFamily;
                        }
                        if (computed.lineHeight && computed.lineHeight !== 'normal') {
                            innerBtn.style.lineHeight = computed.lineHeight;
                        }
                    }
                }
            } catch (e) { }

            navItem.appendChild(innerBtn);

            // Directly launch CloudRedirect application when tab is left-clicked
            navItem.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                launchApp(doc);
            };
            // Right-click opens context menu with Fast Reload & Quick Restart
            navItem.oncontextmenu = (e) => {
                e.preventDefault();
                e.stopPropagation();
                showSuperNavMenu(e, doc);
            };
            navItem.onkeydown = (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    launchApp(doc);
                }
            };

            // Insert directly into the row after the last tab (after MINTAMAAF5)
            if (insertAfter.nextSibling) {
                container.insertBefore(navItem, insertAfter.nextSibling);
            } else {
                container.appendChild(navItem);
            }
        }

        // 1. Inject Button in Bottom Bar (next to Add Game / Steam Unlock)
        function injectBottomBarButton(doc) {
            if (!doc || !doc.body) return;
            if (doc.getElementById('cloudredirect-bottom-btn')) return;

            let targetSibling = null;
            let parentContainer = null;

            // Strategy 1: Find existing mod buttons like ""Steam Unlock""
            const allElements = doc.querySelectorAll('button, div, a');
            for (const el of allElements) {
                const text = el.textContent || '';
                if (text.includes('Steam Unlock') || (el.className && typeof el.className === 'string' && el.className.includes('activation'))) {
                    targetSibling = el;
                    parentContainer = el.parentNode;
                    break;
                }
            }

            // Strategy 2: Find ""+ Add a Game"" button
            if (!targetSibling) {
                for (const el of allElements) {
                    const text = el.textContent || '';
                    if (text.includes('Add a Game') || text.includes('Add Game')) {
                        targetSibling = el;
                        parentContainer = el.parentNode;
                        break;
                    }
                }
            }

            // Strategy 3: Try standard selectors for Add a Game
            if (!targetSibling) {
                const addGameCandidates = doc.querySelectorAll('button[class*=""addgamebutton_""], div[class*=""addgamebutton_""], [class*=""AddGameButton""]');
                for (const el of addGameCandidates) {
                    if (el.offsetParent !== null || el.offsetWidth > 0) {
                        targetSibling = el;
                        parentContainer = el.parentNode;
                        break;
                    }
                }
            }

            // Strategy 4: Fallback to bottom bar container
            if (!parentContainer) {
                parentContainer = doc.querySelector('div[class*=""bottombar_""], div[class*=""bottombarcontrols_""], footer, .bottom_bar');
            }

            if (!parentContainer) return;

            const btn = doc.createElement('div');
            btn.id = 'cloudredirect-bottom-btn';
            btn.className = 'cr-bottom-bar-btn';
            btn.title = 'CloudRedirect v2.9.74 (Save Protection Active - Click to Open App)';
            btn.innerHTML = `
                ${cloudSvg}
                <span>CloudRedirect</span>
                <span class=""cr-status-dot""></span>
            `;

            btn.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                launchApp(doc);
            };

            if (targetSibling && targetSibling.parentNode === parentContainer) {
                targetSibling.parentNode.insertBefore(btn, targetSibling.nextSibling);
            } else if (parentContainer.firstChild) {
                parentContainer.insertBefore(btn, parentContainer.firstChild);
            } else {
                parentContainer.appendChild(btn);
            }
        }

        // 2. Inject Game Details Page Badge
        function injectGameBadge(doc) {
            if (!doc || !doc.body) return;
            const gameActionBars = doc.querySelectorAll('div[class*=""playbar_""], div[class*=""appactionandstats_""], div[class*=""appdetailsheader_""]');
            gameActionBars.forEach(bar => {
                if (bar.querySelector('.cr-game-badge')) return;

                const badge = doc.createElement('div');
                badge.className = 'cr-game-badge';
                badge.title = 'Game save files are actively redirected and backed up by CloudRedirect (Click to Open)';
                badge.innerHTML = `
                    ${cloudSvg}
                    <span>CloudRedirect</span>
                    <span class=""cr-game-badge-check"">✓</span>
                `;
                badge.onclick = (e) => {
                    e.stopPropagation();
                    e.preventDefault();
                    launchApp(doc);
                };

                bar.appendChild(badge);
            });
        }

        function runInjectionsForDoc(doc) {
            if (!doc || !doc.body) return;

            // Remove any legacy header buttons or clipped dropdown menus
            const legacyBtn = doc.getElementById('cloudredirect-header-btn');
            if (legacyBtn) legacyBtn.remove();
            doc.querySelectorAll('.cr-nav-btn, [id*=""cloudredirect-header""], .cr-dropdown-menu').forEach(el => el.remove());

            // If supernav item has old classes or wrong height/display, replace it
            const superItem = doc.getElementById('cloudredirect-supernav-item');
            if (superItem) {
                if (superItem.style.height || superItem.style.display || superItem.querySelector('.cr-supernav-btn')?.style.height) {
                    superItem.remove();
                } else if (superItem.classList.contains('cr-active')) {
                    superItem.classList.remove('cr-active');
                }
            }

            ensureStyles(doc);
            injectSuperNavTab(doc);
            injectBottomBarButton(doc);
            injectGameBadge(doc);
        }

        function runInjections() {
            const docs = getAllSteamDocuments();
            for (const doc of docs) {
                try {
                    runInjectionsForDoc(doc);
                } catch (e) {
                    console.warn('[CloudRedirect] Injection error:', e);
                }
            }
        }

        function setupObserver() {
            runInjections();

            const observedDocs = new WeakSet();
            function registerDocObserver(d) {
                if (!d || !d.body || observedDocs.has(d)) return;
                observedDocs.add(d);
                try {
                    const observer = new MutationObserver(() => {
                        runInjectionsForDoc(d);
                    });
                    observer.observe(d.body, {
                        childList: true,
                        subtree: true
                    });
                } catch (e) { }
            }

            // Periodically check all windows (including after page transitions)
            setInterval(() => {
                const docs = getAllSteamDocuments();
                for (const d of docs) {
                    registerDocObserver(d);
                }
                runInjections();
            }, 800);

            function hookSteamRootMenu(popup) {
                try {
                    const r = popup?.m_popup?.document || popup?.document || popup?.window?.document;
                    if (!r) return;
                    setTimeout(() => {
                        if (r.getElementById('cr-root-menu-item')) return;
                        const menuItems = r.querySelectorAll('div#popup_target div[role=""menuitem""]');
                        if (menuItems.length === 0) return;
                        const lastItem = menuItems[menuItems.length - 1];
                        const parent = lastItem?.parentNode;
                        if (!parent) return;

                        const crItem = lastItem.cloneNode(true);
                        crItem.id = 'cr-root-menu-item';
                        crItem.textContent = 'CloudRedirect';
                        crItem.onclick = (ev) => {
                            ev.stopPropagation();
                            launchApp(r);
                        };
                        parent.insertBefore(crItem, lastItem);
                    }, 50);
                } catch (e) { }
            }

            try {
                if (typeof Millennium !== 'undefined' && typeof Millennium.AddWindowCreateHook === 'function') {
                    Millennium.AddWindowCreateHook((popup) => {
                        if (popup && (popup.m_strTitle === 'Steam Root Menu' || popup.title === 'Steam Root Menu')) {
                            hookSteamRootMenu(popup);
                        }
                        setTimeout(() => runInjections(), 300);
                        setTimeout(() => runInjections(), 1500);
                    });
                }
            } catch (e) { }

            window.addEventListener(""millennium-main-window-ready"", () => {
                setTimeout(() => runInjections(), 300);
            });
        }

        const index = async function PluginMain() {
            setupObserver();

            try {
                if (typeof MILLENNIUM_BACKEND_IPC !== 'undefined' && MILLENNIUM_BACKEND_IPC.postMessage) {
                    MILLENNIUM_BACKEND_IPC.postMessage(1, { pluginName: pluginName });
                }
            } catch (e) { }

            return {
                title: ""CloudRedirect"",
                content: () => {
                    const div = document.createElement('div');
                    div.style.padding = '20px';
                    div.style.color = '#c6d4df';
                    div.innerHTML = `
                        <h2 style=""color: #66c0f4; margin-bottom: 8px;"">CloudRedirect Steam Integration</h2>
                        <p style=""margin-bottom: 16px; color: #8f98a0;"">Universal cloud save redirection and automated cloud backup for Steam games.</p>
                        <button class=""cr-btn-primary"" style=""max-width: 240px;"" onclick=""window.open('cloudredirect://open')"">Open CloudRedirect App</button>
                    `;
                    return div;
                },
                onDismount() {
                    const docs = getAllSteamDocuments();
                    for (const d of docs) {
                        try {
                            const superTab = d.getElementById('cloudredirect-supernav-item');
                            if (superTab) superTab.remove();
                            const topBtn = d.getElementById('cloudredirect-header-btn');
                            if (topBtn) topBtn.remove();
                            d.querySelectorAll('.cr-nav-btn, [id*=""cloudredirect-header""], .cr-dropdown-menu').forEach(el => el.remove());
                            const btmBtn = d.getElementById('cloudredirect-bottom-btn');
                            if (btmBtn) btmBtn.remove();
                            const style = d.getElementById('cr-millennium-styles');
                            if (style) style.remove();
                        } catch (e) { }
                    }
                }
            };
        };

        exports.default = index;
        Object.defineProperty(exports, '__esModule', { value: true });
        return exports;
    })({}, window.MILLENNIUM_API, window.SP_REACTDOM, window.SP_REACT);

    return millennium_main;
};

if (typeof window !== 'undefined') {
    window.PluginEntryPointMain = PluginEntryPointMain;
}

async function ExecutePluginModule() {
    try {
        let PluginModule = PluginEntryPointMain();
        if (typeof window !== 'undefined' && window.PLUGIN_LIST) {
            Object.assign(window.PLUGIN_LIST[pluginName], {
                ...PluginModule,
                __millennium_internal_plugin_name_do_not_use_or_change__: pluginName,
            });
        }
        if (PluginModule && typeof PluginModule.default === 'function') {
            let pluginProps = await PluginModule.default();
            function isValidSidebarNavComponent(obj) {
                return obj && obj.title !== undefined && obj.content !== undefined;
            }
            if (pluginProps && isValidSidebarNavComponent(pluginProps)) {
                if (typeof window !== 'undefined' && window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS) {
                    window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS[pluginName] = pluginProps;
                }
            }
        }
        if (MILLENNIUM_IS_CLIENT_MODULE && typeof MILLENNIUM_BACKEND_IPC !== 'undefined' && MILLENNIUM_BACKEND_IPC.postMessage) {
            MILLENNIUM_BACKEND_IPC.postMessage(1, { pluginName: pluginName });
        }
    } catch (e) {
        console.warn('[CloudRedirect] ExecutePluginModule error:', e);
    }
}
ExecutePluginModule();
";

    #endregion
}
