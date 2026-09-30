using System;
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
            var uriArg = e.Args.FirstOrDefault(a => a.StartsWith("cloudredirect://", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(uriArg))
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "ipc_command.txt"), uriArg);
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
                                        if (cmd.Contains("backup", StringComparison.OrdinalIgnoreCase))
                                        {
                                            _ = Services.UniversalCloudSyncService.SyncAllProfilesAsync();
                                        }
                                        else if (cmd.Contains("saves", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (Current.MainWindow is MainWindow mw)
                                            {
                                                mw.NavigateTo(typeof(Pages.UniversalSavesPage));
                                            }
                                        }
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
}
