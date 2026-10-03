using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CloudRedirect.Windows;

namespace CloudRedirect.Services;

/// <summary>
/// Service managing the Android-style Game Space overlay for CloudRedirect.
/// Provides an in-game slide-out drawer with live performance telemetry,
/// on-demand RAM purging, named save checkpoints, and scratchpad notes.
/// 
/// SAFETY RULES:
/// Strictly blocked on:
/// 1. Legitimately owned Steam games (IsGenuineOwned).
/// 2. Free-to-play games (IsFreeGame).
/// 3. Games with Anti-Cheat or active Anti-Cheat processes (HasAntiCheat / EasyAntiCheat / BattlEye / Vanguard).
/// </summary>
public sealed class GameSpaceService
{
    private static GameSpaceService? _instance;
    public static GameSpaceService Instance => _instance ??= new GameSpaceService();

    private GameSpaceOverlayWindow? _overlayWindow;

    #region Win32 Memory Status

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public record MemoryStats(double UsedGb, double TotalGb, uint LoadPercent);

    public static MemoryStats GetMemoryStats()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = Math.Round((double)mem.ullTotalPhys / (1024 * 1024 * 1024), 1);
                double availGb = Math.Round((double)mem.ullAvailPhys / (1024 * 1024 * 1024), 1);
                double usedGb = Math.Max(0, Math.Round(totalGb - availGb, 1));
                return new MemoryStats(usedGb, totalGb, mem.dwMemoryLoad);
            }
        }
        catch { }

        return new MemoryStats(0, 0, 0);
    }

    #endregion

    /// <summary>
    /// Evaluates whether Game Space is allowed to open.
    /// Strictly blocks owned games, free games, and anti-cheat games.
    /// </summary>
    public bool CanOpenGameSpace(out string? blockReason)
    {
        blockReason = null;

        if (!AppSettings.GameSpaceEnabled)
        {
            blockReason = "Game Space is disabled in Settings.";
            return false;
        }

        var game = ActiveGameTrackerService.CurrentGame;

        if (game != null)
        {
            // 1. Strictly block legitimately owned Steam games
            if (game.IsGenuineOwned)
            {
                blockReason = $"Game Space is disabled for owned game \"{game.Name}\" to protect your Steam account.";
                return false;
            }

            // 2. Strictly block free-to-play games
            if (game.IsFreeGame)
            {
                blockReason = $"Game Space is disabled for free game \"{game.Name}\".";
                return false;
            }

            // 3. Strictly block Anti-Cheat protected games
            if (game.HasAntiCheat)
            {
                blockReason = $"Game Space is disabled for \"{game.Name}\" because it is protected by Anti-Cheat.";
                return false;
            }

            // 4. Check process & installation directory for anti-cheat
            string? installDir = null;
            if (game.AppId > 0)
            {
                var steamPath = SteamDetector.FindSteamPath();
                if (steamPath != null) installDir = AppCloudConfig.FindGameInstallDir(steamPath, game.AppId);
            }

            if (GameSaveAutoDetector.HasAntiCheatOrHypervisor(installDir, game.ProcessName))
            {
                blockReason = $"Game Space blocked: Anti-Cheat service or hypervisor detected for \"{game.Name}\".";
                return false;
            }
        }
        else
        {
            // When no game is active, ensure no background anti-cheat services are running
            if (GameSaveAutoDetector.HasAntiCheatOrHypervisor(null, null))
            {
                blockReason = "Game Space blocked: Background Anti-Cheat service detected.";
                return false;
            }
        }

        return true;
    }

    public bool IsVisible => _overlayWindow != null && _overlayWindow.IsVisible;

    /// <summary>
    /// Toggles Game Space visibility. If blocked by safety rules, displays a warning toast.
    /// </summary>
    public void Toggle(bool isPreview = false)
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.HideOverlay();
                return;
            }

            if (!isPreview && !CanOpenGameSpace(out var blockReason))
            {
                SteamToastService.ShowAuto(
                    "Game Space 🛡️",
                    blockReason ?? "Game Space is disabled for this game.");
                return;
            }

            ShowOverlay();
        });
    }

    public void ShowOverlay()
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new GameSpaceOverlayWindow();
                _overlayWindow.Closed += (_, _) => _overlayWindow = null;
            }

            _overlayWindow.ShowOverlay();
        });
    }

    public void CloseOverlay()
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.HideOverlay();
            }
        });
    }

    #region Notes & Checkpoint Helpers

    public static string GetGameNotesPath(string gameName)
    {
        var safe = SaveHistoryManager.SanitizeFolderName(gameName);
        var dir = Path.Combine(SteamDetector.GetConfigDir(), "game_notes");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{safe}.txt");
    }

    public static string LoadGameNotes(string gameName)
    {
        try
        {
            var path = GetGameNotesPath(gameName);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch { }
        return "";
    }

    public static void SaveGameNotes(string gameName, string notes)
    {
        try
        {
            var path = GetGameNotesPath(gameName);
            File.WriteAllText(path, notes ?? "");
        }
        catch { }
    }

    public static bool CreateCheckpoint(string gameName, string label, out string? error)
    {
        error = null;
        try
        {
            var game = ActiveGameTrackerService.CurrentGame;
            string? saveDir = null;

            if (game != null && game.UniversalProfile != null && !string.IsNullOrEmpty(game.UniversalProfile.SaveFolderPath))
            {
                saveDir = game.UniversalProfile.SaveFolderPath;
            }
            else if (game != null)
            {
                if (game.AppId > 0)
                {
                    var steamPath = SteamDetector.FindSteamPath();
                    saveDir = SaveHistoryManager.FindAppStorageDir(steamPath, game.AppId)
                              ?? GameSaveAutoDetector.DetectSaveFolder(game.Name, game.ProcessName, game.AppId);
                }
                else
                {
                    saveDir = GameSaveAutoDetector.DetectSaveFolder(game.Name, game.ProcessName);
                }
            }

            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
            {
                error = "Save folder not found or game has not saved yet.";
                return false;
            }

            var triggerDesc = string.IsNullOrWhiteSpace(label) ? "Manual Checkpoint" : $"Checkpoint: {label.Trim()}";
            var appIdStr = (game?.AppId ?? 0) > 0 ? game!.AppId.ToString() : null;

            var snapshot = SaveHistoryManager.CreateSnapshot(gameName, saveDir, triggerDesc, appIdStr);
            if (snapshot == null)
            {
                error = "Could not create snapshot (no save files found).";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool RestoreCheckpoint(string gameName, SnapshotInfo snapshot, out string? error)
    {
        error = null;
        try
        {
            var game = ActiveGameTrackerService.CurrentGame;
            string? saveDir = null;

            if (game != null && game.UniversalProfile != null && !string.IsNullOrEmpty(game.UniversalProfile.SaveFolderPath))
            {
                saveDir = game.UniversalProfile.SaveFolderPath;
            }
            else if (game != null)
            {
                if (game.AppId > 0)
                {
                    var steamPath = SteamDetector.FindSteamPath();
                    saveDir = SaveHistoryManager.FindAppStorageDir(steamPath, game.AppId)
                              ?? GameSaveAutoDetector.DetectSaveFolder(game.Name, game.ProcessName, game.AppId);
                }
                else
                {
                    saveDir = GameSaveAutoDetector.DetectSaveFolder(game.Name, game.ProcessName);
                }
            }

            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
            {
                error = "Target save directory not found.";
                return false;
            }

            return SaveHistoryManager.RestoreSnapshot(gameName, saveDir, snapshot);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    #endregion
}
