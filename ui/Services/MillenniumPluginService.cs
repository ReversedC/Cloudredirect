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
                appPath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath
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
.cr-supernav-menu {
    cursor: pointer !important;
    user-select: none !important;
    display: inline-flex !important;
    align-items: center !important;
    height: 100% !important;
    margin: 0 !important;
    padding: 0 !important;
    position: relative !important;
    transition: all 0.2s ease !important;
    vertical-align: middle !important;
}

.cr-supernav-btn {
    display: inline-flex !important;
    align-items: center !important;
    gap: 7px !important;
    cursor: pointer !important;
    font-family: ""Motiva Sans"", ""Twemoji"", ""Noto Sans"", Helvetica, sans-serif !important;
    font-size: 18px !important;
    font-weight: 500 !important;
    text-transform: uppercase !important;
    color: #dcdedf !important;
    padding: 0 10px !important;
    height: 100% !important;
    box-sizing: border-box !important;
    transition: color 0.15s ease-out, text-shadow 0.15s ease-out !important;
}

.cr-supernav-menu:hover .cr-supernav-btn {
    color: #ffffff !important;
    text-shadow: 0 0 10px rgba(255, 255, 255, 0.45) !important;
}

.cr-supernav-label {
    font-family: inherit !important;
    font-size: inherit !important;
    font-weight: inherit !important;
    line-height: inherit !important;
    text-transform: uppercase !important;
}

.cr-supernav-dot {
    width: 6px !important;
    height: 6px !important;
    background: #a4d007 !important;
    border-radius: 50% !important;
    box-shadow: 0 0 6px #a4d007 !important;
    display: inline-block !important;
    flex-shrink: 0 !important;
    margin-left: 2px !important;
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
            if (doc.getElementById('cr-millennium-styles')) return;
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
                .cr-supernav-menu {
                    cursor: pointer !important;
                    user-select: none !important;
                    display: inline-flex !important;
                    align-items: center !important;
                    height: 100% !important;
                    margin: 0 !important;
                    padding: 0 !important;
                    position: relative !important;
                    transition: all 0.2s ease !important;
                    vertical-align: middle !important;
                }
                .cr-supernav-btn {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 7px !important;
                    cursor: pointer !important;
                    font-family: ""Motiva Sans"", ""Twemoji"", ""Noto Sans"", Helvetica, sans-serif !important;
                    font-size: 18px !important;
                    font-weight: 500 !important;
                    text-transform: uppercase !important;
                    color: #dcdedf !important;
                    padding: 0 10px !important;
                    height: 100% !important;
                    box-sizing: border-box !important;
                    transition: color 0.15s ease-out, text-shadow 0.15s ease-out !important;
                }
                .cr-supernav-menu:hover .cr-supernav-btn {
                    color: #ffffff !important;
                    text-shadow: 0 0 10px rgba(255, 255, 255, 0.45) !important;
                }
                .cr-supernav-label {
                    font-family: inherit !important;
                    font-size: inherit !important;
                    font-weight: inherit !important;
                    line-height: inherit !important;
                    text-transform: uppercase !important;
                }
                .cr-supernav-dot {
                    width: 6px !important;
                    height: 6px !important;
                    background: #a4d007 !important;
                    border-radius: 50% !important;
                    box-shadow: 0 0 6px #a4d007 !important;
                    display: inline-block !important;
                    flex-shrink: 0 !important;
                    margin-left: 2px !important;
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
                            let passedCommunity = false;

                            for (const child of children) {
                                const cText = (child.textContent || '').trim().toUpperCase();
                                if (cText.includes('COMMUNITY')) {
                                    sampleTab = child;
                                    lastNavTab = child;
                                    passedCommunity = true;
                                } else if (cText.includes('STORE') || cText.includes('LIBRARY')) {
                                    if (!sampleTab) sampleTab = child;
                                    if (!passedCommunity) lastNavTab = child;
                                } else if (passedCommunity) {
                                    if (cText.length > 0 && !cText.includes('HTTP') && !cText.includes('🔍') && !cText.includes('SEARCH') && !cText.includes('✕')) {
                                        lastNavTab = child;
                                    }
                                    break;
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

        // Injects tab right into STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT row
        function injectSuperNavTab(doc) {
            if (!doc || !doc.body) return;
            if (doc.getElementById('cloudredirect-supernav-item')) return;

            const navInfo = findSuperNavInfo(doc);
            if (!navInfo || !navInfo.container || !navInfo.insertAfter) return;

            const { container, sampleTab, insertAfter } = navInfo;

            // Create the CloudRedirect SuperNav item
            const navItem = doc.createElement('div');
            navItem.id = 'cloudredirect-supernav-item';
            navItem.className = (sampleTab.className || '').replace(/\bactive\b/gi, '').trim() + ' cr-supernav-menu';
            navItem.setAttribute('role', 'button');
            navItem.setAttribute('tabindex', '0');
            navItem.title = 'CloudRedirect v2.9.73 (Save Protection Active - Click to Open App)';

            // Find child button / inner element if present in sampleTab
            const sampleInner = sampleTab.querySelector('div, a, span') || sampleTab;
            const innerBtn = doc.createElement('div');
            innerBtn.className = (sampleInner.className || '').replace(/\bactive\b/gi, '').trim() + ' cr-supernav-btn';
            innerBtn.innerHTML = `
                <span class=""cr-supernav-label"">CLOUDREDIRECT</span>
                <span class=""cr-supernav-dot"" title=""Save Protection Active""></span>
            `;

            // Dynamically copy font-size, font-weight, font-family from sampleTab / sampleInner if present to perfectly match
            try {
                const sampleTarget = sampleInner || sampleTab;
                const win = doc.defaultView || window;
                if (win && sampleTarget) {
                    const computed = win.getComputedStyle(sampleTarget);
                    if (computed) {
                        if (computed.fontSize) {
                            innerBtn.style.setProperty('font-size', computed.fontSize, 'important');
                        }
                        if (computed.fontWeight) {
                            innerBtn.style.setProperty('font-weight', computed.fontWeight, 'important');
                        }
                        if (computed.fontFamily) {
                            innerBtn.style.setProperty('font-family', computed.fontFamily, 'important');
                        }
                        if (computed.letterSpacing) {
                            innerBtn.style.setProperty('letter-spacing', computed.letterSpacing, 'important');
                        }
                    }
                }
            } catch (e) { }

            navItem.appendChild(innerBtn);

            // Directly launch CloudRedirect application when tab is clicked
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
            btn.title = 'CloudRedirect v2.9.73 (Save Protection Active - Click to Open App)';
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

            // Remove any stuck cr-active class on supernav item
            const superItem = doc.getElementById('cloudredirect-supernav-item');
            if (superItem) superItem.classList.remove('cr-active');

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
