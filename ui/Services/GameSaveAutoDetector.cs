using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CloudRedirect.Services;

public record DetectedGameSave(
    string GameName,
    string ProcessName,
    string SaveFolderPath,
    int FileCount,
    long TotalBytes,
    DateTime LastModified
)
{
    public string FormattedSize
    {
        get
        {
            if (TotalBytes < 1024) return $"{TotalBytes} B";
            if (TotalBytes < 1024 * 1024) return $"{TotalBytes / 1024.0:F1} KB";
            return $"{TotalBytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}

/// <summary>
/// Smart Auto-Detection engine for games and their save file locations on Windows.
/// Combines Steam AutoCloud rules, community path databases, fuzzy folder matching,
/// and real-time running process inspection.
/// </summary>
public static class GameSaveAutoDetector
{
    // Common root directories where 99% of Windows PC games store saves
    private static readonly string[] CommonSaveRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), // %APPDATA%
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), // %LOCALAPPDATA%
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"), // LocalLow
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"), // Saved Games
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"), // Documents\My Games
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), // Documents
    ];

    // Known game save signatures (matching by process or name keywords)
    private static readonly Dictionary<string, string[]> KnownGameSaveRelativePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        { "eldenring", [@"%APPDATA%\EldenRing"] },
        { "bg3", [@"%LOCALAPPDATA%\Larian Studios\Baldur's Gate 3\PlayerProfiles\Public\Savegames"] },
        { "b1-Win64-Shipping", [@"%LOCALAPPDATA%\b1\Saved\SaveGames"] },
        { "Cyberpunk2077", [@"%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077"] },
        { "Palworld-Win64-Shipping", [@"%LOCALAPPDATA%\Pal\Saved\SaveGames"] },
        { "DarkSoulsIII", [@"%APPDATA%\DarkSoulsIII"] },
        { "DarkSoulsII", [@"%APPDATA%\DarkSoulsII"] },
        { "DarkSoulsRemastered", [@"%LOCALAPPDATA%\VirtualStore\Program Files\BANDAI NAMCO", @"Documents\NBGI\DARK SOULS REMASTERED"] },
        { "Starfield", [@"Documents\My Games\Starfield\Saves"] },
        { "sekiro", [@"%APPDATA%\Sekiro"] },
        { "armoredcore6", [@"%APPDATA%\ArmoredCore6"] },
        { "witcher3", [@"Documents\The Witcher 3\gamesaves"] },
        { "HogwartsLegacy", [@"%LOCALAPPDATA%\Hogwarts Legacy\Saved\SaveGames"] },
        { "LiesofP-Win64-Shipping", [@"%LOCALAPPDATA%\LiesofP\Saved\SaveGames"] },
        { "GodOfWar", [@"%USERPROFILE%\Saved Games\God of War"] },
        { "GodOfWarRagnarok", [@"%USERPROFILE%\Saved Games\God of War Ragnarök"] },
        { "GhostOfTsushima", [@"Documents\Ghost of Tsushima DIRECTOR'S CUT"] },
        { "HorizonZeroDawn", [@"Documents\Horizon Zero Dawn\Saved Games"] },
        { "HorizonForbiddenWest", [@"Documents\Horizon Forbidden West Complete Edition"] },
        { "Hades", [@"Documents\Saved Games\Hades"] },
        { "Hades2", [@"%USERPROFILE%\Saved Games\Hades II"] },
        { "Hollow Knight", [@"%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight"] },
        { "DeadCells", [@"%APPDATA%\DeadCells"] },
        { "MonsterHunterWorld", [@"%APPDATA%\MonsterHunterWorld"] },
        { "MonsterHunterRise", [@"%APPDATA%\MonsterHunterRise"] },
        { "Fallout4", [@"Documents\My Games\Fallout4\Saves"] },
        { "SkyrimSE", [@"Documents\My Games\Skyrim Special Edition\Saves"] },
        { "Stardew Valley", [@"%APPDATA%\StardewValley\Saves"] },
        { "Terraria", [@"Documents\My Games\Terraria\Players"] },
        { "SlayTheSpire", [@"%USERPROFILE%\.prefs"] },
        { "Balatro", [@"%APPDATA%\Balatro"] },
        { "ManorLords-Win64-Shipping", [@"%LOCALAPPDATA%\ManorLords\Saved\SaveGames"] },
        { "Helldivers2", [@"%APPDATA%\Arrowhead\Helldivers2"] },
        { "Valheim", [@"%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local"] },
        { "DaveTheDiver", [@"%USERPROFILE%\AppData\LocalLow\Mintrocket\DAVE THE DIVER"] },
        { "ResidentEvil4", [@"%APPDATA%\RE4", @"%LOCALAPPDATA%\Capcom\RE4"] },
        { "ResidentEvilVillage", [@"Documents\CAPCOM\RESIDENT EVIL VILLAGE"] },
        { "Persona5Royal", [@"%APPDATA%\SEGA\P5R"] },
        { "Persona3Reload", [@"%APPDATA%\SEGA\P3R"] },
        { "Subnautica", [@"%USERPROFILE%\AppData\LocalLow\Unknown Worlds\Subnautica\SavedGames"] },
        { "NoMansSky", [@"%APPDATA%\HelloGames\NMS"] },
        { "RDR2", [@"Documents\Rockstar Games\Red Dead Redemption 2\Profiles"] },
        { "GTA5", [@"Documents\Rockstar Games\GTA V\Profiles"] },
        // Popular Emulators
        { "Ryujinx", [@"%APPDATA%\Ryujinx\bis\user\save"] },
        { "rpcs3", [@"%USERPROFILE%\AppData\Local\RPCS3\dev_hdd0\home"] },
        { "Dolphin", [@"Documents\Dolphin Emulator\GC", @"Documents\Dolphin Emulator\Wii", @"%APPDATA%\Dolphin Emulator\GC"] },
        { "pcsx2", [@"Documents\PCSX2\memcards", @"%USERPROFILE%\Documents\PCSX2\memcards"] },
        { "retroarch", [@"%APPDATA%\RetroArch\saves"] }
    };

    /// <summary>
    /// Auto-detects the save folder for a game given its name, process name, or Steam AppID.
    /// </summary>
    public static string? DetectSaveFolder(string gameName, string? processName = null, uint appId = 0)
    {
        // 1. Check Steam AutoCloud rules from appinfo.vdf if Steam and AppId are available
        if (appId > 0)
        {
            try
            {
                var steamPath = SteamDetector.FindSteamPath();
                if (steamPath != null)
                {
                    var appInfoPath = Path.Combine(steamPath, "appcache", "appinfo.vdf");
                    if (File.Exists(appInfoPath))
                    {
                        var config = AppInfoParser.ParseSingle(appInfoPath, appId);
                        if (config != null && config.SaveFiles.Count > 0)
                        {
                            var gameInstallDir = AppCloudConfig.FindGameInstallDir(steamPath, appId);
                            foreach (var rule in config.SaveFiles)
                            {
                                var rootPath = AppCloudConfig.RootToFilesystemPath(rule.Root, gameInstallDir);
                                if (rootPath != null)
                                {
                                    var fullCandidate = string.IsNullOrEmpty(rule.Path)
                                        ? rootPath
                                        : Path.Combine(rootPath, rule.Path);

                                    if (Directory.Exists(fullCandidate))
                                        return fullCandidate;
                                }
                            }
                        }
                    }

                    // Check Steam userdata remote folder
                    var userdataDir = Path.Combine(steamPath, "userdata");
                    if (Directory.Exists(userdataDir))
                    {
                        foreach (var accDir in Directory.GetDirectories(userdataDir))
                        {
                            var remotePath = Path.Combine(accDir, appId.ToString(), "remote");
                            if (Directory.Exists(remotePath) && Directory.GetFiles(remotePath, "*", SearchOption.AllDirectories).Length > 0)
                                return remotePath;
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Check known signature table by process or game name (Community Database)
        if (AppSettings.AutoCommunityDatabase)
        {
            var keysToCheck = new List<string>();
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var cleanProc = Path.GetFileNameWithoutExtension(processName).Trim();
                keysToCheck.Add(cleanProc);
            }
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                keysToCheck.Add(gameName.Trim());
                keysToCheck.Add(gameName.Replace(" ", "").Trim());
            }

            foreach (var key in keysToCheck)
            {
                foreach (var kvp in KnownGameSaveRelativePaths)
                {
                    if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                        key.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                        kvp.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var rawPath in kvp.Value)
                        {
                            var expanded = ResolvePathWithWildcards(rawPath);
                            if (expanded != null && Directory.Exists(expanded))
                                return expanded;
                        }
                    }
                }
            }
        }

        // 3. Dynamic fuzzy search across common save roots
        var candidateTokens = GenerateFuzzyTokens(gameName, processName);
        foreach (var root in CommonSaveRoots)
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var dirName = Path.GetFileName(dir);
                    if (candidateTokens.Any(token => dirName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Check if this directory or a subfolder contains saves
                        var saveSubDir = FindSaveSubfolder(dir);
                        if (saveSubDir != null)
                            return saveSubDir;

                        return dir;
                    }

                    // For studio folders like "Larian Studios", "CD Projekt Red", "FromSoftware", check 1 level down
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(dir))
                        {
                            var subName = Path.GetFileName(sub);
                            if (candidateTokens.Any(token => subName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                            {
                                var deepSave = FindSaveSubfolder(sub);
                                return deepSave ?? sub;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 4. Online-Fix, Goldberg, CODEX, Rune, and Steam bypass emulator locations
        if (appId > 0)
        {
            var bypassCandidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "OnlineFix", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Steam", "CODEX", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Steam", "RUNE", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnlineFix", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnlineFix", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steam", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FLT", appId.ToString()),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EMPRESS", appId.ToString())
            };
            foreach (var loc in bypassCandidates)
            {
                if (Directory.Exists(loc) && Directory.EnumerateFileSystemEntries(loc).Any())
                    return loc;
            }
        }

        // 5. Game install directory search (Unreal Engine Saved/SaveGames, standalone Saves, etc.)
        try
        {
            var steamPath = SteamDetector.FindSteamPath();
            string? installDir = null;
            if (steamPath != null && appId > 0)
            {
                installDir = AppCloudConfig.FindGameInstallDir(steamPath, appId);
            }

            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                var foundInInstall = ScanInstallDirForSaves(installDir);
                if (foundInInstall != null)
                    return foundInInstall;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Scans a game's install directory for local save folders (e.g. Unreal Engine Saved/SaveGames, OnlineFix, or standalone Save folders).
    /// </summary>
    public static string? ScanInstallDirForSaves(string installDir)
    {
        try
        {
            var candidateDirs = new[]
            {
                Path.Combine(installDir, "Save"),
                Path.Combine(installDir, "Saves"),
                Path.Combine(installDir, "SaveGames"),
                Path.Combine(installDir, "SaveData"),
                Path.Combine(installDir, "steam_settings"),
                Path.Combine(installDir, "Profile"),
                Path.Combine(installDir, "OnlineFix"),
                Path.Combine(installDir, "AppData")
            };

            foreach (var cd in candidateDirs)
            {
                if (Directory.Exists(cd) && Directory.EnumerateFileSystemEntries(cd).Any())
                    return cd;
            }

            // Check standard Unreal Engine layout: <installDir>/<SubFolder>/Saved/SaveGames
            foreach (var sub in Directory.GetDirectories(installDir))
            {
                var ueSaved = Path.Combine(sub, "Saved", "SaveGames");
                if (Directory.Exists(ueSaved) && Directory.EnumerateFileSystemEntries(ueSaved).Any())
                    return ueSaved;

                var ueSavedRoot = Path.Combine(sub, "Saved");
                if (Directory.Exists(ueSavedRoot) && Directory.EnumerateFileSystemEntries(ueSavedRoot).Any())
                    return ueSavedRoot;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Detects whether a game uses an Anti-Cheat or Hypervisor system (EAC, BattlEye, Vanguard, ACE, etc.).
    /// </summary>
    public static bool HasAntiCheatOrHypervisor(string? installDir, string? processName = null)
    {
        try
        {
            // 1. Check running processes for known anti-cheat services/drivers
            var knownAntiCheatProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_Setup",
                "BEService", "BEService_x64",
                "vgk", "vgc",
                "ACE-BASE", "AntiCheatExpert",
                "BlackCipher",
                "GameMon", "GameMon64",
                "XIGNCODE"
            };

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (knownAntiCheatProcs.Contains(proc.ProcessName))
                        return true;
                }
                catch { }
            }

            // 2. Check game installation folder
            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                var indicators = new[]
                {
                    "EasyAntiCheat", "EasyAntiCheat_EOS.dll", "easyanticheat_x64.dll", "easyanticheat_x86.dll",
                    "BattlEye", "BEService_x64.exe", "BEService.exe", "BEDaisy.sys",
                    "vgk.sys", "vgc.exe",
                    "AntiCheatExpert", "ACE-BASE.sys",
                    "BlackCipher", "GameMon.des",
                    "dbdata.dll"
                };

                foreach (var ind in indicators)
                {
                    if (File.Exists(Path.Combine(installDir, ind)) || Directory.Exists(Path.Combine(installDir, ind)))
                        return true;
                }

                // Check 1 level down
                foreach (var sub in Directory.GetDirectories(installDir))
                {
                    var subName = Path.GetFileName(sub);
                    if (subName.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
                        subName.Contains("BattlEye", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Scans currently running processes and returns detected games with their auto-discovered save folders.
    /// </summary>
    public static List<DetectedGameSave> DetectFromRunningProcesses()
    {
        var result = new List<DetectedGameSave>();
        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (string.IsNullOrEmpty(proc.MainWindowTitle)) continue;
                    var procName = proc.ProcessName;

                    // Filter out common non-game system processes
                    if (IsSystemProcess(procName)) continue;

                    var gameTitle = proc.MainWindowTitle.Trim();
                    // Strip typical suffixes like "(64-bit)", "DirectX 12", etc.
                    gameTitle = CleanWindowTitle(gameTitle);

                    var saveFolder = DetectSaveFolder(gameTitle, procName);
                    if (!string.IsNullOrEmpty(saveFolder) && Directory.Exists(saveFolder))
                    {
                        var files = Directory.GetFiles(saveFolder, "*", SearchOption.AllDirectories);
                        long bytes = files.Sum(f => new FileInfo(f).Length);
                        var lastMod = files.Length > 0 ? files.Max(f => File.GetLastWriteTime(f)) : Directory.GetLastWriteTime(saveFolder);

                        result.Add(new DetectedGameSave(gameTitle, procName, saveFolder, files.Length, bytes, lastMod));
                    }
                }
                catch { }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Scans the PC for all game save folders that currently exist on disk.
    /// </summary>
    public static List<DetectedGameSave> ScanInstalledGameSaves()
    {
        var results = new List<DetectedGameSave>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check known signature games (Community Database)
        if (AppSettings.AutoCommunityDatabase)
        {
            foreach (var kvp in KnownGameSaveRelativePaths)
            {
                foreach (var raw in kvp.Value)
                {
                    var expanded = ResolvePathWithWildcards(raw);
                    if (expanded != null && Directory.Exists(expanded) && seenPaths.Add(expanded))
                    {
                        try
                        {
                            var files = Directory.GetFiles(expanded, "*", SearchOption.AllDirectories);
                            if (files.Length > 0)
                            {
                                long totalBytes = files.Sum(f => new FileInfo(f).Length);
                                var lastMod = files.Max(f => File.GetLastWriteTime(f));
                                var cleanName = FormatGameNameFromToken(kvp.Key);
                                results.Add(new DetectedGameSave(cleanName, kvp.Key, expanded, files.Length, totalBytes, lastMod));
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        // 2. Scan CommonSaveRoots for existing game directories
        foreach (var root in CommonSaveRoots)
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var dirName = Path.GetFileName(dir);
                    if (IsIgnoredSystemFolder(dirName)) continue;

                    // If folder contains save indicators or subdirectories like "SavedGames", "Saves", "Save"
                    var saveDir = FindSaveSubfolder(dir) ?? dir;
                    if (seenPaths.Add(saveDir))
                    {
                        try
                        {
                            var files = Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories);
                            if (files.Length > 0 && files.Length < 1000)
                            {
                                long totalBytes = files.Sum(f => new FileInfo(f).Length);
                                var lastMod = files.Max(f => File.GetLastWriteTime(f));
                                results.Add(new DetectedGameSave(dirName, dirName, saveDir, files.Length, totalBytes, lastMod));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        return results.OrderByDescending(r => r.LastModified).ToList();
    }

    private static string? ResolvePathWithWildcards(string rawPath)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(rawPath);
            if (expanded.StartsWith("Documents", StringComparison.OrdinalIgnoreCase))
            {
                expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    expanded.Substring("Documents".Length).TrimStart('\\', '/'));
            }

            if (expanded.Contains('*'))
            {
                var dir = Path.GetDirectoryName(expanded);
                var pattern = Path.GetFileName(expanded);
                if (dir != null && Directory.Exists(dir))
                {
                    var matches = Directory.GetDirectories(dir, pattern);
                    if (matches.Length > 0) return matches[0];
                }
            }

            return Directory.Exists(expanded) ? expanded : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSaveSubfolder(string dir)
    {
        try
        {
            var subdirs = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub).ToLowerInvariant();
                if (name is "savegames" or "saves" or "save" or "saved" or "savedata" or "saved games" or "playerprofiles")
                    return sub;
            }
        }
        catch { }
        return null;
    }

    private static HashSet<string> GenerateFuzzyTokens(string gameName, string? processName)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(gameName))
        {
            tokens.Add(gameName.Trim());
            tokens.Add(gameName.Replace(" ", "").Trim());
            tokens.Add(gameName.Replace(":", "").Replace("'", "").Replace("-", " ").Trim());

            var words = gameName.Split([' ', ':', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.Length >= 4)
                    tokens.Add(word);
            }
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            var cleanProc = Path.GetFileNameWithoutExtension(processName).Trim();
            tokens.Add(cleanProc);
            if (cleanProc.Contains("-Win64-Shipping"))
                tokens.Add(cleanProc.Replace("-Win64-Shipping", ""));
        }

        return tokens;
    }

    private static string CleanWindowTitle(string title)
    {
        var removeList = new[] { "DirectX 12", "DirectX 11", "Vulkan", "v1.", "v2.", "v0.", "(64-bit)", "(32-bit)", "(x64)" };
        foreach (var rem in removeList)
        {
            int idx = title.IndexOf(rem, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) title = title.Substring(0, idx);
        }
        return title.Trim(' ', '-', '·', ':');
    }

    private static string FormatGameNameFromToken(string token)
    {
        return token switch
        {
            "eldenring" => "Elden Ring",
            "bg3" => "Baldur's Gate 3",
            "b1-Win64-Shipping" => "Black Myth: Wukong",
            "Cyberpunk2077" => "Cyberpunk 2077",
            "Palworld-Win64-Shipping" => "Palworld",
            "DarkSoulsIII" => "Dark Souls III",
            "sekiro" => "Sekiro: Shadows Die Twice",
            "armoredcore6" => "Armored Core VI",
            "HogwartsLegacy" => "Hogwarts Legacy",
            "LiesofP-Win64-Shipping" => "Lies of P",
            _ => token
        };
    }

    public static bool IsSystemProcess(string procName)
    {
        var sys = new[]
        {
            "explorer", "devenv", "chrome", "firefox", "msedge", "brave", "code", "steam",
            "taskmgr", "cmd", "powershell", "CloudRedirect", "discord", "spotify", "slack",
            "system", "idle", "svchost", "csrss", "dwm", "runtimebroker", "searchhost",
            "textinputhost", "shellexperiencehost", "applicationframehost", "startmenuexperiencehost",
            "widgets", "ctfmon", "conhost", "sihost", "fontdrvhost", "antigravity", "antigravity-manager",
            "node", "python", "cursor", "git", "bash", "wsl", "windowsterminal", "gemini"
        };
        return sys.Any(s => s.Equals(procName, StringComparison.OrdinalIgnoreCase) || procName.Contains("antigravity", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIgnoredSystemFolder(string name)
    {
        var ignore = new[]
        {
            "Microsoft", "Windows", "Adobe", "Google", "Intel", "NVIDIA", "AMD", "Apple",
            "Temp", "CrashDumps", "Logs", "CloudRedirect", "Antigravity", "Gemini", "Packages"
        };
        return ignore.Any(i => i.Equals(name, StringComparison.OrdinalIgnoreCase) || name.Contains("Antigravity", StringComparison.OrdinalIgnoreCase));
    }
}
