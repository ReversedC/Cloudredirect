using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Appearance;

namespace CloudRedirect;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\CloudRedirect_SingleInstance_Mutex_99214";
    private const string ShowWindowEventName = @"Local\CloudRedirect_ShowMainWindow_Event_99214";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showWindowEvent;
    private static Thread? _eventWaitThread;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private const int SW_RESTORE = 9;

    public static bool StartMinimized { get; private set; }
    public static string? PendingStartupCommand { get; set; }

    public static void LogStartup(string msg)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        LogStartup("OnStartup started. Process: " + Environment.ProcessPath + " Args: " + string.Join(" ", e.Args));

        // CLI flags for Steam GUI / Millennium integration
        if (e.Args.Any(a => string.Equals(a, "--patch-steam", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a, "--refresh-steam", StringComparison.OrdinalIgnoreCase)))
        {
            var steamPath = Services.SteamDetector.FindSteamPath();
            var (success, msg) = Services.SteamWebUiPatcher.ApplyPatch(steamPath);
            Console.WriteLine(msg);
            Environment.Exit(success ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--unpatch-steam", StringComparison.OrdinalIgnoreCase)))
        {
            var steamPath = Services.SteamDetector.FindSteamPath();
            var (success, msg) = Services.SteamWebUiPatcher.RemovePatch(steamPath);
            Console.WriteLine(msg);
            Environment.Exit(success ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--test-status", StringComparison.OrdinalIgnoreCase)))
        {
            var steamPath = Services.SteamDetector.FindSteamPath();
            var steamDetails = Services.SteamDetector.GetSteamVersionDetails(steamPath);
            var suoInfo = Services.SuoDetector.Detect();
            var statusStr = $"Steam: {steamDetails.DisplayVersion} [{steamDetails.Branch}] (Pkg: {steamDetails.PackageVersion}, ClientDate: {steamDetails.ClientBuildDateStr}, WebDate: {steamDetails.WebBuildDateStr}, API: {steamDetails.ApiVersion}) | SUO: {suoInfo.Version} [{(suoInfo.IsOnline ? "Online" : "Offline")}]";
            LogStartup("CLI Test Status: " + statusStr);
            Console.WriteLine(statusStr);
            Environment.Exit(0);
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--install-millennium", StringComparison.OrdinalIgnoreCase)))
        {
            var success = Services.MillenniumPluginService.DeployPlugin();
            Console.WriteLine(success ? "Millennium plugin installed successfully." : "Failed to install Millennium plugin.");
            Environment.Exit(success ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--uninstall-millennium", StringComparison.OrdinalIgnoreCase)))
        {
            var success = Services.MillenniumPluginService.RemovePlugin();
            Console.WriteLine(success ? "Millennium plugin removed." : "Failed to remove Millennium plugin.");
            Environment.Exit(success ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--sync-millennium", StringComparison.OrdinalIgnoreCase)))
        {
            Services.MillenniumPluginService.SyncWithSettings();
            Console.WriteLine("Millennium plugin synchronized with settings.");
            Environment.Exit(0);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect", "crash.log");
                File.AppendAllText(logPath, $"[{DateTime.Now}] AppDomain UnhandledException:\n{args.ExceptionObject}\n\n");
                LogStartup("AppDomain UnhandledException: " + args.ExceptionObject);
            }
            catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect", "crash.log");
                File.AppendAllText(logPath, $"[{DateTime.Now}] DispatcherUnhandledException:\n{args.Exception}\n\n");
                LogStartup("DispatcherUnhandledException: " + args.Exception);
            }
            catch { }
            // Prevent non-fatal XAML rendering/binding errors from crashing the whole app
            args.Handled = true;
        };

        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out isNewInstance);
        }
        catch
        {
            isNewInstance = true;
        }

        LogStartup("SingleInstanceMutex isNewInstance=" + isNewInstance);

        if (!isNewInstance)
        {
            // Another instance might already be running.
            // Record any protocol arguments so the running instance can execute them.
            string? commandPayload = null;
            var uriArg = e.Args.FirstOrDefault(a => a.StartsWith("cloudredirect://", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(uriArg)) commandPayload = uriArg;
            else if (e.Args.Length > 0) commandPayload = string.Join(" ", e.Args);

            if (!string.IsNullOrEmpty(commandPayload))
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "ipc_command.txt"), commandPayload);
                }
                catch { }
            }

            // Try signaling the running instance to open / restore from tray and bring to foreground.
            bool signaled = false;
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var showEvent))
                {
                    showEvent.Set();
                    showEvent.Dispose();
                    signaled = true;
                }
            }
            catch { }

            LogStartup("Secondary instance signaling existing instance: signaled=" + signaled);

            // If another instance was actively listening and signaled, exit this instance.
            if (signaled)
            {
                LogStartup("Shutting down secondary instance.");
                try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
                // Do NOT set StartupUri = null (WPF throws ArgumentNullException).
                // Instead, set ShutdownMode so Shutdown() works immediately without
                // needing a MainWindow, and clear StartupUri via the XAML-declared
                // value being overridden by creating no window.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Shutdown(0);
                return;
            }

            // Otherwise, the mutex was likely abandoned by a terminated or dead process.
            // Continue starting up as the active instance!
        }

        // We are the primary instance. Create the event wait handle for secondary instance signals
        try
        {
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
            _eventWaitThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_showWindowEvent.WaitOne())
                        {
                            Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
                                    var ipcFile = Path.Combine(dir, "ipc_command.txt");
                                    if (File.Exists(ipcFile))
                                    {
                                        var cmd = File.ReadAllText(ipcFile).Trim();
                                        File.Delete(ipcFile);
                                        ProcessCommand(cmd);
                                    }
                                }
                                catch { }
                                BringToForeground();
                            }));
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "CloudRedirect_SingleInstance_Listener"
            };
            _eventWaitThread.Start();
        }
        catch { }

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i].Equals("--launcher", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                Environment.SetEnvironmentVariable("CLOUDREDIRECT_LAUNCHER_PATH", e.Args[i + 1].Trim('"'));
                break;
            }
            if (e.Args[i].StartsWith("--launcher=", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("CLOUDREDIRECT_LAUNCHER_PATH", e.Args[i].Substring(11).Trim('"'));
                break;
            }
        }

        StartMinimized = e.Args.Any(a => a.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        var actionArg = e.Args.FirstOrDefault(a =>
            a.StartsWith("cloudredirect://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--export-save", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--create-snapshot", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--resign-save", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--tools", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(actionArg))
        {
            PendingStartupCommand = string.Join(" ", e.Args);
        }

        Services.LanguageService.ApplyLanguage(Services.LanguageService.ReadLanguagePreference(), save: false);
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        LogStartup("OnStartup completed. MainWindow: " + (MainWindow != null ? MainWindow.GetType().Name : "null"));
    }

    public static void BringToForeground()
    {
        try
        {
            LogStartup("BringToForeground called.");
            Services.TrayIconService.Instance.RestoreFromTray();

            var win = Current?.MainWindow;
            if (win != null)
            {
                if (!win.IsVisible)
                {
                    win.Show();
                }
                win.ShowInTaskbar = true;
                if (win.WindowState == WindowState.Minimized)
                {
                    win.WindowState = WindowState.Normal;
                }

                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    BringWindowToTop(hwnd);

                    var fgWnd = GetForegroundWindow();
                    uint fgThread = GetWindowThreadProcessId(fgWnd, out _);
                    uint curThread = GetCurrentThreadId();
                    if (fgThread != curThread && fgThread != 0)
                    {
                        AttachThreadInput(curThread, fgThread, true);
                        SetForegroundWindow(hwnd);
                        AttachThreadInput(curThread, fgThread, false);
                    }
                    else
                    {
                        SetForegroundWindow(hwnd);
                    }
                }

                win.Activate();
                win.Topmost = true;
                win.Topmost = false;
                win.Focus();
                LogStartup("BringToForeground finished successfully.");
            }
            else
            {
                LogStartup("BringToForeground: Current.MainWindow is null!");
            }
        }
        catch (Exception ex)
        {
            LogStartup("BringToForeground exception: " + ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogStartup($"OnExit called (ExitCode: {e.ApplicationExitCode}). StackTrace:\n{Environment.StackTrace}");
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect", "app_exit.log");
            File.AppendAllText(logPath, $"[{DateTime.Now}] OnExit called (ExitCode: {e.ApplicationExitCode}). StackTrace:\n{Environment.StackTrace}\n\n");
        }
        catch { }

        try
        {
            _showWindowEvent?.Dispose();
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
        catch { }
        base.OnExit(e);
    }

    public static void ProcessCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        Current?.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                LogStartup($"Processing command: {command}");

                string action = "";
                string targetPath = "";
                string appIdStr = "";
                string gameName = "";

                if (command.StartsWith("cloudredirect://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri = new Uri(command.Trim());
                        var host = uri.Host.ToLowerInvariant();
                        if (host == "open") { BringToForeground(); return; }
                        if (host == "backup")
                        {
                            _ = Services.UniversalCloudSyncService.SyncAllProfilesAsync();
                            BringToForeground();
                            return;
                        }
                        if (host == "saves")
                        {
                            if (Current.MainWindow is MainWindow mwS)
                            {
                                mwS.NavigateTo(typeof(Pages.UniversalSavesPage));
                            }
                            BringToForeground();
                            return;
                        }

                        var query = uri.Query.TrimStart('?');
                        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
                        var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in parts)
                        {
                            var kv = p.Split('=', 2);
                            if (kv.Length == 2)
                                dict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                            else if (kv.Length == 1)
                                dict[Uri.UnescapeDataString(kv[0])] = "";
                        }

                        dict.TryGetValue("cmd", out action);
                        if (string.IsNullOrEmpty(action)) action = host;
                        dict.TryGetValue("appid", out appIdStr);
                        dict.TryGetValue("name", out gameName);
                        dict.TryGetValue("path", out targetPath);
                    }
                    catch (Exception ex)
                    {
                        LogStartup($"Error parsing uri: {ex.Message}");
                    }
                }
                else
                {
                    if (command.Contains("--export-save", StringComparison.OrdinalIgnoreCase))
                    {
                        action = "export-save";
                        targetPath = ExtractArg(command, "--export-save");
                    }
                    else if (command.Contains("--create-snapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        action = "create-snapshot";
                        targetPath = ExtractArg(command, "--create-snapshot");
                    }
                    else if (command.Contains("--resign-save", StringComparison.OrdinalIgnoreCase))
                    {
                        action = "resign-save";
                        targetPath = ExtractArg(command, "--resign-save");
                    }
                    else if (command.Contains("--tools", StringComparison.OrdinalIgnoreCase) || command.Contains("beta", StringComparison.OrdinalIgnoreCase))
                    {
                        action = "tools";
                    }
                }

                uint.TryParse(appIdStr, out var parsedAppId);

                // If targetPath is not provided or doesn't exist, resolve from appId / gameName
                if (string.IsNullOrEmpty(targetPath) || (!Directory.Exists(targetPath) && !File.Exists(targetPath)))
                {
                    var resolved = ResolveSavePath(parsedAppId, gameName);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        targetPath = resolved;
                    }
                }

                switch (action.ToLowerInvariant())
                {
                    case "export-save":
                        if (!string.IsNullOrEmpty(targetPath) && (Directory.Exists(targetPath) || File.Exists(targetPath)))
                        {
                            var folder = File.Exists(targetPath) ? Path.GetDirectoryName(targetPath)! : targetPath;
                            var gName = !string.IsNullOrEmpty(gameName) ? gameName : Path.GetFileName(folder.TrimEnd('\\', '/'));
                            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                            var zipName = $"{SanitizeFileName(gName)}_SaveBackup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                            var destZip = Path.Combine(desktop, zipName);

                            var (ok, err, outPath) = await Services.SaveArchiveService.ExportSaveArchiveAsync(
                                gName, folder, destZip, gName, parsedAppId);
                            if (ok && outPath != null)
                            {
                                Services.TrayIconService.Instance.ShowNotification(
                                    "Save Export Archive Created",
                                    $"Backed up {gName} to Desktop: {zipName}");
                                try { Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
                            }
                            else
                            {
                                Services.TrayIconService.Instance.ShowNotification(
                                    "Save Export Error",
                                    err ?? "Failed to export save archive.");
                            }
                        }
                        else
                        {
                            if (Current.MainWindow is MainWindow mwExp)
                            {
                                mwExp.NavigateTo(typeof(Pages.BetaToolsPage));
                            }
                            Services.TrayIconService.Instance.ShowNotification(
                                "1-Click Save Export",
                                "Select a save directory in Beta Tools to export.");
                        }
                        break;

                    case "create-snapshot":
                        if (!string.IsNullOrEmpty(targetPath) && (Directory.Exists(targetPath) || File.Exists(targetPath)))
                        {
                            var folder = File.Exists(targetPath) ? Path.GetDirectoryName(targetPath)! : targetPath;
                            var gName = !string.IsNullOrEmpty(gameName) ? gameName : Path.GetFileName(folder.TrimEnd('\\', '/'));
                            var snap = Services.SaveHistoryManager.CreateSnapshot(gName, folder, "Context Menu Instant Snapshot");
                            if (snap != null)
                            {
                                Services.TrayIconService.Instance.ShowNotification(
                                    "Save Snapshot Created",
                                    $"Backed up {gName} ({snap.FormattedSize})");
                            }
                        }
                        else
                        {
                            if (Current.MainWindow is MainWindow mwSnap)
                            {
                                mwSnap.NavigateTo(typeof(Pages.BetaToolsPage));
                            }
                        }
                        break;

                    case "character-slots":
                    case "character-switch":
                        if (Current.MainWindow is MainWindow mwSlots)
                        {
                            mwSlots.NavigateTo(typeof(Pages.BetaToolsPage));
                        }
                        break;

                    case "resign-save":
                        if (Current.MainWindow is MainWindow mwResign)
                        {
                            mwResign.NavigateTo(typeof(Pages.BetaToolsPage));
                        }
                        break;

                    case "open-save-dir":
                        if (!string.IsNullOrEmpty(targetPath) && Directory.Exists(targetPath))
                        {
                            try { Process.Start("explorer.exe", targetPath); } catch { }
                        }
                        else
                        {
                            Services.TrayIconService.Instance.ShowNotification("Save Folder", "Save directory not found or not yet enrolled.");
                            if (Current.MainWindow is MainWindow mwDir)
                            {
                                mwDir.NavigateTo(typeof(Pages.BetaToolsPage));
                            }
                        }
                        break;

                    case "tools":
                    case "beta":
                    default:
                        if (Current.MainWindow is MainWindow mwTools)
                        {
                            mwTools.NavigateTo(typeof(Pages.BetaToolsPage));
                        }
                        break;
                }

                BringToForeground();
            }
            catch (Exception ex)
            {
                LogStartup($"ProcessCommand exception: {ex}");
            }
        }));
    }

    private static string ExtractArg(string cmd, string key)
    {
        var idx = cmd.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var sub = cmd.Substring(idx + key.Length).Trim();
        if (sub.StartsWith("\""))
        {
            var end = sub.IndexOf('"', 1);
            return end > 0 ? sub.Substring(1, end - 1) : sub.Trim('"');
        }
        var space = sub.IndexOf(' ');
        return space > 0 ? sub.Substring(0, space) : sub;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    private static string? ResolveSavePath(uint appId, string? gameName)
    {
        try
        {
            if (Services.UniversalSaveWatcherService.Profiles != null)
            {
                var profile = Services.UniversalSaveWatcherService.Profiles.FirstOrDefault(p =>
                    (appId > 0 && p.SteamAppId == appId) ||
                    (!string.IsNullOrWhiteSpace(gameName) && p.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)));
                if (profile != null && !string.IsNullOrWhiteSpace(profile.SaveFolderPath) && Directory.Exists(profile.SaveFolderPath))
                {
                    return profile.SaveFolderPath;
                }
            }

            var active = Services.ActiveGameTrackerService.CurrentActiveGame;
            if (active != null)
            {
                if ((appId > 0 && active.AppId == appId) ||
                    (!string.IsNullOrWhiteSpace(gameName) && active.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrWhiteSpace(active.SaveDirectory) && Directory.Exists(active.SaveDirectory))
                        return active.SaveDirectory;
                }
            }

            if (!string.IsNullOrWhiteSpace(gameName) || appId > 0)
            {
                var autoDetected = Services.GameSaveAutoDetector.DetectSaveFolder(gameName ?? "", null, appId);
                if (!string.IsNullOrWhiteSpace(autoDetected) && Directory.Exists(autoDetected))
                    return autoDetected;
            }

            if (appId > 0)
            {
                var steamPath = Services.SteamDetector.FindSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    var uData = Path.Combine(steamPath, "userdata");
                    if (Directory.Exists(uData))
                    {
                        foreach (var userDir in Directory.GetDirectories(uData))
                        {
                            var appSave = Path.Combine(userDir, appId.ToString(), "remote");
                            if (Directory.Exists(appSave))
                                return appSave;
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }
}
