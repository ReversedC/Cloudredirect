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

    public static bool EnableGamePerformanceBooster
    {
        get => ReadBool("enable_game_performance_booster", true);
        set => WriteBool("enable_game_performance_booster", value);
    }

    public static bool EnableMobileDashboard
    {
        get => ReadBool("enable_mobile_dashboard", true);
        set => WriteBool("enable_mobile_dashboard", value);
    }

    public static int MobileDashboardPort
    {
        get => ReadInt("mobile_dashboard_port", 38400);
        set => WriteInt("mobile_dashboard_port", value);
    }

    public static bool EnableSpacewarBroadcaster
    {
        get => ReadBool("enable_spacewar_broadcaster", true);
        set => WriteBool("enable_spacewar_broadcaster", value);
    }

    public static bool EnableLanP2PSync
    {
        get => ReadBool("enable_lan_p2p_sync", true);
        set => WriteBool("enable_lan_p2p_sync", value);
    }

    public static bool EnableShellContextMenu
    {
        get => ReadBool("enable_shell_context_menu", true);
        set => WriteBool("enable_shell_context_menu", value);
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
        get
        {
            var def = AppUpdater.GetCurrentVersionString().EndsWith("B", StringComparison.OrdinalIgnoreCase) ? "beta" : "main";
            return ReadString("update_branch", def);
        }
        set => WriteString("update_branch", value);
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

    private static int ReadInt(string keyName, int defaultValue)
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return defaultValue;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(keyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                    return val;
            }
        }
        catch { }
        return defaultValue;
    }

    private static void WriteInt(string keyName, int value)
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
