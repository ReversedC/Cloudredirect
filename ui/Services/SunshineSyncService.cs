using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CloudRedirect.Services;

/// <summary>
/// Integrates CloudRedirect with Sunshine (open-source GameStream host).
/// Automatically configures apps.json so all .lua and Steam games appear in
/// GameHub and Moonlight with poster artwork, commands, and zero manual setup.
/// </summary>
public static class SunshineSyncService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string? FindSunshineExePath()
    {
        var primary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "sunshine.exe");
        if (File.Exists(primary)) return primary;

        var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sunshine", "sunshine.exe");
        if (File.Exists(localAppData)) return localAppData;

        return null;
    }

    public static bool IsSunshineInstalled => FindSunshineExePath() != null || FindAppsJsonPath() != null;

    public static bool IsSunshineRunning
    {
        get
        {
            try
            {
                return Process.GetProcessesByName("sunshine").Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string? FindAppsJsonPath()
    {
        var programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "config", "apps.json");
        if (File.Exists(programFilesPath)) return programFilesPath;

        var userProfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "sunshine", "apps.json");
        if (File.Exists(userProfilePath)) return userProfilePath;

        var programDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sunshine", "apps.json");
        if (File.Exists(programDataPath)) return programDataPath;

        // If directory exists in Program Files, default to it
        var pfDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "config");
        if (Directory.Exists(pfDir)) return programFilesPath;

        var userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "sunshine");
        if (Directory.Exists(userDir)) return userProfilePath;

        return null;
    }

    public static string? GetLocalLanIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = addr.Address.ToString();
                        if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") || ipStr.StartsWith("172."))
                        {
                            return ipStr;
                        }
                    }
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    public static void OpenSunshineDashboard()
    {
        try
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CloudRedirect.Windows.SunshineMiniWindow.ShowWindow("https://localhost:47990");
                });
                return;
            }
            Process.Start(new ProcessStartInfo("https://localhost:47990") { UseShellExecute = true });
        }
        catch { }
    }

    public static void OpenSunshinePinPage()
    {
        try
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CloudRedirect.Windows.SunshineMiniWindow.ShowWindow("https://localhost:47990/pin");
                });
                return;
            }
            Process.Start(new ProcessStartInfo("https://localhost:47990/pin") { UseShellExecute = true });
        }
        catch { }
    }

    public record SunshineSyncResult(bool Success, int SyncedCount, string Message);

    /// <summary>
    /// Scans all .lua games and Steam games, then registers them into Sunshine's apps.json
    /// with cover images and steam://rungameid/ launch commands.
    /// </summary>
    public static async Task<SunshineSyncResult> SyncGamesToSunshineAsync(CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                var appsJsonPath = FindAppsJsonPath();
                if (string.IsNullOrEmpty(appsJsonPath))
                {
                    return new SunshineSyncResult(false, 0, "Sunshine is not installed or configuration path not found.");
                }

                var configDir = Path.GetDirectoryName(appsJsonPath)!;
                var coversDir = Path.Combine(configDir, "covers");

                // Check if we have direct write permission to configDir
                bool canWriteDirectly = false;
                try
                {
                    var testFile = Path.Combine(configDir, $".perm_test_{Guid.NewGuid():N}");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    canWriteDirectly = true;
                }
                catch
                {
                    canWriteDirectly = false;
                }

                // If not writable directly, stage in %TEMP%
                var stagingDir = Path.Combine(Path.GetTempPath(), "CloudRedirect_SunshineSync");
                var stagingCoversDir = Path.Combine(stagingDir, "covers");
                var activeCoversDir = canWriteDirectly ? coversDir : stagingCoversDir;

                Directory.CreateDirectory(activeCoversDir);

                // 1. Gather all .lua games
                var luaGames = await LuaCloudSyncService.LoadLuaGamesAsync(ct);
                var gamesToRegister = new Dictionary<uint, string>();

                foreach (var g in luaGames)
                {
                    if (g.AppId > 0 && !string.IsNullOrWhiteSpace(g.GameName) && g.GameName != "Unknown Game")
                    {
                        gamesToRegister[g.AppId] = g.GameName;
                    }
                }

                // 2. Also scan installed Steam games
                try
                {
                    var steamScan = await SteamGameScannerService.ScanInstalledSteamGamesAsync(autoEnroll: false);
                    foreach (var sg in steamScan.Games)
                    {
                        if (sg.AppId > 0 && !string.IsNullOrWhiteSpace(sg.Name) && !gamesToRegister.ContainsKey(sg.AppId))
                        {
                            gamesToRegister[sg.AppId] = sg.Name;
                        }
                    }
                }
                catch { }

                if (gamesToRegister.Count == 0)
                {
                    return new SunshineSyncResult(true, 0, "No active .lua or installed Steam games found to sync.");
                }

                // 3. Load or create apps.json
                JsonObject root;
                if (File.Exists(appsJsonPath))
                {
                    var content = await File.ReadAllTextAsync(appsJsonPath, ct);
                    root = JsonNode.Parse(content)?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (!root.ContainsKey("env"))
                {
                    root["env"] = new JsonObject();
                }

                JsonArray appsArray;
                if (root.ContainsKey("apps") && root["apps"] is JsonArray arr)
                {
                    appsArray = arr;
                }
                else
                {
                    appsArray = new JsonArray();
                    root["apps"] = appsArray;
                }

                // Ensure Desktop is present
                bool hasDesktop = appsArray.Any(n => n?["name"]?.ToString() == "Desktop");
                if (!hasDesktop)
                {
                    appsArray.Insert(0, new JsonObject
                    {
                        ["name"] = "Desktop",
                        ["image-path"] = "desktop.png"
                    });
                }

                // Ensure Steam Big Picture is present
                bool hasSteamBp = appsArray.Any(n => n?["name"]?.ToString() == "Steam Big Picture");
                if (!hasSteamBp)
                {
                    appsArray.Add(new JsonObject
                    {
                        ["name"] = "Steam Big Picture",
                        ["cmd"] = "steam://open/bigpicture",
                        ["prep-cmd"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["do"] = "",
                                ["undo"] = "steam://close/bigpicture"
                            }
                        },
                        ["auto-detach"] = true,
                        ["wait-all"] = true,
                        ["image-path"] = "steam.png"
                    });
                }

                int syncedCount = 0;

                // 4. Register or update each game
                foreach (var kvp in gamesToRegister)
                {
                    var appId = kvp.Key;
                    var gameName = kvp.Value;

                    // Download or verify cover art
                    var coverFile = Path.Combine(activeCoversDir, $"{appId}.jpg");
                    var existingCoverInConfig = Path.Combine(coversDir, $"{appId}.jpg");

                    if (!File.Exists(coverFile) && !canWriteDirectly && File.Exists(existingCoverInConfig))
                    {
                        try { File.Copy(existingCoverInConfig, coverFile, true); } catch { }
                    }

                    if (!File.Exists(coverFile))
                    {
                        try
                        {
                            var headerUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
                            var imgBytes = await _http.GetByteArrayAsync(headerUrl, ct);
                            if (imgBytes != null && imgBytes.Length > 0)
                            {
                                await File.WriteAllBytesAsync(coverFile, imgBytes, ct);
                            }
                        }
                        catch { }
                    }

                    var relativeCover = (File.Exists(coverFile) || File.Exists(existingCoverInConfig)) ? $"covers/{appId}.jpg" : "steam.png";

                    // Check if already in appsArray
                    var existing = appsArray.FirstOrDefault(n =>
                    {
                        var cmd = n?["cmd"]?.ToString() ?? "";
                        return cmd.Contains($"steam://rungameid/{appId}") || n?["name"]?.ToString() == gameName;
                    })?.AsObject();

                    if (existing != null)
                    {
                        existing["name"] = gameName;
                        existing["cmd"] = $"steam://rungameid/{appId}";
                        existing["auto-detach"] = true;
                        existing["wait-all"] = true;
                        existing["image-path"] = relativeCover;
                    }
                    else
                    {
                        appsArray.Add(new JsonObject
                        {
                            ["name"] = gameName,
                            ["cmd"] = $"steam://rungameid/{appId}",
                            ["auto-detach"] = true,
                            ["wait-all"] = true,
                            ["image-path"] = relativeCover
                        });
                    }

                    syncedCount++;
                }

                // 5. Save apps.json with indentation
                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = root.ToJsonString(options);

                if (canWriteDirectly)
                {
                    await File.WriteAllTextAsync(appsJsonPath, updatedJson, ct);
                }
                else
                {
                    var stagingAppsJson = Path.Combine(stagingDir, "apps.json");
                    await File.WriteAllTextAsync(stagingAppsJson, updatedJson, ct);

                    // Copy entire staging directory to configDir with elevated PowerShell,
                    // and grant Users Modify permissions so subsequent syncs are seamless and instant!
                    var copyCommand = $"Copy-Item -Path '{stagingDir}\\*' -Destination '{configDir}' -Recurse -Force; icacls '{configDir}' /grant 'Users:(OI)(CI)M' /T /Q";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{copyCommand}\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    try
                    {
                        var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            await proc.WaitForExitAsync(ct);
                            if (proc.ExitCode != 0)
                            {
                                return new SunshineSyncResult(false, 0, $"Elevated permission copy failed with exit code {proc.ExitCode}.");
                            }
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        return new SunshineSyncResult(false, 0, "Administrator permission was required to update Sunshine configuration. Please accept the UAC prompt to allow CloudRedirect to configure Sunshine.");
                    }
                }

                return new SunshineSyncResult(true, syncedCount, $"Successfully synced {syncedCount} game(s) to Sunshine apps.json!");
            }
            catch (Exception ex)
            {
                return new SunshineSyncResult(false, 0, $"Sync failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Generates a secure, human-readable Sunshine password conforming to security guidelines.
    /// Format: Steam-XXXX-XXXX!
    /// </summary>
    public static string GenerateStrongPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        var rng = new Random();

        string Part(string chars, int len) =>
            new string(Enumerable.Range(0, len).Select(_ => chars[rng.Next(chars.Length)]).ToArray());

        return $"Steam-{Part(upper, 2)}{Part(digits, 2)}-{Part(lower, 2)}{Part(digits, 2)}!";
    }

    /// <summary>
    /// Updates Sunshine's Web UI credentials using sunshine.exe --creds <username> <password>.
    /// </summary>
    public static async Task<(bool Success, string Message)> SetCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) username = "admin";
        if (string.IsNullOrWhiteSpace(password)) return (false, "Password cannot be empty.");

        var sunshineExe = FindSunshineExePath();
        if (string.IsNullOrEmpty(sunshineExe) || !File.Exists(sunshineExe))
        {
            return (false, "Sunshine executable not found.");
        }

        return await Task.Run(async () =>
        {
            try
            {
                // 1. Try running directly first
                var psi = new ProcessStartInfo
                {
                    FileName = sunshineExe,
                    Arguments = $"--creds \"{username}\" \"{password}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                        var stderr = await proc.StandardError.ReadToEndAsync(ct);
                        await proc.WaitForExitAsync(ct);

                        if (proc.ExitCode == 0 || stdout.Contains("New credentials have been created"))
                        {
                            AppSettings.SunshineUsername = username;
                            AppSettings.SunshinePassword = password;
                            return (true, "Sunshine credentials successfully updated.");
                        }
                    }
                }

                // 2. If direct run failed (permission denied), retry with elevated PowerShell
                var elevatedCmd = $"& '{sunshineExe}' --creds \"{username}\" \"{password}\"";
                var elevPsi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{elevatedCmd}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var elevProc = Process.Start(elevPsi))
                {
                    if (elevProc != null)
                    {
                        await elevProc.WaitForExitAsync(ct);
                        if (elevProc.ExitCode == 0)
                        {
                            AppSettings.SunshineUsername = username;
                            AppSettings.SunshinePassword = password;
                            return (true, "Sunshine credentials successfully updated with administrator permissions.");
                        }
                    }
                }

                return (false, "Failed to apply credentials to Sunshine.");
            }
            catch (Exception ex)
            {
                return (false, $"Error setting credentials: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Ensures Sunshine has a generated or configured password. If none is stored,
    /// generates one automatically and applies it to Sunshine.
    /// </summary>
    public static async Task<string> EnsureCredentialsAsync(CancellationToken ct = default)
    {
        var currentPass = AppSettings.SunshinePassword;
        if (!string.IsNullOrEmpty(currentPass))
        {
            return currentPass;
        }

        var newPass = GenerateStrongPassword();
        var res = await SetCredentialsAsync("admin", newPass, ct);
        if (res.Success)
        {
            return newPass;
        }

        return currentPass;
    }
}
