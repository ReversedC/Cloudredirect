using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CloudRedirect.Resources;

namespace CloudRedirect.Services;

/// <summary>
/// Service to patch Steam WebUI styles, replacing the Steam Cloud status icon with CloudRedirect's icon
/// specifically for Lua / redirected games only. Original owned games retain Steam's stock cloud icon.
/// Automatically discovers dynamic CSS module class hashes from Steam's WebUI JavaScript chunks
/// and continuously monitors for Steam updates and Lua game changes.
/// </summary>
public static class SteamWebUiPatcher
{
    public const string MarkerStart = "/* === BEGIN CLOUDREDIRECT STEAM CLOUD ICON (LUA GAMES ONLY) === */";
    public const string MarkerEnd = "/* === END CLOUDREDIRECT STEAM CLOUD ICON (LUA GAMES ONLY) === */";

    /// <summary>
    /// Crisp 96x96 PNG of CloudRedirect icon encoded as base64.
    /// </summary>
    public const string EmbeddedIconBase64 = "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";

    /// <summary>
    /// Custom cloud icon feature is disabled to preserve Steam's stock original cloud icon.
    /// </summary>
    public static bool IsFeatureEnabled()
    {
        return false;
    }

    /// <summary>
    /// Ensures that any legacy or previously applied CSS patch in Steam's library.css
    /// or Millennium's quick.css is completely removed, preserving the stock icon.
    /// </summary>
    public static void EnsureStockIconRestored(string? steamPath = null)
    {
        try
        {
            RemovePatch(steamPath);
        }
        catch { }
    }

    /// <summary>
    /// Checks if the CSS patch is currently injected into Steam's library.css or Millennium's quick.css.
    /// </summary>
    public static bool IsPatchApplied(string? steamPath = null)
    {
        try
        {
            steamPath ??= SteamDetector.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                return false;

            var quickCss = GetMillenniumQuickCssPath(steamPath);
            if (quickCss != null && File.Exists(quickCss))
            {
                var qContent = File.ReadAllText(quickCss);
                if (qContent.Contains(MarkerStart, StringComparison.Ordinal))
                    return true;
            }

            var cssPath = Path.Combine(steamPath, "steamui", "css", "library.css");
            if (!File.Exists(cssPath)) return false;

            var content = File.ReadAllText(cssPath);
            return content.Contains(MarkerStart, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the path to Millennium's quick.css if Millennium is present.
    /// </summary>
    public static string? GetMillenniumQuickCssPath(string steamPath)
    {
        var millDir = Path.Combine(steamPath, "millennium");
        return Directory.Exists(millDir) ? Path.Combine(millDir, "config", "quick.css") : null;
    }

    /// <summary>
    /// Returns true if the Millennium modding framework is installed in the Steam folder.
    /// </summary>
    public static bool IsMillenniumActive(string steamPath)
    {
        var millDir = Path.Combine(steamPath, "millennium");
        return Directory.Exists(millDir);
    }

    /// <summary>
    /// Discovers all AppIDs that are unlocked via Lua scripts or managed by CloudRedirect.
    /// Original games not in this set will continue to use Steam's stock cloud icon.
    /// </summary>
    public static HashSet<uint> GetLuaAppIds(string steamPath)
    {
        var appIds = new HashSet<uint>();

        // 1. Scan config/stplug-in/*.lua
        try
        {
            var pluginDir = Path.Combine(steamPath, "config", "stplug-in");
            if (Directory.Exists(pluginDir))
            {
                var addAppIdRegex = new Regex(@"addappid\s*\(\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                foreach (var file in Directory.EnumerateFiles(pluginDir, "*.lua"))
                {
                    var stem = Path.GetFileNameWithoutExtension(file);
                    if (uint.TryParse(stem, out var fileAppId) && fileAppId > 0)
                    {
                        appIds.Add(fileAppId);
                    }

                    try
                    {
                        foreach (var line in File.ReadLines(file))
                        {
                            var trimmed = line.TrimStart();
                            if (trimmed.StartsWith("--", StringComparison.Ordinal))
                                continue;

                            var match = addAppIdRegex.Match(trimmed);
                            if (match.Success && uint.TryParse(match.Groups[1].Value, out var innerAppId) && innerAppId > 0)
                            {
                                appIds.Add(innerAppId);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 2. Scan cloud_redirect/storage for any redirected game folders
        try
        {
            var storageDir = Path.Combine(steamPath, "cloud_redirect", "storage");
            if (Directory.Exists(storageDir))
            {
                foreach (var accountDir in Directory.EnumerateDirectories(storageDir))
                {
                    foreach (var appDir in Directory.EnumerateDirectories(accountDir))
                    {
                        var folderName = Path.GetFileName(appDir);
                        if (uint.TryParse(folderName, out var appId) && appId > 0)
                        {
                            appIds.Add(appId);
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Scan remote_unlock.json fallback
        try
        {
            var remoteUnlocked = SteamDetector.GetRemoteUnlockedAppIds(steamPath);
            foreach (var id in remoteUnlocked)
            {
                appIds.Add(id);
            }
        }
        catch { }

        // 4. Scan Zero-Lua intercepted apps
        try
        {
            var intercepted = SteamDetector.GetInterceptAppIds(steamPath);
            foreach (var id in intercepted)
            {
                appIds.Add(id);
            }
        }
        catch { }

        // 5. Scan Universal Cloud Saves monitored Steam games
        try
        {
            var profiles = UniversalSaveWatcherService.GetProfiles();
            foreach (var p in profiles)
            {
                if (p.SteamAppId > 0)
                {
                    appIds.Add(p.SteamAppId);
                }
            }
        }
        catch { }

        return appIds;
    }

    /// <summary>
    /// Scans Steam's steamui directory JavaScript files for dynamic React/CSS-module class mappings.
    /// Returns discovered class names (e.g. CloudIconSVG, PlayBarCloudStatusContainer, PlayBar, Container, GameListEntryContainer).
    /// </summary>
    public static Dictionary<string, string> ScanDynamicClasses(string steamPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var steamuiDir = Path.Combine(steamPath, "steamui");
        if (!Directory.Exists(steamuiDir)) return result;

        var targetKeys = new[]
        {
            "CloudIconSVG", "PlayBarCloudStatusContainer", "CloudStatusIcon", "CloudStatusRow",
            "PlayBar", "Container", "InnerContainer", "AppDetailsOverviewPanel", "Header",
            "CapsuleContainer", "GridItem", "GameListEntryContainer", "AppBody"
        };
        var regexes = new Dictionary<string, Regex>();
        foreach (var key in targetKeys)
        {
            regexes[key] = new Regex(@"\b" + Regex.Escape(key) + @"\s*:\s*[""']([^""']+)[""']", RegexOptions.Compiled);
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(steamuiDir, "*.js"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("libraries~", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = File.ReadAllText(file);
                    foreach (var pair in regexes)
                    {
                        if (!result.ContainsKey(pair.Key))
                        {
                            var match = pair.Value.Match(content);
                            if (match.Success)
                            {
                                result[pair.Key] = match.Groups[1].Value;
                            }
                        }
                    }

                    if (result.Count == targetKeys.Length)
                        break; // Found everything!
                }
                catch { }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Generates custom CSS rules strictly scoped to Lua / redirected games using modern :has() selectors.
    /// If an original owned game is displayed, the selector fails to match and Steam shows its stock cloud icon.
    /// </summary>
    public static string GenerateCss(HashSet<uint> luaAppIds, Dictionary<string, string> dynamicClasses, string? customIconPath = null)
    {
        if (luaAppIds == null || luaAppIds.Count == 0)
            return string.Empty;

        string iconB64 = EmbeddedIconBase64;
        if (!string.IsNullOrEmpty(customIconPath) && File.Exists(customIconPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(customIconPath);
                iconB64 = Convert.ToBase64String(bytes);
            }
            catch { }
        }

        // Build selectors matching any asset/link of the Lua / managed games
        var appSelectors = new List<string>();
        foreach (var appId in luaAppIds.OrderBy(id => id))
        {
            appSelectors.Add($"[data-appid=\"{appId}\"]");
            appSelectors.Add($"a[href*=\"/app/{appId}\"]");
            appSelectors.Add($"[data-panel*=\"{appId}\"]");
        }

        var hasClause = string.Join(", ", appSelectors);

        // Containers: InnerContainer, AppDetailsOverviewPanel, Game Details Container, PlayBar, or App Properties Dialog
        var containers = new List<string>
        {
            "div[class*=\"InnerContainer\"]",
            "div[class*=\"AppDetailsOverviewPanel\"]",
            "div[class*=\"Container\"]",
            "div[class*=\"gamepagedetails\"]",
            "div[class*=\"GameDetails\"]",
            "div[class*=\"DesktopUI\"]",
            "div[class*=\"FullAnchor\"]",
            "dialog",
            "body"
        };
        if (dynamicClasses.TryGetValue("InnerContainer", out var ic) && !string.IsNullOrWhiteSpace(ic))
            containers.Add($"div.{ic}");
        if (dynamicClasses.TryGetValue("AppDetailsOverviewPanel", out var adop) && !string.IsNullOrWhiteSpace(adop))
            containers.Add($"div.{adop}");
        if (dynamicClasses.TryGetValue("PlayBar", out var pb) && !string.IsNullOrWhiteSpace(pb))
            containers.Add($"div.{pb}");
        if (dynamicClasses.TryGetValue("Container", out var ct) && !string.IsNullOrWhiteSpace(ct))
            containers.Add($"div.{ct}");

        var containerStr = string.Join(", ", containers.Distinct());

        // Targets: Cloud status SVG icon elements
        var targets = new List<string>
        {
            "div[class*=\"PlayBarCloudStatusContainer\"] svg",
            "span[class*=\"CloudStatusIcon\"] svg",
            "svg[class*=\"CloudIconSVG\"]",
            "div._2cRYms-zZc4misk9tj3bt8 svg",
            "span._1PrjvpmQ3CUjn45PN1Ed6V svg",
            "svg.MbTRimZpGCATmn39ae8RT"
        };
        if (dynamicClasses.TryGetValue("CloudIconSVG", out var cloudSvg) && !string.IsNullOrWhiteSpace(cloudSvg))
            targets.Add($"svg.{cloudSvg}");
        if (dynamicClasses.TryGetValue("PlayBarCloudStatusContainer", out var pbContainer) && !string.IsNullOrWhiteSpace(pbContainer))
            targets.Add($"div.{pbContainer} svg");
        if (dynamicClasses.TryGetValue("CloudStatusIcon", out var csIcon) && !string.IsNullOrWhiteSpace(csIcon))
            targets.Add($"span.{csIcon} svg");

        var targetStr = string.Join(", ", targets.Distinct());

        // Capsules
        var capsuleContainers = new List<string> { "div[class*=\"CapsuleContainer\"]", "div[class*=\"Capsule\"]", "div[class*=\"GridItem\"]", "div[class*=\"AppBody\"]" };
        if (dynamicClasses.TryGetValue("CapsuleContainer", out var cc) && !string.IsNullOrWhiteSpace(cc))
            capsuleContainers.Add($"div.{cc}");
        if (dynamicClasses.TryGetValue("GridItem", out var gi) && !string.IsNullOrWhiteSpace(gi))
            capsuleContainers.Add($"div.{gi}");
        var capsuleStr = string.Join(", ", capsuleContainers.Distinct());

        // Sidebar game list entries
        var sidebarContainers = new List<string> { "div[class*=\"GameListEntryContainer\"]", "div[class*=\"_1vO6BoiVslZgs1kqDGdUs8\"]", "div._1vO6BoiVslZgs1kqDGdUs8" };
        if (dynamicClasses.TryGetValue("GameListEntryContainer", out var glec) && !string.IsNullOrWhiteSpace(glec))
            sidebarContainers.Add($"div.{glec}");
        var sidebarStr = string.Join(", ", sidebarContainers.Distinct());

        return $@"{MarkerStart}
/* 1. PlayBar & Dialogs: Custom Cloud Status Icon */
:is({containerStr}):has(
    :is({hasClause})
) :is({targetStr}) {{
    background-image: url(""data:image/png;base64,{iconB64}"") !important;
    background-repeat: no-repeat !important;
    background-position: center !important;
    background-size: contain !important;
    display: inline-block !important;
    width: 22px !important;
    height: 22px !important;
    min-width: 18px !important;
    min-height: 18px !important;
    visibility: visible !important;
    opacity: 1 !important;
    filter: drop-shadow(0 0 4px rgba(0, 210, 255, 0.55)) !important;
}}

:is({containerStr}):has(
    :is({hasClause})
) :is({targetStr}) > * {{
    display: none !important;
    visibility: hidden !important;
    opacity: 0 !important;
}}

/* 2. Steam Library Grid: Visual Glow & Cloud Badge for Redirected/SUO Games */
:is({capsuleStr}):has(
    :is({hasClause})
) {{
    position: relative !important;
}}

:is({capsuleStr}):has(
    :is({hasClause})
):hover {{
    box-shadow: 0 0 16px rgba(0, 210, 255, 0.45) !important;
    transition: box-shadow 0.2s ease-in-out !important;
}}

:is({capsuleStr}):has(
    :is({hasClause})
)::after {{
    content: ""CLOUD"" !important;
    position: absolute !important;
    top: 6px !important;
    right: 6px !important;
    background: rgba(16, 26, 38, 0.92) !important;
    color: #00d2ff !important;
    font-size: 9px !important;
    font-weight: 700 !important;
    letter-spacing: 0.5px !important;
    padding: 2px 6px !important;
    border-radius: 3px !important;
    border: 1px solid rgba(0, 210, 255, 0.45) !important;
    pointer-events: none !important;
    backdrop-filter: blur(4px) !important;
    z-index: 10 !important;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.6) !important;
    text-shadow: 0 0 4px rgba(0, 210, 255, 0.6) !important;
}}

/* 3. Steam Library Sidebar: Subtle Highlight & Left Accent for Redirected/SUO Games */
:is({sidebarStr}):has(
    :is({hasClause})
) {{
    border-left: 2px solid #00d2ff !important;
}}

:is({sidebarStr}):has(
    :is({hasClause})
):hover {{
    background: rgba(0, 210, 255, 0.08) !important;
}}
{MarkerEnd}
";
    }

    private static readonly object _patchLock = new();

    /// <summary>
    /// Injects the custom icon, capsule badges, and sidebar rules into Millennium's quick.css.
    /// Safely leaves Steam's native library.css untouched (or cleans it) so Steam's manifest file checks never fail.
    /// Also saves custom_cloud_icon: true in config.json. Thread-safe and loop-prevented.
    /// </summary>
    public static (bool Success, string Message) ApplyPatch(string? steamPath = null)
    {
        // Custom icon replacement is retired; restore stock original Steam Cloud icon.
        return RemovePatch(steamPath);
    }



    private static string StripMarkerSection(string content, string startMarker, string endMarker)
    {
        if (content.Contains(startMarker, StringComparison.Ordinal))
        {
            var startIdx = content.IndexOf(startMarker, StringComparison.Ordinal);
            var endIdx = content.IndexOf(endMarker, StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                endIdx += endMarker.Length;
                return content.Substring(0, startIdx).TrimEnd() + "\n" + content.Substring(endIdx).TrimStart();
            }
        }
        return content;
    }

    /// <summary>
    /// Removes the custom icon stylesheet rules from Steam's library.css and Millennium's quick.css.
    /// Also saves custom_cloud_icon: false in config.json.
    /// </summary>
    public static (bool Success, string Message) RemovePatch(string? steamPath = null)
    {
        lock (_patchLock)
        {
            try
            {
                steamPath ??= SteamDetector.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                    return (false, "Steam installation path could not be located.");

                // 1. Remove from library.css
                var cssPath = Path.Combine(steamPath, "steamui", "css", "library.css");
                if (File.Exists(cssPath))
                {
                    var existingContent = File.ReadAllText(cssPath);
                    const string legacyMarkerStart = "/* === BEGIN CLOUDREDIRECT STEAM CLOUD ICON === */";
                    const string legacyMarkerEnd = "/* === END CLOUDREDIRECT STEAM CLOUD ICON === */";
                    existingContent = StripMarkerSection(existingContent, legacyMarkerStart, legacyMarkerEnd);
                    existingContent = StripMarkerSection(existingContent, MarkerStart, MarkerEnd);
                    if (File.ReadAllText(cssPath) != existingContent)
                    {
                        FileUtils.AtomicWriteAllText(cssPath, existingContent);
                    }
                }

                // 2. Remove from Millennium quick.css if present
                var quickCss = GetMillenniumQuickCssPath(steamPath);
                if (quickCss != null && File.Exists(quickCss))
                {
                    try
                    {
                        var existingQuick = File.ReadAllText(quickCss);
                        const string legacyMarkerStart = "/* === BEGIN CLOUDREDIRECT STEAM CLOUD ICON === */";
                        const string legacyMarkerEnd = "/* === END CLOUDREDIRECT STEAM CLOUD ICON === */";
                        existingQuick = StripMarkerSection(existingQuick, legacyMarkerStart, legacyMarkerEnd);
                        existingQuick = StripMarkerSection(existingQuick, MarkerStart, MarkerEnd);
                        if (File.ReadAllText(quickCss) != existingQuick)
                        {
                            FileUtils.AtomicWriteAllText(quickCss, existingQuick);
                        }
                    }
                    catch { }
                }

                // Persist setting in config.json
                var configPath = SteamDetector.GetConfigFilePath();
                ConfigHelper.SaveConfig(configPath, new[] { "custom_cloud_icon" }, writer =>
                {
                    writer.WriteBoolean("custom_cloud_icon", false);
                });

                return (true, "Successfully restored default Steam GUI styles.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to restore default icon: {ex.Message}");
            }
        }
    }

    /// <summary>
    public static void AutoRefreshIfEnabled(string? steamPath = null)
    {
        EnsureStockIconRestored(steamPath);
    }

    /// <summary>
    /// Legacy watcher hook; ensures stock icon is restored.
    /// </summary>
    public static void StartWatcher(string? steamPath = null)
    {
        EnsureStockIconRestored(steamPath);
    }
}
