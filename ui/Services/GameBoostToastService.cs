using System;
using System.Windows;
using CloudRedirect.Windows;

namespace CloudRedirect.Services;

/// <summary>
/// Thread-safe service for presenting non-activating Steam-styled Game Boost animated toast notifications.
/// </summary>
public static class GameBoostToastService
{
    private static GameBoostToastWindow? _activeToast;
    private static readonly object _lock = new();

    public static void Show(string gameName, string? headerUrl, string boostSummary)
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher?.BeginInvoke(new Action(() =>
        {
            lock (_lock)
            {
                try
                {
                    if (_activeToast != null)
                    {
                        try { _activeToast.Close(); } catch { }
                        _activeToast = null;
                    }

                    _activeToast = new GameBoostToastWindow(gameName, headerUrl, boostSummary);
                    _activeToast.Closed += (_, _) =>
                    {
                        lock (_lock)
                        {
                            _activeToast = null;
                        }
                    };
                    _activeToast.Show();
                }
                catch (Exception ex)
                {
                    App.LogStartup($"[GameBoostToast] Failed to show toast: {ex}");
                    System.Diagnostics.Debug.WriteLine($"[GameBoostToast] Failed to show toast: {ex.Message}");
                }
            }
        }));
    }

    public static void Dismiss()
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher?.BeginInvoke(new Action(() =>
        {
            lock (_lock)
            {
                try
                {
                    if (_activeToast != null)
                    {
                        _activeToast.Close();
                        _activeToast = null;
                    }
                }
                catch { }
            }
        }));
    }
}
