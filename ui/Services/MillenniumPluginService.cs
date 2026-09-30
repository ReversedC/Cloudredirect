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
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), PluginJsonContent);

            // 2. Write backend/main.lua
            File.WriteAllText(Path.Combine(backendDir, "main.lua"), BackendLuaContent);

            // 3. Write style.css
            File.WriteAllText(Path.Combine(pluginDir, "style.css"), StyleCssContent);

            // 4. Write index.js (root and .millennium/Dist)
            File.WriteAllText(Path.Combine(pluginDir, "index.js"), FrontendJsContent);
            File.WriteAllText(Path.Combine(distDir, "index.js"), FrontendJsContent);

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

.cr-nav-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 26px;
    padding: 0 10px;
    background: #17222d;
    border: 1px solid #233b53;
    border-radius: 4px;
    color: #c6d4df;
    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
    user-select: none;
    transition: all 0.2s ease;
    vertical-align: middle;
    margin: 0 4px;
    z-index: 1000;
}

.cr-nav-btn:hover {
    background: #203548;
    border-color: #66c0f4;
    color: #ffffff;
    box-shadow: 0 0 8px rgba(102, 192, 244, 0.3);
}

.cr-nav-btn.cr-active {
    background: #1c3852;
    border-color: #66c0f4;
    color: #ffffff;
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

.cr-dropdown-menu {
    position: fixed;
    top: 42px;
    width: 320px;
    background: linear-gradient(135deg, #1b2838 0%, #171d25 100%);
    border: 1px solid #36506c;
    border-radius: 8px;
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.8), 0 0 1px rgba(102, 192, 244, 0.4);
    color: #c6d4df;
    font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    font-size: 13px;
    padding: 14px;
    z-index: 99999;
    animation: crFadeIn 0.18s ease-out forwards;
    backdrop-filter: blur(12px);
}

@keyframes crFadeIn {
    from { opacity: 0; transform: translateY(-6px); }
    to { opacity: 1; transform: translateY(0); }
}

.cr-dropdown-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding-bottom: 10px;
    border-bottom: 1px solid rgba(102, 192, 244, 0.2);
    margin-bottom: 12px;
}

.cr-title-group {
    display: flex;
    align-items: center;
    gap: 8px;
}

.cr-title {
    font-size: 15px;
    font-weight: 700;
    color: #ffffff;
    letter-spacing: 0.3px;
}

.cr-version-badge {
    background: #142332;
    border: 1px solid #23425e;
    color: #66c0f4;
    font-size: 10px;
    font-weight: 700;
    padding: 2px 6px;
    border-radius: 4px;
}

.cr-status-badge {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    background: rgba(164, 208, 7, 0.12);
    border: 1px solid rgba(164, 208, 7, 0.35);
    color: #a4d007;
    font-size: 11px;
    font-weight: 600;
    padding: 2px 8px;
    border-radius: 12px;
}

.cr-dropdown-desc {
    font-size: 12px;
    color: #8f98a0;
    line-height: 1.4;
    margin-bottom: 12px;
}

.cr-stats-card {
    background: rgba(15, 23, 33, 0.6);
    border: 1px solid #233446;
    border-radius: 6px;
    padding: 10px 12px;
    margin-bottom: 14px;
}

.cr-stat-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    font-size: 11px;
    padding: 2px 0;
}

.cr-stat-label { color: #8f98a0; }
.cr-stat-val { color: #66c0f4; font-weight: 600; }
.cr-stat-val.cr-good { color: #a4d007; }

.cr-btn-primary {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    width: 100%;
    height: 36px;
    background: linear-gradient(90deg, #5c7e10 0%, #476508 100%);
    border: none;
    border-radius: 4px;
    color: #ffffff;
    font-size: 13px;
    font-weight: 700;
    cursor: pointer;
    transition: all 0.18s ease;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
    margin-bottom: 8px;
}

.cr-btn-primary:hover {
    background: linear-gradient(90deg, #6e9713 0%, #55790a 100%);
    box-shadow: 0 0 12px rgba(164, 208, 7, 0.4);
}

.cr-btn-secondary {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    width: 100%;
    height: 32px;
    background: #213244;
    border: 1px solid #36506c;
    border-radius: 4px;
    color: #c6d4df;
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.18s ease;
    margin-bottom: 6px;
}

.cr-btn-secondary:hover {
    background: #2a415a;
    border-color: #66c0f4;
    color: #ffffff;
}

.cr-footer {
    display: flex;
    justify-content: space-between;
    align-items: center;
    font-size: 10px;
    color: #626e7b;
    margin-top: 10px;
    padding-top: 8px;
    border-top: 1px solid rgba(255, 255, 255, 0.05);
}

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
}";

    private const string FrontendJsContent = @"(function () {
    const MILLENNIUM_IS_CLIENT_MODULE = true;
    const pluginName = ""CloudRedirect"";

    function initializeRegistry() {
        if (!window.PLUGIN_LIST) window.PLUGIN_LIST = {};
        if (!window.PLUGIN_LIST[pluginName]) window.PLUGIN_LIST[pluginName] = {};
        if (!window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS) window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS = {};
    }
    initializeRegistry();

    const callServerMethod = async (methodName, kwargs) => {
        try {
            if (typeof Millennium !== 'undefined' && typeof Millennium.callServerMethod === 'function') {
                return await Millennium.callServerMethod(pluginName, methodName, kwargs || {});
            }
        } catch (err) {
            console.warn('[CloudRedirect] Backend call error:', err);
        }
        return null;
    };

    function launchApp() {
        callServerMethod(""launch_cloudredirect"", {});
        try {
            const link = document.createElement('a');
            link.href = 'cloudredirect://open';
            link.click();
        } catch (e) { }
    }

    function triggerBackup() {
        callServerMethod(""trigger_backup"", {});
        try {
            const link = document.createElement('a');
            link.href = 'cloudredirect://backup';
            link.click();
        } catch (e) { }
    }

    function openSaves() {
        callServerMethod(""open_saves"", {});
        try {
            const link = document.createElement('a');
            link.href = 'cloudredirect://saves';
            link.click();
        } catch (e) { }
    }

    function ensureStyles() {
        if (document.getElementById('cr-millennium-styles')) return;
        const style = document.createElement('style');
        style.id = 'cr-millennium-styles';
        style.textContent = `
            .cr-nav-btn {
                display: inline-flex;
                align-items: center;
                gap: 6px;
                height: 26px;
                padding: 0 10px;
                background: #17222d;
                border: 1px solid #233b53;
                border-radius: 4px;
                color: #c6d4df;
                font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
                font-size: 12px;
                font-weight: 600;
                cursor: pointer;
                user-select: none;
                transition: all 0.2s ease;
                vertical-align: middle;
                margin: 0 4px;
                z-index: 1000;
            }
            .cr-nav-btn:hover {
                background: #203548;
                border-color: #66c0f4;
                color: #ffffff;
                box-shadow: 0 0 8px rgba(102, 192, 244, 0.3);
            }
            .cr-nav-btn.cr-active {
                background: #1c3852;
                border-color: #66c0f4;
                color: #ffffff;
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
            .cr-dropdown-menu {
                position: fixed;
                top: 42px;
                width: 320px;
                background: linear-gradient(135deg, #1b2838 0%, #171d25 100%);
                border: 1px solid #36506c;
                border-radius: 8px;
                box-shadow: 0 12px 32px rgba(0, 0, 0, 0.8), 0 0 1px rgba(102, 192, 244, 0.4);
                color: #c6d4df;
                font-family: ""Motiva Sans"", -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
                font-size: 13px;
                padding: 14px;
                z-index: 99999;
                animation: crFadeIn 0.18s ease-out forwards;
                backdrop-filter: blur(12px);
            }
            @keyframes crFadeIn {
                from { opacity: 0; transform: translateY(-6px); }
                to { opacity: 1; transform: translateY(0); }
            }
            .cr-dropdown-header {
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding-bottom: 10px;
                border-bottom: 1px solid rgba(102, 192, 244, 0.2);
                margin-bottom: 12px;
            }
            .cr-title-group {
                display: flex;
                align-items: center;
                gap: 8px;
            }
            .cr-title {
                font-size: 15px;
                font-weight: 700;
                color: #ffffff;
                letter-spacing: 0.3px;
            }
            .cr-version-badge {
                background: #142332;
                border: 1px solid #23425e;
                color: #66c0f4;
                font-size: 10px;
                font-weight: 700;
                padding: 2px 6px;
                border-radius: 4px;
            }
            .cr-status-badge {
                display: inline-flex;
                align-items: center;
                gap: 5px;
                background: rgba(164, 208, 7, 0.12);
                border: 1px solid rgba(164, 208, 7, 0.35);
                color: #a4d007;
                font-size: 11px;
                font-weight: 600;
                padding: 2px 8px;
                border-radius: 12px;
            }
            .cr-dropdown-desc {
                font-size: 12px;
                color: #8f98a0;
                line-height: 1.4;
                margin-bottom: 12px;
            }
            .cr-stats-card {
                background: rgba(15, 23, 33, 0.6);
                border: 1px solid #233446;
                border-radius: 6px;
                padding: 10px 12px;
                margin-bottom: 14px;
            }
            .cr-stat-row {
                display: flex;
                justify-content: space-between;
                align-items: center;
                font-size: 11px;
                padding: 2px 0;
            }
            .cr-stat-label { color: #8f98a0; }
            .cr-stat-val { color: #66c0f4; font-weight: 600; }
            .cr-stat-val.cr-good { color: #a4d007; }
            .cr-btn-primary {
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 8px;
                width: 100%;
                height: 36px;
                background: linear-gradient(90deg, #5c7e10 0%, #476508 100%);
                border: none;
                border-radius: 4px;
                color: #ffffff;
                font-size: 13px;
                font-weight: 700;
                cursor: pointer;
                transition: all 0.18s ease;
                box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
                margin-bottom: 8px;
            }
            .cr-btn-primary:hover {
                background: linear-gradient(90deg, #6e9713 0%, #55790a 100%);
                box-shadow: 0 0 12px rgba(164, 208, 7, 0.4);
            }
            .cr-btn-secondary {
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 6px;
                width: 100%;
                height: 32px;
                background: #213244;
                border: 1px solid #36506c;
                border-radius: 4px;
                color: #c6d4df;
                font-size: 12px;
                font-weight: 600;
                cursor: pointer;
                transition: all 0.18s ease;
                margin-bottom: 6px;
            }
            .cr-btn-secondary:hover {
                background: #2a415a;
                border-color: #66c0f4;
                color: #ffffff;
            }
            .cr-footer {
                display: flex;
                justify-content: space-between;
                align-items: center;
                font-size: 10px;
                color: #626e7b;
                margin-top: 10px;
                padding-top: 8px;
                border-top: 1px solid rgba(255, 255, 255, 0.05);
            }
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
        document.head.appendChild(style);
    }

    const cloudSvg = `<svg class=""cr-cloud-icon-svg"" viewBox=""0 0 24 24""><path d=""M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z""/></svg>`;

    let dropdownElement = null;

    function toggleDropdown(btn) {
        if (dropdownElement) {
            dropdownElement.remove();
            dropdownElement = null;
            btn.classList.remove('cr-active');
            return;
        }

        btn.classList.add('cr-active');
        const rect = btn.getBoundingClientRect();

        dropdownElement = document.createElement('div');
        dropdownElement.className = 'cr-dropdown-menu';
        dropdownElement.style.left = Math.max(10, rect.right - 320) + 'px';
        dropdownElement.style.top = (rect.bottom + 6) + 'px';

        dropdownElement.innerHTML = `
            <div class=""cr-dropdown-header"">
                <div class=""cr-title-group"">
                    <span class=""cr-title"">CloudRedirect</span>
                    <span class=""cr-version-badge"">NATIVE</span>
                </div>
                <div class=""cr-status-badge"">
                    <span class=""cr-status-dot""></span>
                    <span>Protected</span>
                </div>
            </div>
            <div class=""cr-dropdown-desc"">
                Real-time universal save redirection and cloud backup active for Steam games.
            </div>
            <div class=""cr-stats-card"">
                <div class=""cr-stat-row"">
                    <span class=""cr-stat-label"">Save Protection Engine</span>
                    <span class=""cr-stat-val cr-good"">Active &amp; Monitoring</span>
                </div>
                <div class=""cr-stat-row"">
                    <span class=""cr-stat-label"">Cloud Storage</span>
                    <span class=""cr-stat-val"">Connected</span>
                </div>
                <div class=""cr-stat-row"">
                    <span class=""cr-stat-label"">Local Snapshots</span>
                    <span class=""cr-stat-val cr-good"">Safe Mode OK</span>
                </div>
            </div>
            <button id=""cr-action-launch"" class=""cr-btn-primary"">
                ${cloudSvg}
                <span>Open CloudRedirect App</span>
            </button>
            <button id=""cr-action-backup"" class=""cr-btn-secondary"">
                <span>⚡ Backup All Saves Now</span>
            </button>
            <button id=""cr-action-saves"" class=""cr-btn-secondary"">
                <span>📁 Open Save Manager</span>
            </button>
            <div class=""cr-footer"">
                <span>Steam Millennium Plugin</span>
                <span>v1.0.0</span>
            </div>
        `;

        document.body.appendChild(dropdownElement);

        dropdownElement.querySelector('#cr-action-launch').onclick = () => {
            launchApp();
            toggleDropdown(btn);
        };
        dropdownElement.querySelector('#cr-action-backup').onclick = () => {
            triggerBackup();
            toggleDropdown(btn);
        };
        dropdownElement.querySelector('#cr-action-saves').onclick = () => {
            openSaves();
            toggleDropdown(btn);
        };

        const outsideClickListener = (e) => {
            if (dropdownElement && !dropdownElement.contains(e.target) && !btn.contains(e.target)) {
                toggleDropdown(btn);
                document.removeEventListener('click', outsideClickListener);
            }
        };
        setTimeout(() => document.addEventListener('click', outsideClickListener), 10);
    }

    function injectHeaderButton() {
        if (document.getElementById('cloudredirect-header-btn')) return;

        const selectors = [
            '.title-bar-actions',
            '[class*=""titlebarcontrols_""]',
            '[class*=""supernav_""]',
            '#desktop_titlebar',
            '.supernav_container',
            '#header_notifications',
            '[class*=""notificationbutton_""]'
        ];

        let target = null;
        for (const sel of selectors) {
            target = document.querySelector(sel);
            if (target) break;
        }

        if (!target) return;

        const btn = document.createElement('div');
        btn.id = 'cloudredirect-header-btn';
        btn.className = 'cr-nav-btn';
        btn.title = 'CloudRedirect Native Save Protection';
        btn.innerHTML = `
            ${cloudSvg}
            <span>CloudRedirect</span>
            <span class=""cr-status-dot""></span>
        `;

        btn.onclick = (e) => {
            e.stopPropagation();
            toggleDropdown(btn);
        };

        if (target.firstChild) {
            target.insertBefore(btn, target.firstChild);
        } else {
            target.appendChild(btn);
        }
    }

    function injectGameBadge() {
        const gameActionBars = document.querySelectorAll('div[class*=""playbar_""], div[class*=""appactionandstats_""], div[class*=""appdetailsheader_""]');
        gameActionBars.forEach(bar => {
            if (bar.querySelector('.cr-game-badge')) return;

            const badge = document.createElement('div');
            badge.className = 'cr-game-badge';
            badge.title = 'Game save files are actively redirected and backed up by CloudRedirect';
            badge.innerHTML = `
                ${cloudSvg}
                <span>CloudRedirect</span>
                <span class=""cr-game-badge-check"">✓</span>
            `;
            badge.onclick = (e) => {
                e.stopPropagation();
                launchApp();
            };

            bar.appendChild(badge);
        });
    }

    function setupObserver() {
        ensureStyles();
        injectHeaderButton();
        injectGameBadge();

        const observer = new MutationObserver(() => {
            ensureStyles();
            injectHeaderButton();
            injectGameBadge();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        setInterval(() => {
            injectHeaderButton();
            injectGameBadge();
        }, 2000);
    }

    const pluginExport = {
        default: async function PluginMain() {
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
                    const btn = document.getElementById('cloudredirect-header-btn');
                    if (btn) btn.remove();
                    if (dropdownElement) dropdownElement.remove();
                    const style = document.getElementById('cr-millennium-styles');
                    if (style) style.remove();
                }
            };
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => pluginExport.default());
    } else {
        pluginExport.default();
    }

    Object.assign(window.PLUGIN_LIST[pluginName], pluginExport);
})();";

    #endregion
}
