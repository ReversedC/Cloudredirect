using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        return GetMillenniumDir() != null;
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
                appPath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath,
                enableGameSpecChecker = AppSettings.EnableGameSpecChecker,
                pcSpecs = HardwareSpecService.GetPcSpecs()
            };

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(statusFile, json);
        }
        catch { }
    }

    /// <summary>
    /// Synchronizes the Millennium plugin state with user preference in AppSettings.
    /// </summary>
    public static void SyncWithSettings()
    {
        try
        {
            if (!IsMillenniumInstalled())
            {
                return;
            }

            if (AppSettings.EnableMillenniumPlugin)
            {
                DeployPlugin();
            }
            else
            {
                RemovePlugin();
            }

            UpdatePluginStatus(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MillenniumPluginService] SyncWithSettings error: {ex}");
        }
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

            // 2. Write backend/main.lua
            File.WriteAllText(Path.Combine(backendDir, "main.lua"), GetResourceContent("backend.main.lua", BackendLuaContent));

            // 3. Write style.css
            File.WriteAllText(Path.Combine(pluginDir, "style.css"), GetResourceContent("style.css", StyleCssContent));

            // 4. Write index.js (root and .millennium/Dist)
            string rootJs = GetResourceContent("MillenniumPlugin.index.js", FrontendJsContent);
            string distJs = GetResourceContent("Dist.index.js", rootJs);
            File.WriteAllText(Path.Combine(pluginDir, "index.js"), rootJs);
            File.WriteAllText(Path.Combine(distDir, "index.js"), distJs);

            // 5. Update millennium/config/config.json enabledPlugins
            EnableInMillenniumConfig();

            // 6. Register URL scheme and write initial status
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
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                return;

            var text = File.ReadAllText(configPath);
            var node = JsonNode.Parse(text);
            if (node == null) return;

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
                using var stream = File.Create(configPath);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                node.WriteTo(writer);
            }
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

/* ==========================================================================
   GAME SPEC CHECKER STYLES (Store Search Rows, Game Lists, Game Details)
   ========================================================================== */

/* Search Result Row Indicator (Next to Win/Mac/Linux icons) */
.cr-spec-indicator {
    display: inline-flex !important;
    align-items: center !important;
    justify-content: center !important;
    vertical-align: middle !important;
    margin-left: 6px !important;
    cursor: pointer !important;
    position: relative !important;
    line-height: normal !important;
    user-select: none !important;
    padding: 2px 5px !important;
    border-radius: 10px !important;
    background: rgba(0, 0, 0, 0.45) !important;
    border: 1px solid rgba(255, 255, 255, 0.14) !important;
    transition: transform 0.15s ease, background 0.15s ease, border-color 0.15s ease !important;
    z-index: 5 !important;
}

.cr-spec-indicator:hover {
    transform: scale(1.18) !important;
    background: rgba(14, 22, 33, 0.95) !important;
    border-color: rgba(102, 192, 244, 0.6) !important;
}

.cr-spec-dot {
    width: 8px !important;
    height: 8px !important;
    border-radius: 50% !important;
    display: inline-block !important;
    flex-shrink: 0 !important;
    transition: all 0.2s ease !important;
}

.cr-spec-dot.cr-green {
    background-color: #00ff88 !important;
    box-shadow: 0 0 6px #00ff88, 0 0 12px rgba(0, 255, 136, 0.5) !important;
}

.cr-spec-dot.cr-yellow {
    background-color: #ffd700 !important;
    box-shadow: 0 0 6px #ffd700, 0 0 12px rgba(255, 215, 0, 0.5) !important;
}

.cr-spec-dot.cr-red {
    background-color: #ff3344 !important;
    box-shadow: 0 0 6px #ff3344, 0 0 12px rgba(255, 51, 68, 0.5) !important;
}

.cr-spec-dot.cr-loading {
    background-color: #66c0f4 !important;
    opacity: 0.7 !important;
    animation: cr-pulse 1.2s infinite ease-in-out !important;
}

@keyframes cr-pulse {
    0%, 100% { opacity: 0.3; transform: scale(0.85); }
    50% { opacity: 1; transform: scale(1.15); }
}

/* Tooltip Popup for Spec Breakdown */
.cr-spec-tooltip {
    position: fixed;
    z-index: 1000000;
    width: 310px;
    background: #141c26;
    border: 1px solid #2d4560;
    border-radius: 6px;
    padding: 12px 14px;
    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.8), 0 0 20px rgba(102, 192, 244, 0.25);
    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    font-size: 11px;
    color: #c6d4df;
    pointer-events: none;
    opacity: 0;
    transition: opacity 0.15s ease;
}

.cr-spec-tooltip.cr-visible {
    opacity: 1;
}

.cr-spec-tooltip-header {
    display: flex;
    align-items: center;
    gap: 8px;
    font-weight: 700;
    font-size: 12px;
    padding-bottom: 8px;
    margin-bottom: 8px;
    border-bottom: 1px solid rgba(255, 255, 255, 0.12);
}

.cr-spec-tooltip-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin: 5px 0;
    font-size: 11px;
}

.cr-spec-tooltip-label {
    color: #8f98a0;
    font-weight: 500;
}

.cr-spec-tooltip-val {
    font-weight: 600;
    color: #ffffff;
    max-width: 185px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    text-align: right;
}

.cr-spec-tooltip-verdict {
    margin-top: 10px;
    padding-top: 8px;
    border-top: 1px solid rgba(255, 255, 255, 0.12);
    font-weight: 600;
    font-size: 11px;
    line-height: 1.35;
}

.cr-spec-tooltip-verdict.cr-green { color: #00ff88; }
.cr-spec-tooltip-verdict.cr-yellow { color: #ffd700; }
.cr-spec-tooltip-verdict.cr-red { color: #ff5566; }

/* Large Header Badge (in Library Game Details Page) */
.cr-game-spec-badge {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    background: rgba(20, 32, 44, 0.85);
    border: 1px solid #2d4c6b;
    border-radius: 14px;
    padding: 4px 12px;
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

.cr-game-spec-badge:hover {
    background: #1e3347;
    border-color: #66c0f4;
    box-shadow: 0 0 12px rgba(102, 192, 244, 0.4);
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

        // User PC specs (defaults to profiled hardware: Ryzen 5 5600GT, 16GB RAM, Radeon Graphics)
        let g_userSpecs = {
            cpu: ""AMD Ryzen 5 5600GT with Radeon Graphics"",
            cpuCores: 6,
            ramGb: 16.0,
            gpu: ""AMD Radeon(TM) Graphics"",
            vramGb: 2.0,
            isIntegratedGpu: true,
            os: ""Windows 64-bit""
        };

        // Game Spec Checker toggle: default is ON (true)
        let g_enableSpecChecker = true;
        try {
            const cachedToggle = localStorage.getItem('cr_enable_game_spec_checker');
            if (cachedToggle !== null) {
                g_enableSpecChecker = (cachedToggle === 'true');
            }
        } catch (e) { }

        // Fetch status from CloudRedirect backend
        async function refreshStatus() {
            try {
                const res = await __call_server_method__(""get_status"", {});
                if (res && res.data) {
                    const data = typeof res.data === 'string' ? JSON.parse(res.data) : res.data;
                    if (data.pcSpecs) {
                        g_userSpecs = Object.assign(g_userSpecs, data.pcSpecs);
                    }
                    if (typeof data.enableGameSpecChecker === 'boolean') {
                        if (g_enableSpecChecker !== data.enableGameSpecChecker) {
                            g_enableSpecChecker = data.enableGameSpecChecker;
                            try { localStorage.setItem('cr_enable_game_spec_checker', String(g_enableSpecChecker)); } catch (e) { }
                            // Re-evaluate or remove badges on all active documents
                            runInjections();
                        }
                    }
                }
            } catch (e) { }
        }

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

                /* Game Detail Page Cloud Badge */
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

                /* ==========================================================================
                   GAME SPEC CHECKER STYLES (Store Search Rows, Game Lists, Game Details)
                   ========================================================================== */
                .cr-spec-indicator {
                    display: inline-flex !important;
                    align-items: center !important;
                    justify-content: center !important;
                    vertical-align: middle !important;
                    margin-left: 6px !important;
                    cursor: pointer !important;
                    position: relative !important;
                    line-height: normal !important;
                    user-select: none !important;
                    padding: 2px 5px !important;
                    border-radius: 10px !important;
                    background: rgba(0, 0, 0, 0.45) !important;
                    border: 1px solid rgba(255, 255, 255, 0.14) !important;
                    transition: transform 0.15s ease, background 0.15s ease, border-color 0.15s ease !important;
                    z-index: 5 !important;
                }

                .cr-spec-indicator:hover {
                    transform: scale(1.18) !important;
                    background: rgba(14, 22, 33, 0.95) !important;
                    border-color: rgba(102, 192, 244, 0.6) !important;
                }

                .cr-spec-dot {
                    width: 8px !important;
                    height: 8px !important;
                    border-radius: 50% !important;
                    display: inline-block !important;
                    flex-shrink: 0 !important;
                    transition: all 0.2s ease !important;
                }

                .cr-spec-dot.cr-green {
                    background-color: #00ff88 !important;
                    box-shadow: 0 0 6px #00ff88, 0 0 12px rgba(0, 255, 136, 0.5) !important;
                }

                .cr-spec-dot.cr-yellow {
                    background-color: #ffd700 !important;
                    box-shadow: 0 0 6px #ffd700, 0 0 12px rgba(255, 215, 0, 0.5) !important;
                }

                .cr-spec-dot.cr-red {
                    background-color: #ff3344 !important;
                    box-shadow: 0 0 6px #ff3344, 0 0 12px rgba(255, 51, 68, 0.5) !important;
                }

                .cr-spec-dot.cr-loading {
                    background-color: #66c0f4 !important;
                    opacity: 0.7 !important;
                    animation: cr-pulse 1.2s infinite ease-in-out !important;
                }

                @keyframes cr-pulse {
                    0%, 100% { opacity: 0.3; transform: scale(0.85); }
                    50% { opacity: 1; transform: scale(1.15); }
                }

                /* Tooltip Popup for Spec Breakdown */
                .cr-spec-tooltip {
                    position: fixed !important;
                    z-index: 1000000 !important;
                    width: 320px !important;
                    background: #141c26 !important;
                    border: 1px solid #2d4560 !important;
                    border-radius: 6px !important;
                    padding: 12px 14px !important;
                    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.8), 0 0 20px rgba(102, 192, 244, 0.25) !important;
                    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif !important;
                    font-size: 11px !important;
                    color: #c6d4df !important;
                    pointer-events: none !important;
                    opacity: 0 !important;
                    transition: opacity 0.15s ease !important;
                }

                .cr-spec-tooltip.cr-visible {
                    opacity: 1 !important;
                }

                .cr-spec-tooltip-header {
                    display: flex !important;
                    align-items: center !important;
                    gap: 8px !important;
                    font-weight: 700 !important;
                    font-size: 12px !important;
                    padding-bottom: 8px !important;
                    margin-bottom: 8px !important;
                    border-bottom: 1px solid rgba(255, 255, 255, 0.12) !important;
                }

                .cr-spec-tooltip-row {
                    display: flex !important;
                    justify-content: space-between !important;
                    align-items: center !important;
                    margin: 5px 0 !important;
                    font-size: 11px !important;
                }

                .cr-spec-tooltip-label {
                    color: #8f98a0 !important;
                    font-weight: 500 !important;
                }

                .cr-spec-tooltip-val {
                    font-weight: 600 !important;
                    color: #ffffff !important;
                    max-width: 195px !important;
                    overflow: hidden !important;
                    text-overflow: ellipsis !important;
                    white-space: nowrap !important;
                    text-align: right !important;
                }

                .cr-spec-tooltip-verdict {
                    margin-top: 10px !important;
                    padding-top: 8px !important;
                    border-top: 1px solid rgba(255, 255, 255, 0.12) !important;
                    font-weight: 600 !important;
                    font-size: 11px !important;
                    line-height: 1.35 !important;
                }

                .cr-spec-tooltip-verdict.cr-green { color: #00ff88 !important; }
                .cr-spec-tooltip-verdict.cr-yellow { color: #ffd700 !important; }
                .cr-spec-tooltip-verdict.cr-red { color: #ff5566 !important; }

                /* Large Header Badge (in Library Game Details Page) */
                .cr-game-spec-badge {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 7px !important;
                    background: rgba(20, 32, 44, 0.85) !important;
                    border: 1px solid #2d4c6b !important;
                    border-radius: 14px !important;
                    padding: 4px 12px !important;
                    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif !important;
                    font-size: 11px !important;
                    font-weight: 600 !important;
                    color: #c6d4df !important;
                    cursor: pointer !important;
                    transition: all 0.2s ease !important;
                    user-select: none !important;
                    margin-left: 10px !important;
                    vertical-align: middle !important;
                }

                .cr-game-spec-badge:hover {
                    background: #1e3347 !important;
                    border-color: #66c0f4 !important;
                    box-shadow: 0 0 12px rgba(102, 192, 244, 0.4) !important;
                }
            `;
            doc.head.appendChild(style);
        }

        // Helper to locate the exact SuperNav tab bar container in doc
        function findSuperNavInfo(doc) {
            const allEls = doc.querySelectorAll('div, a, span, button');

            for (const el of allEls) {
                const t = (el.textContent || '').trim().toUpperCase();
                if (t === 'COMMUNITY' || t === 'STORE' || t === 'LIBRARY') {
                    // Traverse up within 5 levels to locate the SuperNav container
                    let curr = el;
                    let depth = 0;
                    while (curr && curr !== doc.body && depth < 5) {
                        const parent = curr.parentElement;
                        if (!parent) break;
                        const pText = (parent.textContent || '').toUpperCase();
                        if (pText.includes('STORE') && pText.includes('LIBRARY') && pText.includes('COMMUNITY')) {
                            const children = Array.from(parent.children);
                            let sampleTab = null;
                            let lastNavTab = null;

                            for (let i = children.length - 1; i >= 0; i--) {
                                const child = children[i];
                                const cText = (child.textContent || '').trim().toUpperCase();
                                if (cText && (cText.includes('STORE') || cText.includes('LIBRARY') || cText.includes('COMMUNITY') || child.classList.contains('supernav_container') || child.classList.contains('supernav_content') || child.getAttribute('data-supernav'))) {
                                    sampleTab = child;
                                    break;
                                }
                            }

                            for (let i = children.length - 1; i >= 0; i--) {
                                const child = children[i];
                                if (child.id === 'cloudredirect-supernav-item') continue;
                                const cText = (child.textContent || '').trim().toUpperCase();
                                if (cText && !cText.includes('CLOSE') && !cText.includes('MINIMIZE') && !cText.includes('MAXIMIZE')) {
                                    lastNavTab = child;
                                    break;
                                }
                            }

                            if (parent && lastNavTab) {
                                return {
                                    container: parent,
                                    insertAfter: lastNavTab,
                                    sampleTab: sampleTab || lastNavTab
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

        // Inject CloudRedirect SuperNav Tab
        function injectSuperNavTab(doc) {
            if (!doc || !doc.body) return;
            if (doc.getElementById('cloudredirect-supernav-item')) return;

            const info = findSuperNavInfo(doc);
            if (!info || !info.container || !info.insertAfter) return;

            const { container, insertAfter, sampleTab } = info;

            const navItem = doc.createElement('div');
            navItem.id = 'cloudredirect-supernav-item';
            navItem.tabIndex = 0;
            navItem.setAttribute('role', 'button');
            navItem.title = 'CloudRedirect v2.9.75 (Click to Open)';

            if (sampleTab) {
                navItem.className = sampleTab.className || '';
                const btnChild = sampleTab.querySelector('a, div, span, button');
                if (btnChild) {
                    navItem.classList.add('cr-supernav-menu');
                }
            }
            if (!navItem.className) {
                navItem.className = 'supernav_container cr-supernav-menu';
            }

            let innerBtnClass = 'cr-supernav-btn menuitem';
            if (sampleTab) {
                const sampleBtn = sampleTab.querySelector('a, div, span, button');
                if (sampleBtn && sampleBtn.className) {
                    innerBtnClass = sampleBtn.className + ' cr-supernav-btn';
                }
            }

            navItem.innerHTML = `
                <a class=""${innerBtnClass}"">
                    <span class=""cr-supernav-label"">CLOUDREDIRECT</span>
                    <span class=""cr-supernav-dot""></span>
                </a>
            `;

            navItem.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                launchApp(doc);
            };
            navItem.onkeydown = (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    launchApp(doc);
                }
            };

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

            const allElements = doc.querySelectorAll('button, div, a');
            for (const el of allElements) {
                const text = el.textContent || '';
                if (text.includes('Steam Unlock') || (el.className && typeof el.className === 'string' && el.className.includes('activation'))) {
                    targetSibling = el;
                    parentContainer = el.parentNode;
                    break;
                }
            }

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

            if (!parentContainer) {
                parentContainer = doc.querySelector('div[class*=""bottombar_""], div[class*=""bottombarcontrols_""], footer, .bottom_bar');
            }

            if (!parentContainer) return;

            const btn = doc.createElement('div');
            btn.id = 'cloudredirect-bottom-btn';
            btn.className = 'cr-bottom-bar-btn';
            btn.title = 'CloudRedirect v2.9.75 (Save Protection Active - Click to Open App)';
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

        // 2. Inject Game Details Page Cloud Badge
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

        // ==========================================================================
        // 3. GAME SYSTEM REQUIREMENTS COMPATIBILITY CHECKER
        // ==========================================================================

        function parseSpecs(html) {
            if (!html) return {};
            const items = [];
            const re = /<strong>\s*([^:<]+?)\s*:\s*<\/strong>\s*([^<\n\r]+)/gi;
            let match;
            while ((match = re.exec(html)) !== null) {
                items.push([match[1], match[2]]);
            }
            const data = {};
            for (const [kRaw, vRaw] of items) {
                const k = kRaw.toLowerCase().trim();
                const v = vRaw.trim();
                if (k.includes('memory') || k.includes('ram')) {
                    data.ramText = v;
                    const mGb = v.match(/(\d+(?:\.\d+)?)\s*GB/i);
                    const mMb = v.match(/(\d+(?:\.\d+)?)\s*MB/i);
                    if (mGb) data.ramGb = parseFloat(mGb[1]);
                    else if (mMb) data.ramGb = Math.round((parseFloat(mMb[1]) / 1024) * 100) / 100;
                } else if (k.includes('graphics') || k.includes('video')) {
                    data.gpuText = v;
                    const mVram = v.match(/(\d+(?:\.\d+)?)\s*GB/i);
                    if (mVram) data.vramGb = parseFloat(mVram[1]);
                } else if (k.includes('processor') || k.includes('cpu')) {
                    data.cpuText = v;
                } else if (k.includes('storage') || k.includes('disk') || k.includes('space')) {
                    data.storageText = v;
                } else if (k.includes('os')) {
                    data.osText = v;
                }
            }
            return data;
        }

        function evaluateSpecs(minHtml, recHtml, user) {
            if (!minHtml && !recHtml) {
                return {
                    rating: 'GREEN',
                    dotClass: 'cr-green',
                    title: '🟢 Perfect (Max Graphics)',
                    verdict: 'Lightweight game. Smoothly runs at maximum graphics settings (60+ FPS).',
                    cpuPass: true,
                    gpuPass: true,
                    ramPass: true,
                    min: {},
                    rec: {}
                };
            }

            const min = parseSpecs(minHtml);
            const rec = parseSpecs(recHtml);

            const minRam = min.ramGb || 2.0;
            const recRam = rec.ramGb || minRam;

            const minGpu = (min.gpuText || '').toLowerCase();
            const recGpu = (rec.gpuText || '').toLowerCase();

            const heavyKeywords = ['rtx 30', 'rtx 40', 'rtx 2080', 'rx 6800', 'rx 6700', 'gtx 1080', 'gtx 1070', 'gtx 1060 6gb', 'rx 580 8gb', 'rtx 2060'];
            const midKeywords = ['gtx 1050', 'gtx 960', 'gtx 750', 'gtx 1650', 'rx 560', 'rx 570', 'rx 460', 'radeon vega', 'intel arc', 'gtx 1060 3gb', 'gtx 1060'];

            const isMinHeavy = heavyKeywords.some(k => minGpu.includes(k)) || (min.vramGb && min.vramGb >= 6);
            const isMinMid = midKeywords.some(k => minGpu.includes(k)) || (min.vramGb && min.vramGb >= 3);

            const isRecHeavy = heavyKeywords.some(k => recGpu.includes(k)) || (rec.vramGb && rec.vramGb >= 6);
            const isRecMid = midKeywords.some(k => recGpu.includes(k)) || (rec.vramGb && rec.vramGb >= 3);

            let rating = 'GREEN';
            let dotClass = 'cr-green';
            let title = '🟢 Perfect! Can run at Maximum graphic settings';
            let verdict = 'Passes all requirements! Smoothly runs at maximum graphics settings (60+ FPS).';
            let cpuPass = true;
            let gpuPass = true;
            let ramPass = true;

            if (user.ramGb < minRam) {
                rating = 'RED';
                dotClass = 'cr-red';
                title = '🔴 Not supported / Lagging';
                verdict = `System RAM (${user.ramGb}GB) is below minimum required (${minRam}GB). Expect lagging or crashes.`;
                ramPass = false;
            } else if (user.isIntegratedGpu) {
                if (isMinHeavy) {
                    rating = 'RED';
                    dotClass = 'cr-red';
                    title = '🔴 Not supported / Lagging';
                    verdict = 'Requires dedicated 6GB+ gaming graphics card. Integrated GPU will experience heavy lag.';
                    gpuPass = false;
                } else if (isMinMid || isRecHeavy) {
                    rating = 'YELLOW';
                    dotClass = 'cr-yellow';
                    title = '🟡 Passes minimum requirement (Low/Medium)';
                    verdict = 'Passes minimum requirements! Playable at Low to Medium graphic settings (30–60 FPS).';
                    gpuPass = 'warn';
                } else if (minRam <= 8.0 && !isMinHeavy && !isMinMid) {
                    rating = 'GREEN';
                    dotClass = 'cr-green';
                    title = '🟢 Perfect! Can run at Maximum graphic settings';
                    verdict = 'Lightweight requirements! Perfect performance at High/Maximum graphic settings (60+ FPS).';
                } else {
                    rating = 'YELLOW';
                    dotClass = 'cr-yellow';
                    title = '🟡 Passes minimum requirement (Low/Medium)';
                    verdict = 'Passes minimum requirements! Recommended for Low/Medium graphic settings.';
                    gpuPass = 'warn';
                }
            }

            return {
                rating,
                dotClass,
                title,
                verdict,
                cpuPass,
                gpuPass,
                ramPass,
                min,
                rec
            };
        }

        const g_specCache = new Map();
        const g_fetchQueue = [];
        let g_isFetchingQueue = false;

        async function getGameSpecsAsync(appId) {
            if (!appId) return null;
            const cacheKey = 'cr_spec_cache_' + appId;
            if (g_specCache.has(appId)) {
                return g_specCache.get(appId);
            }

            try {
                const stored = localStorage.getItem(cacheKey);
                if (stored) {
                    const parsed = JSON.parse(stored);
                    g_specCache.set(appId, parsed);
                    return parsed;
                }
            } catch (e) { }

            return new Promise((resolve) => {
                g_fetchQueue.push({ appId, resolve });
                processFetchQueue();
            });
        }

        async function processFetchQueue() {
            if (g_isFetchingQueue || g_fetchQueue.length === 0) return;
            g_isFetchingQueue = true;

            while (g_fetchQueue.length > 0) {
                const item = g_fetchQueue.shift();
                const { appId, resolve } = item;
                try {
                    const res = await fetch(`https://store.steampowered.com/api/appdetails?appids=${appId}`);
                    if (res.ok) {
                        const data = await res.json();
                        const appData = data && data[appId] && data[appId].data;
                        const pcReq = appData && appData.pc_requirements;
                        const min = (pcReq && pcReq.minimum) || '';
                        const rec = (pcReq && pcReq.recommended) || '';
                        const gameName = (appData && appData.name) || `App ${appId}`;
                        const evalResult = evaluateSpecs(min, rec, g_userSpecs);
                        evalResult.gameName = gameName;
                        evalResult.appId = appId;

                        g_specCache.set(appId, evalResult);
                        try {
                            localStorage.setItem('cr_spec_cache_' + appId, JSON.stringify(evalResult));
                        } catch (e) { }

                        resolve(evalResult);
                    } else {
                        resolve(null);
                    }
                } catch (e) {
                    resolve(null);
                }
                // Polite delay between Store API calls
                await new Promise(r => setTimeout(r, 60));
            }

            g_isFetchingQueue = false;
        }

        function getOrCreateTooltip(doc) {
            let tooltip = doc.getElementById('cr-spec-tooltip');
            if (!tooltip) {
                tooltip = doc.createElement('div');
                tooltip.id = 'cr-spec-tooltip';
                tooltip.className = 'cr-spec-tooltip';
                doc.body.appendChild(tooltip);
            }
            return tooltip;
        }

        function showSpecTooltip(targetEl, spec, doc) {
            if (!spec || !doc) return;
            const tooltip = getOrCreateTooltip(doc);
            const rect = targetEl.getBoundingClientRect();

            const minRam = spec.min && spec.min.ramGb ? `${spec.min.ramGb} GB` : '2 GB';
            const userRam = `${g_userSpecs.ramGb} GB`;

            const ramStatus = spec.ramPass ? '✓ Pass' : '✗ Low RAM';
            const gpuStatus = spec.gpuPass === true ? '✓ Max' : (spec.gpuPass === 'warn' ? '⚡ Playable' : '✗ Low GPU');
            const cpuStatus = spec.cpuPass ? '✓ Max' : '✗ Low CPU';

            tooltip.innerHTML = `
                <div class=""cr-spec-tooltip-header"">
                    <span class=""cr-spec-dot ${spec.dotClass}""></span>
                    <span style=""color:#ffffff;"">${spec.gameName || 'Game Compatibility'}</span>
                </div>
                <div class=""cr-spec-tooltip-row"">
                    <span class=""cr-spec-tooltip-label"">Rating:</span>
                    <span class=""cr-spec-tooltip-val ${spec.dotClass}"">${spec.title}</span>
                </div>
                <div class=""cr-spec-tooltip-row"">
                    <span class=""cr-spec-tooltip-label"">Your CPU:</span>
                    <span class=""cr-spec-tooltip-val"" title=""${g_userSpecs.cpu}"">${g_userSpecs.cpu.split('with')[0].trim()} (${cpuStatus})</span>
                </div>
                <div class=""cr-spec-tooltip-row"">
                    <span class=""cr-spec-tooltip-label"">Your RAM:</span>
                    <span class=""cr-spec-tooltip-val"">${userRam} (Min: ${minRam}) (${ramStatus})</span>
                </div>
                <div class=""cr-spec-tooltip-row"">
                    <span class=""cr-spec-tooltip-label"">Your GPU:</span>
                    <span class=""cr-spec-tooltip-val"" title=""${g_userSpecs.gpu}"">${g_userSpecs.gpu} (${gpuStatus})</span>
                </div>
                <div class=""cr-spec-tooltip-verdict ${spec.dotClass}"">
                    ${spec.verdict}
                </div>
                <div style=""margin-top:8px; font-size:10px; color:#5c6b79; display:flex; justify-content:space-between;"">
                    <span>CloudRedirect Spec Match</span>
                    <span style=""color:#66c0f4;"">Enabled</span>
                </div>
            `;

            const tipWidth = 320;
            let left = rect.left + (rect.width / 2) - (tipWidth / 2);
            if (left < 10) left = 10;
            if (left + tipWidth > window.innerWidth - 10) left = window.innerWidth - tipWidth - 10;

            let top = rect.top - 185;
            if (top < 10) {
                top = rect.bottom + 8;
            }

            tooltip.style.left = `${left}px`;
            tooltip.style.top = `${top}px`;
            tooltip.classList.add('cr-visible');
        }

        function hideSpecTooltip(doc) {
            if (!doc) return;
            const tooltip = doc.getElementById('cr-spec-tooltip');
            if (tooltip) {
                tooltip.classList.remove('cr-visible');
            }
        }

        // Injects spec dot into Steam Store search result rows (.search_result_row .search_platforms)
        function injectStoreSearchSpecIndicators(doc) {
            if (!g_enableSpecChecker) {
                doc.querySelectorAll('.cr-spec-indicator, .cr-game-spec-badge').forEach(el => el.remove());
                return;
            }

            const rows = doc.querySelectorAll('a.search_result_row');
            rows.forEach(row => {
                const platforms = row.querySelector('.search_platforms');
                if (!platforms) return;
                if (platforms.querySelector('.cr-spec-indicator')) return;

                const appId = row.getAttribute('data-ds-appid') || row.getAttribute('data-ds-packageid');
                if (!appId || !/^\d+$/.test(appId)) return;

                const indicator = doc.createElement('div');
                indicator.className = 'cr-spec-indicator';
                indicator.setAttribute('data-cr-appid', appId);
                indicator.title = 'CloudRedirect: Checking PC spec compatibility...';
                indicator.innerHTML = '<span class=""cr-spec-dot cr-loading""></span>';

                indicator.onclick = (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    launchApp(doc);
                };

                indicator.onmouseleave = () => {
                    hideSpecTooltip(doc);
                };

                platforms.appendChild(indicator);

                getGameSpecsAsync(appId).then(spec => {
                    if (!spec) {
                        indicator.innerHTML = '<span class=""cr-spec-dot cr-green"" title=""Standard requirements""></span>';
                        return;
                    }
                    indicator.innerHTML = `<span class=""cr-spec-dot ${spec.dotClass}""></span>`;
                    indicator.title = `${spec.title}\n${spec.verdict}\n(Click to open CloudRedirect)`;

                    indicator.onmouseenter = () => {
                        showSpecTooltip(indicator, spec, doc);
                    };
                }).catch(() => {
                    indicator.innerHTML = '<span class=""cr-spec-dot cr-green""></span>';
                });
            });
        }

        // Injects spec badge into Game Detail Pages
        function injectLibraryGameSpecBadge(doc) {
            if (!g_enableSpecChecker) return;

            const saveBadges = doc.querySelectorAll('.cr-game-badge');
            saveBadges.forEach(saveBadge => {
                const parent = saveBadge.parentNode;
                if (!parent || parent.querySelector('.cr-game-spec-badge')) return;

                let appId = null;
                const href = window.location.href;
                const match = href.match(/app\/(\d+)/i) || (doc.location && doc.location.href.match(/app\/(\d+)/i));
                if (match) appId = match[1];

                if (!appId) {
                    const dsEl = doc.querySelector('[data-appid], [data-ds-appid]');
                    if (dsEl) appId = dsEl.getAttribute('data-appid') || dsEl.getAttribute('data-ds-appid');
                }

                if (!appId) return;

                const specBadge = doc.createElement('div');
                specBadge.className = 'cr-game-spec-badge';
                specBadge.innerHTML = '<span class=""cr-spec-dot cr-loading""></span><span>Checking Specs...</span>';
                parent.insertBefore(specBadge, saveBadge.nextSibling);

                specBadge.onclick = (e) => {
                    e.stopPropagation();
                    launchApp(doc);
                };

                specBadge.onmouseleave = () => hideSpecTooltip(doc);

                getGameSpecsAsync(appId).then(spec => {
                    if (!spec) return;
                    specBadge.innerHTML = `<span class=""cr-spec-dot ${spec.dotClass}""></span><span>${spec.rating === 'GREEN' ? 'Max Settings' : (spec.rating === 'YELLOW' ? 'Playable (Low/Med)' : 'Below Min Specs')}</span>`;
                    specBadge.onmouseenter = () => showSpecTooltip(specBadge, spec, doc);
                });
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
            injectStoreSearchSpecIndicators(doc);
            injectLibraryGameSpecBadge(doc);
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
            refreshStatus();
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

            // Periodically check all windows & refresh status
            setInterval(() => {
                const docs = getAllSteamDocuments();
                for (const d of docs) {
                    registerDocObserver(d);
                }
                runInjections();
            }, 800);

            setInterval(() => {
                refreshStatus();
            }, 3000);

            try {
                if (typeof Millennium !== 'undefined' && typeof Millennium.AddWindowCreateHook === 'function') {
                    Millennium.AddWindowCreateHook((popup) => {
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
                        <p style=""margin-bottom: 16px; color: #8f98a0;"">Universal cloud save redirection, automated backup, and PC system specs compatibility check for Steam games.</p>
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
                            d.querySelectorAll('.cr-nav-btn, [id*=""cloudredirect-header""], .cr-dropdown-menu, .cr-spec-indicator, .cr-game-spec-badge, #cr-spec-tooltip').forEach(el => el.remove());
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
