using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record ScannedSteamGame(
    uint AppId,
    string Name,
    string InstallDir,
    bool IsLuaGame,
    bool HasSteamCloud,
    bool IsGenuineWithoutCloud,
    string? DetectedSavePath,
    bool AutoEnrolled
);

public record SteamScanSummary(
    int TotalInstalledGames,
    int LuaGamesCount,
    int GenuineCloudGamesCount,
    int NonCloudGamesCount,
    int NewlyEnrolledCount,
    List<ScannedSteamGame> Games
);

/// <summary>
/// Scans all installed Steam games across all library folders,
/// classifies them (Lua game vs Genuine with Cloud vs Genuine without Cloud),
/// and automatically protects non-cloud games via Universal Cloud Saves.
/// </summary>
public static class SteamGameScannerService
{
    public static readonly HashSet<uint> IgnoredAppIds = new()
    {
        228980,  // Steamworks Common Redistributables
        1070560, // Steam Linux Runtime
        1391110, // Steam Linux Runtime - Soldier
        1628350, // Steam Linux Runtime - Sniper
        894820,  // SteamVR
        250820,  // SteamVR
        105600,  // Terraria dedicated server
    };

    public static async Task<SteamScanSummary> ScanInstalledSteamGamesAsync(bool autoEnroll = true)
    {
        return await Task.Run(() =>
        {
            var steamPath = SteamDetector.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            {
                return new SteamScanSummary(0, 0, 0, 0, 0, new List<ScannedSteamGame>());
            }

            var libraryPaths = GetLibraryPaths(steamPath);
            var scannedGames = new List<ScannedSteamGame>();
            int luaCount = 0;
            int genuineCloudCount = 0;
            int nonCloudCount = 0;
            int newlyEnrolled = 0;

            foreach (var libPath in libraryPaths)
            {
                var steamAppsDir = Path.Combine(libPath, "steamapps");
                if (!Directory.Exists(steamAppsDir)) continue;

                foreach (var manifestPath in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
                {
                    try
                    {
                        var (appId, name, installDirName) = ParseManifest(manifestPath);
                        if (appId == 0 || IgnoredAppIds.Contains(appId)) continue;
                        if (string.IsNullOrWhiteSpace(name))
                            name = $"Steam App {appId}";

                        // Skip typical non-game tool names
                        if (name.Contains("Steamworks", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Soundtrack", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Dedicated Server", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fullInstallDir = Path.Combine(steamAppsDir, "common", installDirName);

                        bool isLua = SteamDetector.IsLuaGame(appId, steamPath);
                        bool isZeroLua = SteamDetector.IsInterceptApp(appId, steamPath);
                        bool isIntercepted = isLua || isZeroLua;
                        bool hasCloud = AppInfoParser.HasCloudSave(appId, steamPath);
                        bool hasCloudDenied = SteamDetector.HasCloudAccessDenied(appId, steamPath);
                        bool isSuoGame = SuoDetector.IsAppInCatalog(appId);
                        bool isRemoteUnlocked = SteamDetector.IsRemoteUnlockedApp(appId, steamPath);
                        bool hasAppIdTxt = File.Exists(Path.Combine(fullInstallDir, "steam_appid.txt"));

                        bool isUnlockedNoLua = !isIntercepted && (hasCloudDenied || isSuoGame || isRemoteUnlocked || hasAppIdTxt);
                        bool isGenuineWithCloud = !isIntercepted && !isUnlockedNoLua && hasCloud;
                        bool isGenuineNoCloud = !isIntercepted && !isUnlockedNoLua && !hasCloud;

                        // Auto-enroll non-genuine games into Zero-Lua interception and fix syncstate
                        if (isUnlockedNoLua)
                        {
                            SteamDetector.AddInterceptApp(appId, steamPath);
                            if (hasCloudDenied)
                            {
                                SteamDetector.FixRemoteCacheSyncState(appId, steamPath);
                            }
                        }

                        if (isIntercepted) luaCount++;
                        else if (isGenuineWithCloud) genuineCloudCount++;
                        else nonCloudCount++;

                        string? detectedSave = null;
                        bool enrolled = false;

                        // If genuine without cloud, or unlocked without Lua, or intercepted without cloud: auto-detect and protect
                        if (isGenuineNoCloud || isUnlockedNoLua || (isIntercepted && !hasCloud))
                        {
                            detectedSave = GameSaveAutoDetector.DetectSaveFolder(name, null, appId);
                            if (!string.IsNullOrEmpty(detectedSave) && Directory.Exists(detectedSave))
                            {
                                if (autoEnroll)
                                {
                                    var existing = UniversalSaveWatcherService.FindProfile(appId, null, name);
                                    if (existing == null)
                                    {
                                        UniversalSaveWatcherService.AutoEnrollIfNeeded(
                                            name, null, appId, detectedSave, isGenuine: !isLua);
                                        enrolled = true;
                                        newlyEnrolled++;
                                    }
                                    else
                                    {
                                        enrolled = true;
                                    }
                                }
                            }
                        }

                        scannedGames.Add(new ScannedSteamGame(
                            appId,
                            name,
                            fullInstallDir,
                            isLua,
                            hasCloud,
                            isGenuineNoCloud,
                            detectedSave,
                            enrolled
                        ));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse {manifestPath}: {ex.Message}");
                    }
                }
            }

            return new SteamScanSummary(
                scannedGames.Count,
                luaCount,
                genuineCloudCount,
                nonCloudCount,
                newlyEnrolled,
                scannedGames
            );
        });
    }

    public static (uint AppId, string Name, string InstallDir) ParseManifest(string path)
    {
        uint appId = 0;
        string name = "";
        string installDir = "";

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("\"appid\"", StringComparison.OrdinalIgnoreCase))
            {
                var val = ExtractQuotedValue(trimmed, "\"appid\"");
                uint.TryParse(val, out appId);
            }
            else if (trimmed.StartsWith("\"name\"", StringComparison.OrdinalIgnoreCase))
            {
                name = ExtractQuotedValue(trimmed, "\"name\"") ?? "";
            }
            else if (trimmed.StartsWith("\"installdir\"", StringComparison.OrdinalIgnoreCase))
            {
                installDir = ExtractQuotedValue(trimmed, "\"installdir\"") ?? "";
            }

            if (appId > 0 && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(installDir))
                break;
        }

        return (appId, name, installDir);
    }

    private static string? ExtractQuotedValue(string line, string key)
    {
        int keyIndex = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0) return null;

        int afterKey = keyIndex + key.Length;
        int firstQuote = line.IndexOf('"', afterKey);
        if (firstQuote < 0) return null;

        int secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0) return null;

        return line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    public static List<string> GetLibraryPaths(string steamPath)
    {
        var paths = new List<string> { steamPath };
        try
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                var regex = new Regex(@"\""path\""\s+\""([^\""]+)\""", RegexOptions.IgnoreCase);
                foreach (var line in File.ReadAllLines(vdfPath))
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        var p = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(p) && !paths.Any(existing => existing.Equals(p, StringComparison.OrdinalIgnoreCase)))
                        {
                            paths.Add(p);
                        }
                    }
                }
            }
        }
        catch { }

        return paths;
    }
}
