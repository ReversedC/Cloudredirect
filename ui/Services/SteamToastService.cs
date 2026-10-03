using System;
using System.Text.RegularExpressions;
using System.Windows;
using CloudRedirect.Windows;

namespace CloudRedirect.Services;

/// <summary>
/// Universal Service for displaying non-activating Steam-styled mini animated toast notifications
/// for backups, restores, auto-heals, warnings, and system events.
/// </summary>
public static class SteamToastService
{
    private static SteamToastNotificationWindow? _activeToast;
    private static readonly object _lock = new();

    public static void Show(string title, string message, ToastNotificationType type)
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

                    _activeToast = new SteamToastNotificationWindow(title, message, type);
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
                    App.LogStartup($"[SteamToast] Failed to show toast: {ex}");
                }
            }
        }));
    }

    /// <summary>
    /// Intelligently parses title, message, and emojis to automatically classify the toast notification type
    /// and format the game title and description cleanly.
    /// </summary>
    public static void ShowAuto(string rawTitle, string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return;

        var type = ToastNotificationType.Info;
        string lowerMsg = rawMessage.ToLowerInvariant();
        string lowerTitle = rawTitle.ToLowerInvariant();

        // 1. Detect Notification Type
        if (rawMessage.Contains("⚠️") || lowerMsg.Contains("issue") || lowerMsg.Contains("error") || lowerMsg.Contains("failed") || lowerMsg.Contains("rejected"))
        {
            type = ToastNotificationType.Warning;
        }
        else if (rawMessage.Contains("🩹") || lowerMsg.Contains("heal") || lowerMsg.Contains("repaired") || lowerMsg.Contains("fixed"))
        {
            type = ToastNotificationType.AutoHeal;
        }
        else if (rawMessage.Contains("↺") || lowerMsg.Contains("restored") || lowerMsg.Contains("rollback"))
        {
            type = ToastNotificationType.Restore;
        }
        else if (rawMessage.Contains("☁️") || lowerMsg.Contains("synchronized") || lowerMsg.Contains("backup") || lowerMsg.Contains("snapshot") || lowerMsg.Contains("checkpoint") || lowerMsg.Contains("saved"))
        {
            type = ToastNotificationType.Backup;
        }
        else if (rawMessage.Contains("🚀") || lowerMsg.Contains("boost"))
        {
            type = ToastNotificationType.GameBoost;
        }

        // 2. Format title & body cleanly (e.g. "Game Name: details...")
        string cleanTitle = rawTitle;
        string cleanMsg = rawMessage;

        // Strip leading emoji symbols from message
        cleanMsg = Regex.Replace(cleanMsg, @"^[\s\p{Cs}\p{So}\p{Sk}⚠️☁️🩹↺🚀💾]+", "").Trim();

        // If message has "Game Name: Description", extract Game Name as Title!
        int colonIdx = cleanMsg.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 45 && (cleanTitle.Equals("CloudRedirect", StringComparison.OrdinalIgnoreCase) || cleanTitle.Equals("Universal Cloud Saves", StringComparison.OrdinalIgnoreCase) || cleanTitle.Contains("Auto-Heal")))
        {
            cleanTitle = cleanMsg.Substring(0, colonIdx).Trim();
            cleanMsg = cleanMsg.Substring(colonIdx + 1).Trim();
        }

        Show(cleanTitle, cleanMsg, type);
    }
}
