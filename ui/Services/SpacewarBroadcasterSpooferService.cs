using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CloudRedirect.Services;

/// <summary>
/// Spacewar Broadcast Spoofer Service:
/// Automatically intercepts games running under AppID 480 (Spacewar) or generic wrappers,
/// identifies the actual game via process signatures and window titles, and broadcasts the true game
/// identity across CloudRedirect, system tray, Millennium Steam UI, and Discord Rich Presence.
/// </summary>
public static class SpacewarBroadcasterSpooferService
{
    private static bool _isInitialized;
    private static string? _lastSpoofedTitle;

    public static string? CurrentSpoofedTitle => _lastSpoofedTitle;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        ActiveGameTrackerService.OnActiveGameChanged += HandleActiveGameChanged;
    }

    private static void HandleActiveGameChanged(ActiveGameInfo? game)
    {
        if (!AppSettings.EnableSpacewarBroadcaster)
            return;

        if (game != null && game.IsRunning)
        {
            if (game.AppId == 480 || IsGenericBroadcastApp(game.AppId))
            {
                var realName = ResolveRealGameTitle(game);
                _lastSpoofedTitle = realName;
                BroadcastRealTitle(realName, game);
            }
            else
            {
                _lastSpoofedTitle = null;
            }
        }
        else
        {
            _lastSpoofedTitle = null;
            ClearBroadcast();
        }
    }

    public static bool IsGenericBroadcastApp(uint appId)
    {
        return appId is 480 or 105600; // Spacewar or Terraria Dedicated Server generic hosts
    }

    public static string ResolveRealGameTitle(ActiveGameInfo game)
    {
        // 1. Process name mapping
        var proc = game.ProcessName;
        if (!string.IsNullOrEmpty(proc))
        {
            var clean = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proc[..^4] : proc;
            var mapped = GameSaveAutoDetector.GetPresetGameName(clean);
            if (!string.IsNullOrEmpty(mapped))
                return mapped;
        }

        // 2. Window title inspection
        try
        {
            if (!string.IsNullOrEmpty(proc))
            {
                var procs = Process.GetProcessesByName(proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proc[..^4] : proc);
                foreach (var p in procs)
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                        !p.MainWindowTitle.Equals("Spacewar", StringComparison.OrdinalIgnoreCase) &&
                        !p.MainWindowTitle.Contains("Console", StringComparison.OrdinalIgnoreCase))
                    {
                        return p.MainWindowTitle.Trim();
                    }
                }
            }
        }
        catch { }

        // 3. Fallback to folder name
        if (!string.IsNullOrEmpty(game.SaveDirectory))
        {
            var folder = Path.GetFileName(game.SaveDirectory.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(folder) && !folder.Equals("480"))
                return folder;
        }

        return !string.IsNullOrEmpty(game.GameName) && !game.GameName.Equals("Spacewar", StringComparison.OrdinalIgnoreCase)
            ? game.GameName
            : "Generic Multiplayer Game";
    }

    private static void BroadcastRealTitle(string realTitle, ActiveGameInfo game)
    {
        try
        {
            // 1. Write live presence file for Steam Millennium plugin / web overlay
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);

            var presenceFile = Path.Combine(appData, "active_presence.json");
            var meta = new
            {
                isSpoofed = true,
                originalAppId = game.AppId,
                realGameTitle = realTitle,
                processName = game.ProcessName,
                timestamp = DateTime.Now.ToString("o")
            };
            File.WriteAllText(presenceFile, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            // 2. Notify system tray
            TrayIconService.Instance.UpdateTrayTitle($"CloudRedirect - Playing {realTitle}");
        }
        catch { }
    }

    private static void ClearBroadcast()
    {
        try
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
            var presenceFile = Path.Combine(appData, "active_presence.json");
            if (File.Exists(presenceFile))
                File.Delete(presenceFile);

            TrayIconService.Instance.UpdateTrayTitle("CloudRedirect");
        }
        catch { }
    }
}
