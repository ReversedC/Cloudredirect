using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Steam &amp; Shell Context Menu Integration Service:
/// Registers seamless right-click context menu actions in Windows File Explorer
/// and Steam client interface for instant Save Export, SteamID64 Re-signing, and Snapshot creation.
/// </summary>
public static class SteamContextMenuService
{
    private const string ShellKeyPath = @"Software\Classes\Directory\shell\CloudRedirect";

    public static bool IsShellContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellKeyPath, false);
            return key != null;
        }
        catch { return false; }
    }

    public static void RegisterShellContextMenu(bool enable = true)
    {
        try
        {
            if (!enable)
            {
                Registry.CurrentUser.DeleteSubKeyTree(ShellKeyPath, false);
                return;
            }

            var exePath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(ShellKeyPath);
            if (key == null) return;

            key.SetValue("", "CloudRedirect Actions");
            key.SetValue("Icon", $"\"{exePath}\",0");
            key.SetValue("SubCommands", "");

            using var shellKey = key.CreateSubKey("Shell");
            if (shellKey != null)
            {
                // Sub-item 1: Export Save (.zip)
                using (var expKey = shellKey.CreateSubKey("1_Export"))
                {
                    expKey.SetValue("", "1-Click Save Export (.zip)");
                    expKey.SetValue("Icon", $"\"{exePath}\",0");
                    using var cmdKey = expKey.CreateSubKey("command");
                    cmdKey.SetValue("", $"\"{exePath}\" --export-save \"%1\"");
                }

                // Sub-item 2: Re-sign SteamID64
                using (var resKey = shellKey.CreateSubKey("2_Resign"))
                {
                    resKey.SetValue("", "SteamID64 Account Re-signer");
                    resKey.SetValue("Icon", $"\"{exePath}\",0");
                    using var cmdKey = resKey.CreateSubKey("command");
                    cmdKey.SetValue("", $"\"{exePath}\" --resign-save \"%1\"");
                }

                // Sub-item 3: Create Instant Snapshot
                using (var snapKey = shellKey.CreateSubKey("3_Snapshot"))
                {
                    snapKey.SetValue("", "Create Save Snapshot");
                    snapKey.SetValue("Icon", $"\"{exePath}\",0");
                    using var cmdKey = snapKey.CreateSubKey("command");
                    cmdKey.SetValue("", $"\"{exePath}\" --create-snapshot \"%1\"");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamContextMenuService] Register failed: {ex.Message}");
        }
    }
}
