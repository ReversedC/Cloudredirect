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
    private const string GlobalSingleInstanceMutexName = @"Global\CloudRedirect_SingleInstance_Mutex_99214";
    private const string ShowWindowEventName = @"Local\CloudRedirect_ShowMainWindow_Event_99214";
    private const string GlobalShowWindowEventName = @"Global\CloudRedirect_ShowMainWindow_Event_99214";

    private static IntPtr _singleInstanceMutexHandle = IntPtr.Zero;
    private static IntPtr _showWindowEventHandle = IntPtr.Zero;
    private static Thread? _eventWaitThread;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string StringSecurityDescriptor,
        uint StringSDRevision,
        out IntPtr SecurityDescriptor,
        IntPtr SecurityDescriptorSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateEvent(
        ref SECURITY_ATTRIBUTES lpEventAttributes,
        bool bManualReset,
        bool bInitialState,
        string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateMutex(
        ref SECURITY_ATTRIBUTES lpMutexAttributes,
        bool bInitialOwner,
        string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseMutex(IntPtr hMutex);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

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
    public static bool LaunchedFromProtocol { get; private set; }

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
            Services.MillenniumPluginService.DeployPlugin();
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

        // 1. If launched via Auto-Update, wait for the previous instance PID to finish terminating
        int updateFromPid = 0;
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--update-from-pid=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(arg.Substring("--update-from-pid=".Length), out updateFromPid);
            }
            else if (arg.Equals("--update-from-pid", StringComparison.OrdinalIgnoreCase))
            {
                int idx = Array.IndexOf(e.Args, arg);
                if (idx >= 0 && idx + 1 < e.Args.Length)
                {
                    int.TryParse(e.Args[idx + 1], out updateFromPid);
                }
            }
        }

        if (updateFromPid > 0)
        {
            LogStartup($"Launched from Auto-Update. Waiting for previous instance PID {updateFromPid} to exit...");
            try
            {
                var oldProc = Process.GetProcessById(updateFromPid);
                if (!oldProc.WaitForExit(4000))
                {
                    try { oldProc.Kill(); oldProc.WaitForExit(1000); } catch { }
                }
            }
            catch { }
            Thread.Sleep(100);
        }

        // 2. 100% Admin/User Cross-Compatible Single-Instance Mutex (World DACL)
        bool isNewInstance = true;
        try
        {
            var sa = new SECURITY_ATTRIBUTES();
            sa.nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES));
            sa.bInheritHandle = false;

            if (ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;WD)", 1, out IntPtr pSec, IntPtr.Zero))
            {
                sa.lpSecurityDescriptor = pSec;
            }

            IntPtr hMutex = CreateMutex(ref sa, true, GlobalSingleInstanceMutexName);
            int lastErr = Marshal.GetLastWin32Error();
            if (hMutex == IntPtr.Zero)
            {
                hMutex = CreateMutex(ref sa, true, SingleInstanceMutexName);
                lastErr = Marshal.GetLastWin32Error();
            }

            if (hMutex != IntPtr.Zero)
            {
                _singleInstanceMutexHandle = hMutex;
                isNewInstance = (lastErr != 183); // ERROR_ALREADY_EXISTS = 183
            }
        }
        catch (Exception ex)
        {
            LogStartup("SingleInstanceMutex error: " + ex.Message);
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
                if (EventWaitHandle.TryOpenExisting(GlobalShowWindowEventName, out var showEvent) ||
                    EventWaitHandle.TryOpenExisting(ShowWindowEventName, out showEvent))
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
                Environment.Exit(0);
                return;
            }

            // Otherwise, the mutex was likely abandoned by a terminated or dead process.
            // Continue starting up as the active instance!
        }

        // We are the primary instance. Create the event wait handle for secondary instance signals with World DACL
        try
        {
            SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
            sa.nLength = Marshal.SizeOf(sa);
            sa.bInheritHandle = false;
            IntPtr pSd = IntPtr.Zero;
            if (ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;WD)", 1, out pSd, IntPtr.Zero))
            {
                sa.lpSecurityDescriptor = pSd;
            }

            _showWindowEventHandle = CreateEvent(ref sa, false, false, GlobalShowWindowEventName);
            if (_showWindowEventHandle == IntPtr.Zero)
            {
                _showWindowEventHandle = CreateEvent(ref sa, false, false, ShowWindowEventName);
            }

            if (_showWindowEventHandle != IntPtr.Zero)
            {
                _eventWaitThread = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            uint res = WaitForSingleObject(_showWindowEventHandle, INFINITE);
                            if (res == WAIT_OBJECT_0)
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
                                            else if (cmd.Contains("minibrowser", StringComparison.OrdinalIgnoreCase) ||
                                                     cmd.Contains("guide", StringComparison.OrdinalIgnoreCase) ||
                                                     cmd.Contains("patchwiki", StringComparison.OrdinalIgnoreCase))
                                            {
                                                var targetUrl = Services.PatchWikiService.ExtractUrlFromProtocolCommand(cmd);
                                                CloudRedirect.Windows.MiniBrowserWindow.Open(targetUrl);
                                            }
                                        }
                                    }
                                    catch { }
                                    BringToForeground();
                                }));
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (ThreadAbortException)
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
        LaunchedFromProtocol = e.Args.Any(a => a.StartsWith("cloudredirect://", StringComparison.OrdinalIgnoreCase));

        Services.LanguageService.ApplyLanguage(Services.LanguageService.ReadLanguagePreference(), save: false);
        Services.WebView2Helper.EnsureLoaderConfigured();
        Services.PatchWikiService.Instance.Initialize();

        var miniBrowserCmd = e.Args.FirstOrDefault(a => a.StartsWith("cloudredirect://minibrowser", StringComparison.OrdinalIgnoreCase) ||
                                                        a.StartsWith("cloudredirect://guide", StringComparison.OrdinalIgnoreCase) ||
                                                        a.StartsWith("cloudredirect://patchwiki", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(miniBrowserCmd))
        {
            var targetUrl = Services.PatchWikiService.ExtractUrlFromProtocolCommand(miniBrowserCmd);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CloudRedirect.Windows.MiniBrowserWindow.Open(targetUrl);
            }));
        }

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
            if (_showWindowEventHandle != IntPtr.Zero)
            {
                CloseHandle(_showWindowEventHandle);
                _showWindowEventHandle = IntPtr.Zero;
            }
            if (_singleInstanceMutexHandle != IntPtr.Zero)
            {
                ReleaseMutex(_singleInstanceMutexHandle);
                CloseHandle(_singleInstanceMutexHandle);
                _singleInstanceMutexHandle = IntPtr.Zero;
            }
        }
        catch { }
        base.OnExit(e);
    }
}
