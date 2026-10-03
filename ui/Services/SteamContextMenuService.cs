using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Steam &amp; Shell Context Menu Integration Service:
/// Registers seamless right-click context menu actions in Windows File Explorer
/// across Folders, Folder Backgrounds (Empty space / Desktop), Files, and Drives
/// for instant Save Export (.zip), SteamID64 Re-signing, Snapshot creation, and Beta Tools.
/// </summary>
public static class SteamContextMenuService
{
    private static readonly string[] ShellKeyRoots = new[]
    {
        @"Software\Classes\Directory\shell\CloudRedirect",
        @"Software\Classes\Directory\Background\shell\CloudRedirect",
        @"Software\Classes\*\shell\CloudRedirect",
        @"Software\Classes\Drive\shell\CloudRedirect"
    };

    public static bool IsShellContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellKeyRoots[0], false);
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
                foreach (var path in ShellKeyRoots)
                {
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(path, false);
                    }
                    catch { }
                }
                return;
            }

            var exePath = AppUpdater.GetAppExecutablePath() ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            foreach (var rootPath in ShellKeyRoots)
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(rootPath);
                    if (key == null) continue;

                    key.SetValue("", "CloudRedirect Actions");
                    key.SetValue("Icon", $"\"{exePath}\",0");
                    key.SetValue("SubCommands", "");

                    using var shellKey = key.CreateSubKey("Shell");
                    if (shellKey != null)
                    {
                        bool isBackground = rootPath.Contains("Background", StringComparison.OrdinalIgnoreCase);
                        string pathParam = isBackground ? "%V" : "%1";

                        // Sub-item 1: Export Save (.zip)
                        using (var expKey = shellKey.CreateSubKey("1_Export"))
                        {
                            expKey.SetValue("", "1-Click Save Export (.zip)");
                            expKey.SetValue("Icon", $"\"{exePath}\",0");
                            using var cmdKey = expKey.CreateSubKey("command");
                            cmdKey.SetValue("", $"\"{exePath}\" --export-save \"{pathParam}\"");
                        }

                        // Sub-item 2: Re-sign SteamID64
                        using (var resKey = shellKey.CreateSubKey("2_Resign"))
                        {
                            resKey.SetValue("", "SteamID64 Account Re-signer");
                            resKey.SetValue("Icon", $"\"{exePath}\",0");
                            using var cmdKey = resKey.CreateSubKey("command");
                            cmdKey.SetValue("", $"\"{exePath}\" --resign-save \"{pathParam}\"");
                        }

                        // Sub-item 3: Create Instant Snapshot
                        using (var snapKey = shellKey.CreateSubKey("3_Snapshot"))
                        {
                            snapKey.SetValue("", "Create Save Snapshot");
                            snapKey.SetValue("Icon", $"\"{exePath}\",0");
                            using var cmdKey = snapKey.CreateSubKey("command");
                            cmdKey.SetValue("", $"\"{exePath}\" --create-snapshot \"{pathParam}\"");
                        }

                        // Sub-item 4: Open CloudRedirect Beta Tools
                        using (var toolsKey = shellKey.CreateSubKey("4_Tools"))
                        {
                            toolsKey.SetValue("", "Open CloudRedirect Beta Hub");
                            toolsKey.SetValue("Icon", $"\"{exePath}\",0");
                            using var cmdKey = toolsKey.CreateSubKey("command");
                            cmdKey.SetValue("", $"\"{exePath}\" --tools");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SteamContextMenuService] Register root '{rootPath}' failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamContextMenuService] Register failed: {ex.Message}");
        }
    }
}
