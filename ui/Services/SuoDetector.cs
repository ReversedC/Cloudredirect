using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Detects Steam Unlock ONENNABE (SUO) installation, version, and live online/offline process status.
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
    /// and live online/offline status via running process and local backend port (49377).
    /// </summary>
    public static SuoStatus Detect()
    {
        string? version = null;
        string? installPath = null;
        bool isInstalled = false;

        // 1. Registry lookup in Windows Uninstall keys
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

        string displayVersion = isInstalled
            ? (!string.IsNullOrWhiteSpace(version) ? $"v{version}" : "Detected")
            : "Not Installed";

        // 3. Check live Online/Offline status
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
            else if (isInstalled)
            {
                // Test if backend port 49377 is active
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync("127.0.0.1", 49377);
                if (Task.WhenAny(connectTask, Task.Delay(400)).Result == connectTask && tcp.Connected)
                {
                    isOnline = true;
                }
            }
        }
        catch { }

        return new SuoStatus(isInstalled, displayVersion, isOnline, processId, installPath);
    }
}
