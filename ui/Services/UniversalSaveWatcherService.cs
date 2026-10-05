using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CloudRedirect.Resources;

namespace CloudRedirect.Services;

public class UniversalGameProfile : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));

    private string? _headerUrl;
    private string _status = "Monitoring";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string SaveFolderPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime? LastSyncTime { get; set; }
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(LocalizedStatus));
            }
        }
    }
    public uint SteamAppId { get; set; } = 0;
    public bool IsAutoEnrolled { get; set; } = false;
    public bool IsGenuineSteamGame { get; set; } = false;
    public bool HasAntiCheat { get; set; } = false;

    public bool HasSteamAppId => SteamAppId > 0;
    public bool HasProcess => !string.IsNullOrWhiteSpace(ProcessName);
    
    public string? HeaderUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(_headerUrl))
                return _headerUrl;
            return SteamAppId > 0
                ? $"https://cdn.akamai.steamstatic.com/steam/apps/{SteamAppId}/header.jpg"
                : null;
        }
        set
        {
            if (_headerUrl != value)
            {
                _headerUrl = value;
                OnPropertyChanged(nameof(HeaderUrl));
            }
        }
    }

    public string LocalizedStatus
    {
        get
        {
            if (string.Equals(Status, "Monitoring", StringComparison.OrdinalIgnoreCase))
                return S.Get("UniversalSaves_Status_Monitoring");
            if (string.Equals(Status, "Up to Date", StringComparison.OrdinalIgnoreCase))
                return S.Get("UniversalSaves_Status_UpToDate");
            if (string.Equals(Status, "Syncing...", StringComparison.OrdinalIgnoreCase))
                return S.Get("UniversalSaves_Status_Syncing");
            if (Status.Contains("Game Running", StringComparison.OrdinalIgnoreCase))
                return S.Get("UniversalSaves_Status_GameRunning");
            if (string.Equals(Status, "No Saves Yet", StringComparison.OrdinalIgnoreCase))
                return S.Get("UniversalSaves_Status_NoSavesYet");
            return Status;
        }
    }

    public string ProtectionTypeTag
    {
        get => IsGenuineSteamGame
            ? S.Get("UniversalSaves_Tag_GenuineSteam")
            : (HasAntiCheat ? S.Get("UniversalSaves_Tag_AntiCheatSafe") : (IsAutoEnrolled ? S.Get("UniversalSaves_Tag_AutoProtected") : S.Get("UniversalSaves_Tag_Custom")));
        set { }
    }

    public string ExpandedSavePath
    {
        get
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(SaveFolderPath);
                if (expanded.StartsWith("~"))
                {
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    expanded = Path.Combine(userProfile, expanded.TrimStart('~', '/', '\\'));
                }
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return SaveFolderPath;
            }
        }
        set { }
    }

    public bool FolderExists
    {
        get => Directory.Exists(ExpandedSavePath);
        set { }
    }
}

/// <summary>
/// Universal Out-of-Process Save Watcher.
/// Allows games without native Steam Cloud support or games protected with Hypervisors / Anti-Cheat
/// (EAC, BattlEye, Vanguard) to safely backup and synchronize saves to the cloud with zero DLL injection.
/// </summary>
public static class UniversalSaveWatcherService
{
    public static event Action? OnProfilesChanged;
    public static event Action<UniversalGameProfile>? OnProfileStatusChanged;

    public static readonly List<UniversalGameProfile> PopularPresets =
    [
        new()
        {
            GameName = "Elden Ring",
            ProcessName = "eldenring",
            SaveFolderPath = @"%APPDATA%\EldenRing"
        },
        new()
        {
            GameName = "Baldur's Gate 3",
            ProcessName = "bg3",
            SaveFolderPath = @"%LOCALAPPDATA%\Larian Studios\Baldur's Gate 3\PlayerProfiles\Public\Savegames\Story"
        },
        new()
        {
            GameName = "Black Myth: Wukong",
            ProcessName = "b1-Win64-Shipping",
            SaveFolderPath = @"%LOCALAPPDATA%\b1\Saved\SaveGames"
        },
        new()
        {
            GameName = "Cyberpunk 2077",
            ProcessName = "Cyberpunk2077",
            SaveFolderPath = @"%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077"
        },
        new()
        {
            GameName = "Palworld",
            ProcessName = "Palworld-Win64-Shipping",
            SaveFolderPath = @"%LOCALAPPDATA%\Pal\Saved\SaveGames"
        },
        new()
        {
            GameName = "Dark Souls III",
            ProcessName = "DarkSoulsIII",
            SaveFolderPath = @"%APPDATA%\DarkSoulsIII"
        },
        new()
        {
            GameName = "Starfield",
            ProcessName = "Starfield",
            SaveFolderPath = @"%USERPROFILE%\Documents\My Games\Starfield\Saves"
        },
        new()
        {
            GameName = "Sekiro: Shadows Die Twice",
            ProcessName = "sekiro",
            SaveFolderPath = @"%APPDATA%\Sekiro"
        },
        new()
        {
            GameName = "Minecraft Dungeons",
            ProcessName = "Dungeons",
            SaveFolderPath = @"%LOCALAPPDATA%\Dungeons2\Saved\SaveGames"
        }
    ];

    private static List<UniversalGameProfile>? _profiles;

    public static string GetConfigPath()
    {
        return Path.Combine(SteamDetector.GetConfigDir(), "universal_saves.json");
    }

    public static List<UniversalGameProfile> GetProfiles()
    {
        if (_profiles != null) return _profiles;

        try
        {
            var path = GetConfigPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _profiles = JsonSerializer.Deserialize<List<UniversalGameProfile>>(json) ?? [];

                // Sanitize: remove any spurious system processes, clean up auto-enrolled genuine games, and fix duplicate statuses
                int countBefore = _profiles.Count;
                bool modified = false;
                _profiles.RemoveAll(p => GameSaveAutoDetector.IsSystemProcess(p.ProcessName) ||
                                         p.GameName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) ||
                                         p.ProcessName.Contains("antigravity", StringComparison.OrdinalIgnoreCase) ||
                                         p.GameName.Equals("Windows Input Experience", StringComparison.OrdinalIgnoreCase) ||
                                         (p.IsAutoEnrolled && p.IsGenuineSteamGame));
                if (_profiles.Count != countBefore) modified = true;

                foreach (var p in _profiles)
                {
                    if (string.IsNullOrWhiteSpace(p.Status) || p.Status.StartsWith("Auto-Protected", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Status = "Monitoring";
                        modified = true;
                    }

                    if (p.IsGenuineSteamGame && p.SteamAppId > 0 && SteamDetector.IsAppUnlocked(p.SteamAppId))
                    {
                        p.IsGenuineSteamGame = false;
                        modified = true;
                    }
                }

                if (modified)
                {
                    SaveProfiles();
                }

                return _profiles;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load universal saves config: {ex}");
        }

        _profiles = [];
        return _profiles;
    }

    public static void SaveProfiles()
    {
        try
        {
            var path = GetConfigPath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_profiles ?? [], new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            OnProfilesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save universal saves config: {ex}");
        }
    }

    public static UniversalGameProfile? FindProfile(uint appId, string? processName, string? gameName)
    {
        var list = GetProfiles();
        if (appId > 0)
        {
            var match = list.FirstOrDefault(p => p.SteamAppId == appId);
            if (match != null) return match;
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            var cleanProc = Path.GetFileNameWithoutExtension(processName).Trim();
            var match = list.FirstOrDefault(p =>
            {
                if (string.IsNullOrWhiteSpace(p.ProcessName)) return false;
                var pClean = Path.GetFileNameWithoutExtension(p.ProcessName).Trim();
                return pClean.Equals(cleanProc, StringComparison.OrdinalIgnoreCase) ||
                       pClean.Equals(processName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       p.ProcessName.Trim().Equals(cleanProc, StringComparison.OrdinalIgnoreCase) ||
                       p.ProcessName.Trim().Equals(processName.Trim(), StringComparison.OrdinalIgnoreCase);
            });
            if (match != null)
            {
                if (match.SteamAppId == 0 && appId > 0)
                {
                    match.SteamAppId = appId;
                    SaveProfiles();
                }
                return match;
            }
        }

        if (!string.IsNullOrWhiteSpace(gameName))
        {
            var cleanGame = gameName.Trim();
            var match = list.FirstOrDefault(p =>
                p.GameName.Equals(cleanGame, StringComparison.OrdinalIgnoreCase) ||
                cleanGame.Contains(p.GameName, StringComparison.OrdinalIgnoreCase) ||
                p.GameName.Contains(cleanGame, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                if (match.SteamAppId == 0 && appId > 0)
                {
                    match.SteamAppId = appId;
                    SaveProfiles();
                }
                return match;
            }
        }

        return null;
    }

    public static UniversalGameProfile? AutoEnrollIfNeeded(string gameName, string? processName, uint appId, string saveFolderPath, bool hasAntiCheat = false, bool isGenuine = false)
    {
        if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(saveFolderPath))
            return null;

        var existing = FindProfile(appId, processName, gameName);
        if (existing != null)
            return existing;

        if (isGenuine && appId > 0 && !SteamDetector.IsAppUnlocked(appId))
        {
            // Do NOT auto-enroll genuine owned or free games!
            return null;
        }

        if (isGenuine && appId > 0 && SteamDetector.IsAppUnlocked(appId))
        {
            isGenuine = false;
        }

        var profile = new UniversalGameProfile
        {
            GameName = gameName,
            ProcessName = processName ?? "",
            SteamAppId = appId,
            SaveFolderPath = saveFolderPath,
            Enabled = true,
            IsAutoEnrolled = true,
            IsGenuineSteamGame = isGenuine,
            HasAntiCheat = hasAntiCheat,
            Status = "Monitoring"
        };

        AddProfile(profile);
        return profile;
    }

    public static void AddProfile(UniversalGameProfile profile)
    {
        if (GameSaveAutoDetector.IsSystemProcess(profile.ProcessName) ||
            profile.GameName.Equals("Windows Input Experience", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (profile.IsGenuineSteamGame && profile.SteamAppId > 0 && SteamDetector.IsAppUnlocked(profile.SteamAppId))
        {
            profile.IsGenuineSteamGame = false;
        }

        var list = GetProfiles();
        if (list.Any(p => p.GameName.Equals(profile.GameName, StringComparison.OrdinalIgnoreCase) ||
                         (profile.SteamAppId > 0 && p.SteamAppId == profile.SteamAppId)))
            return;

        list.Add(profile);
        SaveProfiles();
    }

    public static void RemoveProfile(string id)
    {
        var list = GetProfiles();
        var item = list.FirstOrDefault(p => p.Id == id);
        if (item != null)
        {
            list.Remove(item);
            SaveProfiles();
        }
    }

    public static void UpdateProfileStatus(UniversalGameProfile profile, string status)
    {
        profile.Status = status;
        OnProfileStatusChanged?.Invoke(profile);
    }

    public static async Task<bool> SyncProfileNowAsync(UniversalGameProfile profile, string triggerReason = "Manual Sync")
    {
        try
        {
            if (profile.IsGenuineSteamGame)
            {
                // Never auto-sync genuine games on game exit or checkpoints
                if (triggerReason.Contains("Exit", StringComparison.OrdinalIgnoreCase) ||
                    triggerReason.Contains("Auto", StringComparison.OrdinalIgnoreCase) ||
                    triggerReason.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            var saveDir = profile.ExpandedSavePath;
            if (!Directory.Exists(saveDir))
            {
                UpdateProfileStatus(profile, "Folder Not Found");
                return false;
            }

            var saveFiles = Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories);
            if (saveFiles.Length == 0)
            {
                UpdateProfileStatus(profile, "No Saves Yet");
                TrayIconService.Instance.ShowNotification("Universal Cloud Saves", $"{profile.GameName}: Save folder is currently empty. Play the game and save first.");
                return false;
            }

            if (AppSettings.AutoConflictHealing && SaveHistoryManager.CheckSaveCorruption(saveDir))
            {
                if (SaveHistoryManager.TryAutoHealFromLastSnapshot(profile.GameName, saveDir, out var healMsg, profile.SteamAppId > 0 ? profile.SteamAppId.ToString() : null))
                {
                    TrayIconService.Instance.ShowNotification("CloudRedirect", $"🩹 {profile.GameName}: {healMsg}");
                }
            }

            UpdateProfileStatus(profile, "Syncing...");

            // 1. Create a versioned local snapshot first (Advanced Save Protection)
            var snapshot = SaveHistoryManager.CreateSnapshot(profile.GameName, saveDir, triggerReason);

            // 2. Upload to Cloud Provider (Google Drive, Local Folder, OneDrive)
            var syncResult = await UniversalCloudSyncService.UploadProfileSavesToCloudAsync(profile);

            profile.LastSyncTime = DateTime.Now;
            UpdateProfileStatus(profile, syncResult.Success ? "Up to Date" : "Sync Warning");
            SaveProfiles();

            var message = syncResult.Success
                ? $"☁️ {profile.GameName}: Saves synchronized to cloud."
                : $"⚠️ {profile.GameName}: Local snapshot saved. Cloud sync: {syncResult.Message}";

            TrayIconService.Instance.ShowNotification("CloudRedirect", message);
            return syncResult.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error syncing universal profile {profile.GameName}: {ex}");
            UpdateProfileStatus(profile, "Sync Error");
            return false;
        }
    }
}
