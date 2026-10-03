using System;
using System.IO;
using System.Windows;
using CloudRedirect.TrayHelper;

namespace CloudRedirect.Services;

/// <summary>
/// Manages the Windows System Tray (notification area) icon, context menu,
/// and minimize/restore transitions using rock-solid WinFormsTrayHelper.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static TrayIconService? _instance;
    public static TrayIconService Instance => _instance ??= new TrayIconService();

    private MainWindow? _mainWindow;
    private WinFormsTrayHelper? _trayHelper;
    private bool _isCreated;
    private bool _hasShownBalloon;

    public void Initialize(MainWindow mainWindow)
    {
        if (_isCreated) return;
        _mainWindow = mainWindow;

        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_logo3.ico");
            if (!File.Exists(icoPath))
            {
                icoPath = Path.Combine(Directory.GetCurrentDirectory(), "steam_logo3.ico");
            }

            _trayHelper = new WinFormsTrayHelper();
            _isCreated = _trayHelper.Initialize(
                "CloudRedirect",
                File.Exists(icoPath) ? icoPath : null,
                onOpen: () => RestoreFromTray(),
                onExit: () => ExitApplication(),
                onToggleOrRestore: () => ToggleOrRestoreWindow()
            );

            App.LogStartup($"TrayIconService.Initialize: isCreated={_isCreated}");
        }
        catch (Exception ex)
        {
            App.LogStartup($"TrayIconService.Initialize EXCEPTION: {ex}");
        }
    }

    public void MinimizeToTray()
    {
        if (_mainWindow == null) return;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            App.LogStartup("TrayIconService.MinimizeToTray: window hidden from taskbar to tray");
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.Hide();
            _mainWindow.ShowInTaskbar = false;
        });

        if (!_hasShownBalloon && _trayHelper != null)
        {
            _hasShownBalloon = true;
            _trayHelper.ShowBalloon("CloudRedirect", "CloudRedirect is running in the background. Cloud save synchronization remains active.");
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (!AppSettings.ShowSyncNotifications) return;

        // Route to custom Steam-styled mini animated toast overlay (Zero focus stealing)
        SteamToastService.ShowAuto(title, message);
    }

    public void RestoreFromTray()
    {
        if (_mainWindow == null) return;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            App.LogStartup("TrayIconService.RestoreFromTray: restoring window to normal and foreground");
            _mainWindow.Show();
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    public void ToggleOrRestoreWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized && _mainWindow.IsActive)
            {
                MinimizeToTray();
            }
            else
            {
                RestoreFromTray();
            }
        });
    }

    public void ExitApplication()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Dispatcher.Invoke(() => _mainWindow.ForceExit());
        }
        else
        {
            Dispose();
            Environment.Exit(0);
        }
    }

    public void Dispose()
    {
        _trayHelper?.Dispose();
        _trayHelper = null;
        _isCreated = false;
    }
}
