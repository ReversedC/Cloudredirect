using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CloudRedirect.Services;

public record ActiveGameInfo(
    uint AppId,
    string Name,
    string? HeaderUrl,
    string? ProcessName,
    bool IsUniversal,
    DateTime StartTime,
    bool IsLuaGame = false,
    bool HasSteamCloud = true,
    bool IsGenuineOwned = false,
    bool HasAntiCheat = false,
    bool IsCloudDenied = false,
    bool IsZeroLuaIntercepted = false,
    UniversalGameProfile? UniversalProfile = null,
    bool IsFreeGame = false,
    int ProcessId = 0
);

/// <summary>
/// Real-time Active Game Tracker.
/// Distinguishes between:
/// 1. Lua Games (redirected by CloudRedirect DLL).
/// 2. Genuine Steam Games with Steam Cloud (uses original native Steam Cloud untouched).
/// 3. Genuine Steam Games without Steam Cloud (auto-protected by CloudRedirect Universal Saves).
/// 4. Non-Steam / Anti-Cheat / Bypass Games (monitored by Universal Safe Mode).
/// </summary>
public static class ActiveGameTrackerService
{
    private static System.Threading.Timer? _pollTimer;
    private static ActiveGameInfo? _currentGame;
    private static string? _lastMonitoredUniversalProcess;
    private static UniversalGameProfile? _lastActiveUniversalProfile;

    public static ActiveGameInfo? CurrentGame => _currentGame;
    public static event Action<ActiveGameInfo?>? OnActiveGameChanged;

    public static void ClearActiveGame()
    {
        if (_currentGame != null)
        {
            _currentGame = null;
            _lastMidGameCheckpointTime = DateTime.MinValue;
            _lastTrackedSaveDir = null;
            OnActiveGameChanged?.Invoke(null);
            GameBoostService.RevertBoost();
        }
    }

    /// <summary>
    /// Checks whether any game is currently actively running (either tracked by CloudRedirect or indicated by Steam).
    /// </summary>
    public static bool IsAnyGameActive()
    {
        if (_currentGame != null) return true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false);
            var val = key?.GetValue("RunningAppId");
            if (val is int intVal && intVal > 0) return true;
            if (val is long longVal && longVal > 0) return true;
        }
        catch { }
        return false;
    }

    public static void Start()
    {
        if (_pollTimer != null) return;
        _pollTimer = new Timer(async _ => await CheckActiveGamesAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2.5));
    }

    public static void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    #region Win32 Process Image Discovery

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static string? GetProcessFilePath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(h, 0, sb, ref size))
            {
                return sb.ToString();
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    #endregion

    private static async Task CheckActiveGamesAsync()
    {
        try
        {
            GameSaveAutoDetector.CheckPeriodicCommunityDatabaseSync();

            // 1. Check Steam's native RunningAppId in registry
            uint runningAppId = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false);
                var val = key?.GetValue("RunningAppId");
                if (val is int intVal && intVal > 0)
                {
                    runningAppId = (uint)intVal;
                }
                else if (val is long longVal && longVal > 0)
                {
                    runningAppId = (uint)longVal;
                }
            }
            catch { }

            if (runningAppId > 0 && !SteamDetector.IsSteamRunning())
            {
                runningAppId = 0;
            }

            if (runningAppId > 0)
            {
                // If we are already tracking this Steam app, check if its process is still alive!
                if (_currentGame != null && _currentGame.AppId == runningAppId)
                {
                    bool isAlive = true;
                    if (_currentGame.ProcessId > 0)
                    {
                        try
                        {
                            using var p = Process.GetProcessById(_currentGame.ProcessId);
                            if (p.HasExited) isAlive = false;
                        }
                        catch
                        {
                            isAlive = false;
                        }
                    }

                    if (!isAlive && !string.IsNullOrEmpty(_currentGame.ProcessName))
                    {
                        var procs = Process.GetProcessesByName(_currentGame.ProcessName);
                        if (procs.Length > 0) isAlive = true;
                    }

                    if (!isAlive)
                    {
                        // The game process has exited; clear runningAppId to trigger immediate game exit handling below
                        runningAppId = 0;
                    }
                }
            }

            if (runningAppId > 0)
            {
                if (_currentGame == null || _currentGame.AppId != runningAppId)
                {
                    string name = $"Steam App {runningAppId}";
                    string? headerUrl = null;
                    StoreAppInfo? storeInfo = null;

                    try
                    {
                        var appMap = await SteamStoreClient.Shared.GetAppInfoAsync(new[] { runningAppId });
                        if (appMap.TryGetValue(runningAppId, out storeInfo))
                        {
                            if (!string.IsNullOrEmpty(storeInfo.Name))
                                name = storeInfo.Name;
                            headerUrl = storeInfo.HeaderUrl;
                        }
                    }
                    catch { }

                    // Classify the game:
                    bool isLuaGame = SteamDetector.IsLuaGame(runningAppId);
                    bool isZeroLua = SteamDetector.IsInterceptApp(runningAppId);
                    bool isIntercepted = isLuaGame || isZeroLua;
                    bool hasCloud = AppInfoParser.HasCloudSave(runningAppId);

                    bool hasCloudDenied = SteamDetector.HasCloudAccessDenied(runningAppId);
                    bool isSuoGame = SuoDetector.IsAppInCatalog(runningAppId);
                    bool isRemoteUnlocked = SteamDetector.IsRemoteUnlockedApp(runningAppId);
                    bool hasAppIdTxt = SteamDetector.HasAppIdTxt(runningAppId);

                    bool isFreeGame = (storeInfo?.IsFree == true) || SteamDetector.IsKnownFreeApp(runningAppId);

                    // Unlocked/Non-genuine without interception if cloud upload was rejected by Valve, in remote_unlock.json, in SUO catalog, or has steam_appid.txt
                    bool isUnlockedNoLua = !isIntercepted && (hasCloudDenied || isSuoGame || isRemoteUnlocked || hasAppIdTxt) && !isFreeGame;
                    bool isGenuine = (!isIntercepted && !isUnlockedNoLua) || isFreeGame;

                    string? procName = null;
                    string? installDir = null;
                    Process? matchedGameProcess = null;
                    try
                    {
                        var steamPath = SteamDetector.FindSteamPath();
                        if (steamPath != null)
                        {
                            installDir = AppCloudConfig.FindGameInstallDir(steamPath, runningAppId);
                        }

                        var installExeNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                        {
                            try
                            {
                                foreach (var exeFile in Directory.EnumerateFiles(installDir, "*.exe", SearchOption.AllDirectories))
                                {
                                    installExeNames.Add(Path.GetFileNameWithoutExtension(exeFile));
                                }
                            }
                            catch { }
                        }

                        string normalizedGameName = !string.IsNullOrEmpty(name)
                            ? name.Replace(" ", "").Replace(":", "").Replace("-", "")
                            : string.Empty;

                        var procs = Process.GetProcesses();
                        foreach (var p in procs)
                        {
                            try
                            {
                                if (p.Id <= 4) continue;

                                // 1. Check path using QueryFullProcessImageName (reliable across 32/64-bit and security contexts)
                                if (!string.IsNullOrEmpty(installDir))
                                {
                                    string? exePath = GetProcessFilePath(p.Id);
                                    if (!string.IsNullOrEmpty(exePath) && exePath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                                    {
                                        procName = p.ProcessName;
                                        matchedGameProcess = p;
                                        break;
                                    }
                                }

                                // 2. Check if process name matches any exe inside the game's install folder
                                if (installExeNames.Count > 0 && installExeNames.Contains(p.ProcessName))
                                {
                                    procName = p.ProcessName;
                                    matchedGameProcess = p;
                                    break;
                                }

                                // 3. Fallback: Check if process name matches game title (e.g. "Farm to Table" -> "FarmToTable")
                                if (!string.IsNullOrEmpty(normalizedGameName) && p.ProcessName.Equals(normalizedGameName, StringComparison.OrdinalIgnoreCase))
                                {
                                    procName = p.ProcessName;
                                    matchedGameProcess = p;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    bool hasAntiCheat = GameSaveAutoDetector.HasAntiCheatOrHypervisor(installDir, procName);

                    UniversalGameProfile? universalProfile = null;

                    // Evaluate:
                    // 1. Genuine / Free game:
                    // STRICT SAFETY DIRECTIVE: Legitimately owned or free-to-play games must NEVER
                    // be touched, redirected, or auto-enrolled into CloudRedirect, even if the game
                    // has no Steam Cloud support.
                    if (isGenuine || isFreeGame)
                    {
                        // Genuine owned and free games are completely left to Steam.
                    }
                    // 2. Unlocked game without interception (Cloud Denied or Depot game) -> auto-protect immediately via Universal Saves & Zero-Lua!
                    else if (isUnlockedNoLua)
                    {
                        // Proactively register into Zero-Lua interception and fix corrupted syncstate
                        SteamDetector.AddInterceptApp(runningAppId);
                        SteamDetector.FixRemoteCacheSyncState(runningAppId);

                        universalProfile = UniversalSaveWatcherService.FindProfile(runningAppId, procName, name);
                        if (universalProfile == null)
                        {
                            var saveFolder = GameSaveAutoDetector.DetectSaveFolder(name, procName, runningAppId);
                            if (saveFolder != null)
                            {
                                universalProfile = UniversalSaveWatcherService.AutoEnrollIfNeeded(
                                    name, procName, runningAppId, saveFolder, hasAntiCheat, isGenuine: false);

                                if (universalProfile != null && AppSettings.ShowSyncNotifications)
                                {
                                    TrayIconService.Instance.ShowNotification(
                                        "CloudRedirect Protection",
                                        $"{name} Steam Cloud upload was rejected by Valve. Save folder is now safeguarded by CloudRedirect!");
                                }
                            }
                        }
                    }
                    // 3. Intercepted game (Lua or Zero-Lua) without cloud saves or with anti-cheat
                    else if (isIntercepted && (!hasCloud || hasAntiCheat) && AppSettings.AutoProtectNonCloudGames)
                    {
                        universalProfile = UniversalSaveWatcherService.FindProfile(runningAppId, procName, name);
                        if (universalProfile == null)
                        {
                            var saveFolder = GameSaveAutoDetector.DetectSaveFolder(name, procName, runningAppId);
                            if (saveFolder != null)
                            {
                                universalProfile = UniversalSaveWatcherService.AutoEnrollIfNeeded(
                                    name, procName, runningAppId, saveFolder, hasAntiCheat, isGenuine: false);
                            }
                        }
                    }

                    if (universalProfile != null)
                    {
                        _lastActiveUniversalProfile = universalProfile;
                        _lastMonitoredUniversalProcess = procName ?? universalProfile.ProcessName;
                        UniversalSaveWatcherService.UpdateProfileStatus(universalProfile, "Game Running 🎮");
                    }

                    _currentGame = new ActiveGameInfo(
                        runningAppId,
                        name,
                        headerUrl,
                        procName,
                        universalProfile != null,
                        DateTime.Now,
                        IsLuaGame: isLuaGame,
                        HasSteamCloud: hasCloud,
                        IsGenuineOwned: isGenuine,
                        HasAntiCheat: hasAntiCheat,
                        IsCloudDenied: hasCloudDenied || isUnlockedNoLua,
                        IsZeroLuaIntercepted: isZeroLua,
                        UniversalProfile: universalProfile,
                        IsFreeGame: isFreeGame,
                        ProcessId: matchedGameProcess?.Id ?? 0
                    );

                    OnActiveGameChanged?.Invoke(_currentGame);

                    // Trigger Game Boost (Non-blocking async)
                    _ = GameBoostService.ApplyBoostAsync(matchedGameProcess, name, headerUrl, procName, installDir);
                }

                // Periodic Mid-Game Checkpoint for active Steam game & Smart RAM Purge
                if (_currentGame != null)
                {
                    CheckMidGameCheckpoint(_currentGame);
                    GameBoostService.CheckPeriodicSmartRam(_currentGame.ProcessId);
                }
                return;
            }

            // 2. If no Steam game is flagged in registry, check user-configured Universal Save Watcher games
            var universalProfiles = UniversalSaveWatcherService.GetProfiles().Where(p => p.Enabled).ToList();
            if (universalProfiles.Count > 0)
            {
                var processes = Process.GetProcesses();
                foreach (var profile in universalProfiles)
                {
                    if (string.IsNullOrWhiteSpace(profile.ProcessName)) continue;
                    if (GameSaveAutoDetector.IsSystemProcess(profile.ProcessName)) continue;

                    var targetProcName = profile.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileNameWithoutExtension(profile.ProcessName)
                        : profile.ProcessName;

                    var isRunning = processes.Any(p =>
                    {
                        try { return p.ProcessName.Equals(targetProcName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });

                    if (isRunning)
                    {
                        if (_currentGame == null || _currentGame.ProcessName != targetProcName)
                        {
                            var matchedProc = processes.FirstOrDefault(p =>
                            {
                                try { return p.ProcessName.Equals(targetProcName, StringComparison.OrdinalIgnoreCase); }
                                catch { return false; }
                            });

                            _currentGame = new ActiveGameInfo(
                                profile.SteamAppId,
                                profile.GameName,
                                null,
                                targetProcName,
                                true,
                                DateTime.Now,
                                IsLuaGame: false,
                                HasSteamCloud: false,
                                IsGenuineOwned: profile.IsGenuineSteamGame,
                                HasAntiCheat: profile.HasAntiCheat,
                                UniversalProfile: profile,
                                IsFreeGame: profile.SteamAppId > 0 && SteamStoreClient.Shared.IsFreeGame(profile.SteamAppId),
                                ProcessId: matchedProc?.Id ?? 0
                            );
                            _lastMonitoredUniversalProcess = targetProcName;
                            _lastActiveUniversalProfile = profile;

                            UniversalSaveWatcherService.UpdateProfileStatus(profile, "Game Running 🎮");
                            OnActiveGameChanged?.Invoke(_currentGame);

                            _ = GameBoostService.ApplyBoostAsync(matchedProc, profile.GameName, null, targetProcName, null);
                        }

                        // Periodic Mid-Game Checkpoint (Disaster Insurance) & Smart RAM Purge
                        CheckMidGameCheckpoint(_currentGame);
                        GameBoostService.CheckPeriodicSmartRam(_currentGame.ProcessId);
                        return;
                    }
                }
            }

            // 3. If a game was active and now stopped:
            if (_currentGame != null)
            {
                var exitedGame = _currentGame;
                _currentGame = null;
                _lastMidGameCheckpointTime = DateTime.MinValue;
                _lastTrackedSaveDir = null;
                OnActiveGameChanged?.Invoke(null);
                GameBoostService.RevertBoost();

                var gameName = exitedGame.Name;
                var appId = exitedGame.AppId;
                var procName = exitedGame.ProcessName;
                var universalProfile = exitedGame.UniversalProfile;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        // STRICT SAFETY DIRECTIVE: Genuine Steam games (purchased or free-to-play)
                        // must NEVER be touched, redirected, or backed up by CloudRedirect,
                        // regardless of whether they have Steam Cloud support or not.
                        if (exitedGame.IsGenuineOwned || exitedGame.IsFreeGame)
                        {
                            return;
                        }

                        // Ensure remotecache.vdf is repaired if Steam flagged syncstate 3
                        if (appId > 0)
                        {
                            SteamDetector.FixRemoteCacheSyncState(appId);
                        }

                        if (!AppSettings.AutoSyncOnGameExit)
                        {
                            return;
                        }

                        var sw = Stopwatch.StartNew();

                        if (universalProfile == null)
                        {
                            universalProfile = UniversalSaveWatcherService.FindProfile(appId, procName, gameName);
                        }

                        if (universalProfile != null)
                        {
                            _lastActiveUniversalProfile = null;
                            _lastMonitoredUniversalProcess = null;
                            bool ok = await UniversalSaveWatcherService.SyncProfileNowAsync(universalProfile, "Auto-Backup on Game Exit");
                            sw.Stop();

                            if (AppSettings.ShowSyncNotifications)
                            {
                                var message = ok
                                    ? $"☁️ {gameName}: Saves synchronized to cloud ({sw.Elapsed.TotalSeconds:F1}s)"
                                    : $"⚠️ {gameName}: Cloud sync encountered an issue upon exit.";
                                TrayIconService.Instance.ShowNotification("CloudRedirect", message);
                            }
                        }
                        else
                        {
                            // Wait briefly for game process to finish flushing saves
                            await Task.Delay(2000);

                            var steamPath = SteamDetector.FindSteamPath();
                            string? saveDir = null;
                            if (appId > 0)
                            {
                                saveDir = SaveHistoryManager.FindAppStorageDir(steamPath, appId, requireFiles: true)
                                          ?? GameSaveAutoDetector.DetectSaveFolder(gameName, procName, appId);
                            }
                            else
                            {
                                saveDir = GameSaveAutoDetector.DetectSaveFolder(gameName, procName);
                            }

                            if (saveDir != null && Directory.Exists(saveDir))
                            {
                                // If the save directory is an external directory (not inside Steam's redirected storage),
                                // auto-enroll it as a Universal Profile and sync to cloud!
                                bool isExternal = string.IsNullOrEmpty(steamPath) ||
                                                  !saveDir.StartsWith(steamPath, StringComparison.OrdinalIgnoreCase);

                                if (isExternal)
                                {
                                    var autoProf = UniversalSaveWatcherService.AutoEnrollIfNeeded(
                                        gameName, procName, appId, saveDir, hasAntiCheat: false, isGenuine: false);
                                    if (autoProf != null)
                                    {
                                        bool ok = await UniversalSaveWatcherService.SyncProfileNowAsync(autoProf, "Auto-Backup on Game Exit");
                                        sw.Stop();
                                        if (AppSettings.ShowSyncNotifications)
                                        {
                                            var message = ok
                                                ? $"☁️ {gameName}: Saves synchronized to cloud ({sw.Elapsed.TotalSeconds:F1}s)"
                                                : $"⚠️ {gameName}: Cloud sync encountered an issue upon exit.";
                                            TrayIconService.Instance.ShowNotification("CloudRedirect", message);
                                        }
                                        return;
                                    }
                                }

                                // Check for corruption and auto-heal if needed
                                if (AppSettings.AutoConflictHealing && SaveHistoryManager.CheckSaveCorruption(saveDir))
                                {
                                    if (SaveHistoryManager.TryAutoHealFromLastSnapshot(gameName, saveDir, out var healMsg, appId > 0 ? appId.ToString() : null))
                                    {
                                        TrayIconService.Instance.ShowNotification("CloudRedirect Auto-Heal", $"🩹 {gameName}: {healMsg}");
                                    }
                                }

                                var snapshot = SaveHistoryManager.CreateSnapshot(
                                    gameName,
                                    saveDir,
                                    "Auto-Backup on Game Exit",
                                    appId > 0 ? appId.ToString() : null);

                                sw.Stop();

                                if (snapshot != null && AppSettings.ShowSyncNotifications)
                                {
                                    TrayIconService.Instance.ShowNotification(
                                        "CloudRedirect",
                                        $"🛡️ {gameName}: Local save protected ({snapshot.FileCount} file(s) • {snapshot.FormattedSize}) in {sw.Elapsed.TotalSeconds:F1}s");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to auto-snapshot on exit for {gameName}: {ex}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ActiveGameTracker error: {ex}");
        }
    }

    private static DateTime _lastMidGameCheckpointTime = DateTime.MinValue;
    private static string? _lastTrackedSaveDir;

    private static void CheckMidGameCheckpoint(ActiveGameInfo game)
    {
        if (!AppSettings.AutoMidGameCheckpoint) return;

        // Never interfere with genuine Steam games (owned or free)
        if (game.IsGenuineOwned || game.IsFreeGame) return;

        try
        {
            var saveDir = game.UniversalProfile?.ExpandedSavePath;
            if (string.IsNullOrEmpty(saveDir))
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

            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) return;

            if (_lastMidGameCheckpointTime == DateTime.MinValue || _lastTrackedSaveDir != saveDir)
            {
                _lastMidGameCheckpointTime = DateTime.Now;
                _lastTrackedSaveDir = saveDir;
                return;
            }

            // Check if 5 minutes elapsed since last checkpoint
            if ((DateTime.Now - _lastMidGameCheckpointTime).TotalMinutes >= 5)
            {
                var files = Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    var newestFile = files.Max(f => File.GetLastWriteTime(f));
                    if (newestFile > _lastMidGameCheckpointTime)
                    {
                        var snap = SaveHistoryManager.CreateSnapshot(
                            game.Name,
                            saveDir,
                            "Mid-Game Checkpoint (Disaster Insurance)",
                            game.AppId > 0 ? game.AppId.ToString() : null);

                        _lastMidGameCheckpointTime = DateTime.Now;

                        if (snap != null && AppSettings.ShowSyncNotifications)
                        {
                            TrayIconService.Instance.ShowNotification(
                                "CloudRedirect Checkpoint",
                                $"🛡️ {game.Name}: Mid-game checkpoint saved (Disaster Insurance)");
                        }
                    }
                }
            }
        }
        catch { }
    }
}
