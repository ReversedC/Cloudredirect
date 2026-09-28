using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Detects Steam Unlock ONENNABE (SUO) installation, version, and live online/offline process status.
/// Accurately detects auto-updated SUO versions (e.g. 1.4.7) from running single-file extracts,
/// window titles, or Windows uninstall registry entries.
/// </summary>
public static class SuoDetector
{
    public sealed record SuoStatus(
        bool IsInstalled,
        string Version,
        bool IsOnline,
        int? ProcessId,
        string? InstallPath);

    /// <summary>
    /// Asynchronously detects SUO status without blocking the UI thread.
    /// </summary>
    public static async Task<SuoStatus> DetectAsync()
    {
        return await Task.Run(Detect);
    }

    /// <summary>
    /// Synchronously detects SUO installation, version from Windows uninstall registry or directory,
    /// and live online/offline status via running process and local backend port.
    /// </summary>
    public static SuoStatus Detect()
    {
        string? version = null;
        string? installPath = null;
        bool isInstalled = false;

        // 1. Registry lookup in Windows Uninstall keys (finds initial install location and version)
        try
        {
            string[] subkeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var sub in subkeys)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(sub);
                if (baseKey == null) continue;
                foreach (var name in baseKey.GetSubKeyNames())
                {
                    using var appKey = baseKey.OpenSubKey(name);
                    if (appKey == null) continue;
                    var displayName = appKey.GetValue("DisplayName") as string;
                    if (!string.IsNullOrEmpty(displayName) &&
                        (displayName.Contains("ONENNABE", StringComparison.OrdinalIgnoreCase) ||
                         displayName.Contains("Steam Unlock", StringComparison.OrdinalIgnoreCase)))
                    {
                        version = appKey.GetValue("DisplayVersion") as string;
                        installPath = appKey.GetValue("InstallLocation") as string;
                        isInstalled = true;
                        break;
                    }
                }
                if (isInstalled) break;
            }

            if (!isInstalled)
            {
                using var userKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (userKey != null)
                {
                    foreach (var name in userKey.GetSubKeyNames())
                    {
                        using var appKey = userKey.OpenSubKey(name);
                        if (appKey == null) continue;
                        var displayName = appKey.GetValue("DisplayName") as string;
                        if (!string.IsNullOrEmpty(displayName) &&
                            (displayName.Contains("ONENNABE", StringComparison.OrdinalIgnoreCase) ||
                             displayName.Contains("Steam Unlock", StringComparison.OrdinalIgnoreCase)))
                        {
                            version = appKey.GetValue("DisplayVersion") as string;
                            installPath = appKey.GetValue("InstallLocation") as string;
                            isInstalled = true;
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Filesystem fallback if registry lookup did not find installPath
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
        {
            string[] candidatePaths =
            {
                @"C:\Program Files\Steam Unlock ONENNABE",
                @"C:\Program Files (x86)\Steam Unlock ONENNABE"
            };
            foreach (var cp in candidatePaths)
            {
                if (Directory.Exists(cp) && (File.Exists(Path.Combine(cp, "Steam_Unlock_ONENNABE.exe")) || File.Exists(Path.Combine(cp, "updater.exe"))))
                {
                    installPath = cp;
                    isInstalled = true;
                    break;
                }
            }
        }

        // 3. Detect updated version (SUO's updater.exe updates the binary in place without rewriting Windows registry).
        // Check running process MainWindowTitle or extracted single-file bundle in %TEMP%\onefile_*
        try
        {
            // A. Check running SUO window title (e.g. "Steam Unlock Onennabe 1.4.7")
            var procs = Process.GetProcessesByName("Steam_Unlock_ONENNABE");
            foreach (var p in procs)
            {
                try
                {
                    var title = p.MainWindowTitle;
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        var m = Regex.Match(title, @"(?:Steam\s+Unlock\s+Onennabe\s+)?(\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            version = m.Groups[1].Value;
                            isInstalled = true;
                            break;
                        }
                    }
                }
                catch { }
            }

            // B. If not detected from window title, inspect newest extracted single-file dll in %TEMP%\onefile_*
            var tempDir = Path.GetTempPath();
            if (Directory.Exists(tempDir))
            {
                var onefileDirs = Directory.GetDirectories(tempDir, "onefile_*")
                    .OrderByDescending(Directory.GetLastWriteTimeUtc);

                foreach (var dir in onefileDirs.Take(5))
                {
                    var dllPath = Path.Combine(dir, "Steam_Unlock_ONENNABE.dll");
                    if (File.Exists(dllPath))
                    {
                        var extractedVer = TryExtractVersionFromDll(dllPath);
                        if (!string.IsNullOrEmpty(extractedVer))
                        {
                            version = extractedVer;
                            isInstalled = true;
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        // 4. Check live Online/Offline status
        bool isOnline = false;
        int? processId = null;

        try
        {
            var procs = Process.GetProcessesByName("Steam_Unlock_ONENNABE");
            if (procs.Length > 0)
            {
                isOnline = true;
                processId = procs[0].Id;
                foreach (var p in procs) p.Dispose();
            }

            // Check dynamic port allocated by SUO daemon (saved in %LOCALAPPDATA%\OnennabeCloudSaves\onennabe.port)
            int targetPort = 49377;
            try
            {
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var portFile = Path.Combine(localApp, "OnennabeCloudSaves", "onennabe.port");
                if (File.Exists(portFile))
                {
                    var content = File.ReadAllText(portFile);
                    var mPort = Regex.Match(content, @"""port""\s*:\s*(\d+)");
                    if (mPort.Success && int.TryParse(mPort.Groups[1].Value, out var parsedPort))
                    {
                        targetPort = parsedPort;
                    }
                }
            }
            catch { }

            if (!isOnline && isInstalled)
            {
                // Test if backend port is active
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync("127.0.0.1", targetPort);
                if (Task.WhenAny(connectTask, Task.Delay(400)).Result == connectTask && tcp.Connected)
                {
                    isOnline = true;
                }
            }
        }
        catch { }

        string displayVersion = isInstalled
            ? (!string.IsNullOrWhiteSpace(version) ? $"v{version}" : "Detected")
            : "Not Installed";

        return new SuoStatus(isInstalled, displayVersion, isOnline, processId, installPath);
    }

    /// <summary>
    /// Scans the binary bytes of Steam_Unlock_ONENNABE.dll for version tokens (e.g. u1.4.7\0uworkshop).
    /// </summary>
    private static string? TryExtractVersionFromDll(string dllPath)
    {
        try
        {
            using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long fileLength = fs.Length;
            long seekOffset = Math.Max(0, fileLength - 15 * 1024 * 1024);
            fs.Seek(seekOffset, SeekOrigin.Begin);

            int toRead = (int)(fileLength - seekOffset);
            var buffer = new byte[toRead];
            int bytesRead = fs.Read(buffer, 0, toRead);
            var latin1 = Encoding.Latin1.GetString(buffer, 0, bytesRead);

            // Match pattern "u1.4.7\0" or "u1.X.X\0uworkshop"
            var m = Regex.Match(latin1, @"u(\d+\.\d+\.\d+)\x00uworkshop");
            if (m.Success)
            {
                return m.Groups[1].Value;
            }

            // Fallback match near CloudRedirect or workshop
            var m2 = Regex.Match(latin1, @"CloudRedirect\.exe[^\x00]{0,20}\x00[a-z]{0,4}\x00u(\d+\.\d+\.\d+)\x00");
            if (m2.Success)
            {
                return m2.Groups[1].Value;
            }

            // General pattern for version token in SUO binary
            var m3 = Regex.Match(latin1, @"\x00u(\d+\.\d+\.\d+)\x00");
            if (m3.Success)
            {
                return m3.Groups[1].Value;
            }
        }
        catch { }
        return null;
    }

    private static readonly object _catalogLock = new();
    private static System.Collections.Generic.HashSet<uint>? _cachedCatalogAppIds;
    private static DateTime _lastCatalogCheck = DateTime.MinValue;

    /// <summary>
    /// Checks whether an AppID exists in Steam Unlock ONENNABE's local branches_cache.json database.
    /// </summary>
    public static bool IsAppInCatalog(uint appId)
    {
        if (appId == 0) return false;
        var catalog = GetCatalogAppIds();
        return catalog.Contains(appId);
    }

    /// <summary>
    /// Gets all AppIDs registered in SUO's branches_cache.json.
    /// </summary>
    public static System.Collections.Generic.HashSet<uint> GetCatalogAppIds()
    {
        lock (_catalogLock)
        {
            if (_cachedCatalogAppIds != null && (DateTime.UtcNow - _lastCatalogCheck).TotalMinutes < 5)
            {
                return _cachedCatalogAppIds;
            }
        }

        var set = new System.Collections.Generic.HashSet<uint>();
        try
        {
            string[] possiblePaths =
            {
                @"C:\Program Files\Steam Unlock ONENNABE\branches_cache.json",
                @"C:\Program Files (x86)\Steam Unlock ONENNABE\branches_cache.json"
            };

            string? targetPath = null;
            foreach (var p in possiblePaths)
            {
                if (File.Exists(p)) { targetPath = p; break; }
            }

            if (targetPath != null)
            {
                using var fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                string? line;
                var appidRegex = new Regex(@"""appid""\s*:\s*""?(\d+)""?", RegexOptions.Compiled);
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Contains("\"appid\""))
                    {
                        var m = appidRegex.Match(line);
                        if (m.Success && uint.TryParse(m.Groups[1].Value, out var id) && id > 0)
                        {
                            set.Add(id);
                        }
                    }
                }
            }
        }
        catch { }

        lock (_catalogLock)
        {
            _cachedCatalogAppIds = set;
            _lastCatalogCheck = DateTime.UtcNow;
        }

        return set;
    }
}
