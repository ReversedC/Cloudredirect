using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace CloudRedirect.Services;

/// <summary>
/// Manages the Windows System Tray (notification area) icon, Steam-styled context menu,
/// and minimize/restore transitions using native Win32 Shell_NotifyIcon without WinForms dependencies.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static TrayIconService? _instance;
    public static TrayIconService Instance => _instance ??= new TrayIconService();

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 2048;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int GCLP_HICONSM = -34;
    private const int GCLP_HICON = -14;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;
    private const int NIIF_INFO = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern IntPtr GetClassLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size > 4 ? GetClassLongPtr64(hWnd, nIndex) : GetClassLongPtr32(hWnd, nIndex);
    }

    private MainWindow? _mainWindow;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _ownsIcon;
    private bool _isCreated;
    private ContextMenu? _contextMenu;
    private bool _hasShownBalloon;

    public void Initialize(MainWindow mainWindow)
    {
        if (_isCreated) return;
        _mainWindow = mainWindow;

        var helper = new WindowInteropHelper(mainWindow);
        _hwnd = helper.EnsureHandle();

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        // Resolve icon
        _hIcon = ResolveAppIcon(_hwnd, out _ownsIcon);

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "CloudRedirect"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref nid);

        BuildContextMenu();
    }

    private IntPtr ResolveAppIcon(IntPtr hwnd, out bool ownsIcon)
    {
        ownsIcon = false;

        // Try file path to steam_logo3.ico in app dir
        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_logo3.ico");
            if (File.Exists(icoPath))
            {
                var h = LoadImage(IntPtr.Zero, icoPath, 1 /*IMAGE_ICON*/, 16, 16, 0x00000010 /*LR_LOADFROMFILE*/);
                if (h != IntPtr.Zero)
                {
                    ownsIcon = true;
                    return h;
                }
            }
        }
        catch { }

        // Try getting window icon
        var hIcon = SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
        if (hIcon != IntPtr.Zero) return hIcon;

        hIcon = SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
        if (hIcon != IntPtr.Zero) return hIcon;

        hIcon = GetClassLongPtr(hwnd, GCLP_HICONSM);
        if (hIcon != IntPtr.Zero) return hIcon;

        return GetClassLongPtr(hwnd, GCLP_HICON);
    }

    private MenuItem? _openMenuItem;

    private void BuildContextMenu()
    {
        _contextMenu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        var shortcutSuffix = GlobalHotkeyService.Instance.IsRegistered
            ? $" ({GlobalHotkeyService.Instance.CurrentShortcut})"
            : "";

        _openMenuItem = new MenuItem
        {
            Header = $"Open CloudRedirect{shortcutSuffix}",
            FontWeight = FontWeights.Bold
        };
        _openMenuItem.Click += (_, _) => RestoreFromTray();

        GlobalHotkeyService.Instance.OnHotkeyRegistrationChanged += (shortcut, registered) =>
        {
            _mainWindow?.Dispatcher.Invoke(() =>
            {
                if (_openMenuItem != null)
                {
                    var suffix = registered ? $" ({shortcut})" : "";
                    _openMenuItem.Header = $"Open CloudRedirect{suffix}";
                }
            });
        };

        var minimizeItem = new MenuItem
        {
            Header = "Minimize to Tray"
        };
        minimizeItem.Click += (_, _) => MinimizeToTray();

        var exitItem = new MenuItem
        {
            Header = "Exit"
        };
        exitItem.Click += (_, _) => ExitApplication();

        _contextMenu.Items.Add(_openMenuItem);
        _contextMenu.Items.Add(minimizeItem);
        _contextMenu.Items.Add(new Separator());
        _contextMenu.Items.Add(exitItem);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int cmd = lParam.ToInt32();
            if (cmd == WM_LBUTTONUP || cmd == WM_LBUTTONDBLCLK)
            {
                if (_mainWindow != null && _mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
                {
                    _mainWindow.Activate();
                }
                else
                {
                    RestoreFromTray();
                }
                handled = true;
            }
            else if (cmd == WM_RBUTTONUP)
            {
                if (_contextMenu != null)
                {
                    SetForegroundWindow(hwnd);
                    _contextMenu.IsOpen = true;
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void MinimizeToTray()
    {
        if (_mainWindow == null) return;

        _mainWindow.WindowState = WindowState.Minimized;
        _mainWindow.Hide();
        _mainWindow.ShowInTaskbar = false;

        if (!_hasShownBalloon && _isCreated)
        {
            _hasShownBalloon = true;
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_INFO,
                szInfo = "CloudRedirect is running in the background. Cloud save synchronization remains active.",
                szInfoTitle = "CloudRedirect",
                dwInfoFlags = NIIF_INFO
            };
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
    }

    public void UpdateTrayTitle(string tooltip)
    {
        if (!_isCreated) return;
        try
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_TIP,
                szTip = tooltip.Length > 127 ? tooltip.Substring(0, 124) + "..." : tooltip
            };
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
        catch { }
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isCreated || !AppSettings.ShowSyncNotifications) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_INFO,
            szInfo = message.Length > 255 ? message.Substring(0, 252) + "..." : message,
            szInfoTitle = title.Length > 63 ? title.Substring(0, 60) + "..." : title,
            dwInfoFlags = NIIF_INFO
        };
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void RestoreFromTray()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ToggleOrRestoreWindow()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized && _mainWindow.IsActive)
        {
            MinimizeToTray();
        }
        else
        {
            RestoreFromTray();
            SetForegroundWindow(_hwnd);
        }
    }

    public void ExitApplication()
    {
        if (_mainWindow != null)
        {
            _mainWindow.ForceExit();
        }
        else
        {
            Dispose();
            Application.Current.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isCreated = false;
        }

        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
