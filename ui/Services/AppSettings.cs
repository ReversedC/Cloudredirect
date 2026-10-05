using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Manages application-level preferences such as Windows startup,
/// minimize-to-tray behavior, and sync notifications.
/// </summary>
public static class AppSettings
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CloudRedirect";

    public static string GetSettingsPath()
    {
        return Path.Combine(SteamDetector.GetConfigDir(), "settings.json");
    }

    public static bool StartWithWindows
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;

                if (value)
                {
                    var exePath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\" -minimized");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update Run registry key: {ex}");
            }
        }
    }

    public static bool MinimizeToTrayOnClose
    {
        get => ReadBool("minimize_to_tray_on_close", true);
        set => WriteBool("minimize_to_tray_on_close", value);
    }

    public static bool ShowSyncNotifications
    {
        get => ReadBool("show_sync_notifications", true);
        set => WriteBool("show_sync_notifications", value);
    }

    public static bool AutoFitZoom
    {
        get => ReadBool("auto_fit_zoom", true);
        set => WriteBool("auto_fit_zoom", value);
    }

    public static bool AutoProtectNonCloudGames
    {
        get => ReadBool("auto_protect_non_cloud_games", true);
        set => WriteBool("auto_protect_non_cloud_games", value);
    }

    public static bool EnableMillenniumPlugin
    {
        get => ReadBool("enable_millennium_plugin", true);
        set => WriteBool("enable_millennium_plugin", value);
    }

    public static bool AutoRestartSteamOnLaunch
    {
        get => ReadBool("auto_restart_steam_on_launch", true);
        set => WriteBool("auto_restart_steam_on_launch", value);
    }

    public static bool AutoSyncOnGameExit
    {
        get => ReadBool("auto_sync_on_game_exit", true);
        set => WriteBool("auto_sync_on_game_exit", value);
    }

    public static bool AutoMidGameCheckpoint
    {
        get => ReadBool("auto_mid_game_checkpoint", true);
        set => WriteBool("auto_mid_game_checkpoint", value);
    }

    public static bool AutoConflictHealing
    {
        get => ReadBool("auto_conflict_healing", true);
        set => WriteBool("auto_conflict_healing", value);
    }

    public static bool AutoStorageCompression
    {
        get => ReadBool("auto_storage_compression", true);
        set => WriteBool("auto_storage_compression", value);
    }

    public static bool AutoCommunityDatabase
    {
        get => ReadBool("auto_community_database", true);
        set => WriteBool("auto_community_database", value);
    }

    public const string DefaultCommunityDatabaseUrl = "https://gist.githubusercontent.com/mirzaarsyad74-cmyk/01b85e449667ae2bbba88d4fdd42df5a/raw/community_saves.json";
    public const string LudusaviManifestUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";

    public static string CommunityDatabaseUrl
    {
        get => ReadString("community_database_url", LudusaviManifestUrl);
        set => WriteString("community_database_url", value);
    }

    public static string CommunityDatabaseLastSync
    {
        get => ReadString("community_database_last_sync", "");
        set => WriteString("community_database_last_sync", value);
    }

    public static bool GameBoostEnabled
    {
        get => ReadBool("game_boost_enabled", false);
        set => WriteBool("game_boost_enabled", value);
    }

    public static bool GameBoostHighPriority
    {
        get => ReadBool("game_boost_high_priority", true);
        set => WriteBool("game_boost_high_priority", value);
    }

    public static bool GameBoostPowerPlan
    {
        get => ReadBool("game_boost_power_plan", true);
        set => WriteBool("game_boost_power_plan", value);
    }

    public static bool GameBoostRamTrim
    {
        get => ReadBool("game_boost_ram_trim", true);
        set => WriteBool("game_boost_ram_trim", value);
    }

    public static bool GameBoostThrottleBackground
    {
        get => ReadBool("game_boost_throttle_bg", false);
        set => WriteBool("game_boost_throttle_bg", value);
    }

    public static bool GameBoostShowToast
    {
        get => ReadBool("game_boost_show_toast", true);
        set => WriteBool("game_boost_show_toast", value);
    }

    public static bool GameBoostHighPerformanceGpu
    {
        get => ReadBool("game_boost_high_perf_gpu", true);
        set => WriteBool("game_boost_high_perf_gpu", value);
    }

    public static bool GameBoostAutoFreezeBackground
    {
        get => ReadBool("game_boost_auto_freeze_bg", true);
        set => WriteBool("game_boost_auto_freeze_bg", value);
    }

    public static bool GameBoostPeriodicRamPurge
    {
        get => ReadBool("game_boost_periodic_ram_purge", true);
        set => WriteBool("game_boost_periodic_ram_purge", value);
    }

    public static bool GameBoostNetworkBoost
    {
        get => ReadBool("game_boost_network_boost", true);
        set => WriteBool("game_boost_network_boost", value);
    }

    public static bool GameBoostBluetoothBoost
    {
        get => ReadBool("game_boost_bluetooth_boost", true);
        set => WriteBool("game_boost_bluetooth_boost", value);
    }

    public static bool GameSpaceEnabled
    {
        get => ReadBool("game_space_enabled", true);
        set => WriteBool("game_space_enabled", value);
    }

    public static string GameSpaceHotkey
    {
        get => ReadString("game_space_hotkey", "Ctrl+Space");
        set => WriteString("game_space_hotkey", value);
    }

    public static bool StickyNoteHotkeyEnabled
    {
        get => ReadBool("sticky_note_hotkey_enabled", true);
        set => WriteBool("sticky_note_hotkey_enabled", value);
    }

    public static string StickyNoteHotkey
    {
        get => ReadString("sticky_note_hotkey", "Alt+N");
        set => WriteString("sticky_note_hotkey", value);
    }

    public static double ZoomScale
    {
        get => ReadDouble("zoom_scale", 1.0);
        set => WriteDouble("zoom_scale", value);
    }

    public static bool GlobalHotkeyEnabled
    {
        get => ReadBool("global_hotkey_enabled", true);
        set => WriteBool("global_hotkey_enabled", value);
    }

    public static string GlobalHotkey
    {
        get => ReadString("global_hotkey", "Ctrl+Shift+C");
        set => WriteString("global_hotkey", value);
    }

    public static string UpdateBranch
    {
        get => "main";
        set => WriteString("update_branch", "main");
    }

    private static bool ReadBool(string keyName, bool defaultValue)
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return defaultValue;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(keyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                    return prop.GetBoolean();
            }
        }
        catch { }
        return defaultValue;
    }

    private static void WriteBool(string keyName, bool value)
    {
        try
        {
            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JsonElement existing = default;
            if (File.Exists(path))
            {
                try
                {
                    var oldJson = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(oldJson);
                    existing = doc.RootElement.Clone();
                }
                catch { }
            }

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteBoolean(keyName, value);

                if (existing.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in existing.EnumerateObject())
                    {
                        if (prop.NameEquals(keyName)) continue;
                        prop.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            File.WriteAllBytes(path, ms.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write setting {keyName}: {ex}");
        }
    }

    private static double ReadDouble(string keyName, double defaultValue)
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return defaultValue;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(keyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDouble();
            }
        }
        catch { }
        return defaultValue;
    }

    public static void WriteDouble(string keyName, double value)
    {
        try
        {
            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JsonElement existing = default;
            if (File.Exists(path))
            {
                try
                {
                    var oldJson = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(oldJson);
                    existing = doc.RootElement.Clone();
                }
                catch { }
            }

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber(keyName, value);

                if (existing.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in existing.EnumerateObject())
                    {
                        if (prop.NameEquals(keyName)) continue;
                        prop.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            File.WriteAllBytes(path, ms.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write setting {keyName}: {ex}");
        }
    }

    private static string ReadString(string keyName, string defaultValue)
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return defaultValue;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(keyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    var val = prop.GetString();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
        }
        catch { }
        return defaultValue;
    }

    public static void WriteString(string keyName, string value)
    {
        try
        {
            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JsonElement existing = default;
            if (File.Exists(path))
            {
                try
                {
                    var oldJson = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(oldJson);
                    existing = doc.RootElement.Clone();
                }
                catch { }
            }

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString(keyName, value);

                if (existing.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in existing.EnumerateObject())
                    {
                        if (prop.NameEquals(keyName)) continue;
                        prop.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            File.WriteAllBytes(path, ms.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write setting {keyName}: {ex}");
        }
    }
}
