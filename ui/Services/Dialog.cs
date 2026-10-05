using System;
using System.Threading.Tasks;
using System.Windows;
using CloudRedirect.Dialogs;
using CloudRedirect.Resources;

namespace CloudRedirect.Services;

/// <summary>
/// Modern authentic Steam dialog helpers matching the CloudRedirect Steam theme.
/// </summary>
public static class Dialog
{
    public static async Task ShowInfoAsync(string title, string message)
    {
        await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: S.Get("Dialog_OK"),
                secondaryButtonText: null,
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.PlayGreen,
                iconType: SteamMessageBoxDialog.DialogIconType.Info);
            dlg.ShowDialog();
        });
    }

    public static async Task ShowWarningAsync(string title, string message)
    {
        await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: S.Get("Dialog_OK"),
                secondaryButtonText: null,
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.PlayGreen,
                iconType: SteamMessageBoxDialog.DialogIconType.Warning);
            dlg.ShowDialog();
        });
    }

    public static async Task ShowErrorAsync(string title, string message)
    {
        await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: S.Get("Dialog_OK"),
                secondaryButtonText: null,
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.Danger,
                iconType: SteamMessageBoxDialog.DialogIconType.Error);
            dlg.ShowDialog();
        });
    }

    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        return await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: S.Get("Dialog_Yes"),
                secondaryButtonText: S.Get("Dialog_No"),
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.PlayGreen,
                iconType: SteamMessageBoxDialog.DialogIconType.Question);
            dlg.ShowDialog();
            return dlg.UserConfirmed;
        });
    }

    public static async Task<bool> ConfirmDangerAsync(string title, string message)
    {
        return await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: S.Get("Dialog_Yes"),
                secondaryButtonText: S.Get("Dialog_No"),
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.Danger,
                iconType: SteamMessageBoxDialog.DialogIconType.Warning);
            dlg.ShowDialog();
            return dlg.UserConfirmed;
        });
    }

    public static async Task<bool> ConfirmDangerAsync(string title, UIElement content)
    {
        return await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                content,
                primaryButtonText: S.Get("Dialog_Yes"),
                secondaryButtonText: S.Get("Dialog_No"),
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.Danger);
            dlg.ShowDialog();
            return dlg.UserConfirmed;
        });
    }

    public static async Task<bool> ConfirmDangerCountdownAsync(string title, string message, int countdownSeconds = 3)
    {
        return await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: countdownSeconds > 0 ? S.Format("Dialog_YesCountdownFormat", countdownSeconds) : S.Get("Dialog_YesDeleteEverything"),
                secondaryButtonText: S.Get("Dialog_No"),
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.Danger,
                iconType: SteamMessageBoxDialog.DialogIconType.Warning);
            
            if (countdownSeconds > 0)
            {
                dlg.EnableCountdown(countdownSeconds, S.Get("Dialog_YesCountdownFormat"), S.Get("Dialog_YesDeleteEverything"));
            }

            dlg.ShowDialog();
            return dlg.UserConfirmed;
        });
    }

    public static async Task<bool> ChoiceAsync(string title, string message, string primaryText, string secondaryText)
    {
        return await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                message,
                primaryButtonText: primaryText,
                secondaryButtonText: secondaryText,
                primaryTheme: SteamMessageBoxDialog.ButtonTheme.PlayGreen,
                iconType: SteamMessageBoxDialog.DialogIconType.Question);
            dlg.ShowDialog();
            return dlg.UserConfirmed;
        });
    }

    public static async Task<bool> ShowCustomAsync(string title, UIElement content, string primaryText, string secondaryText, SteamMessageBoxDialog.ButtonTheme theme = SteamMessageBoxDialog.ButtonTheme.PlayGreen)
    {
        return await RunOnDispatcherAsync(() =>
        {
            var dlg = new SteamMessageBoxDialog(
                title,
                content,
                primaryButtonText: primaryText,
                secondaryButtonText: secondaryText,
                primaryTheme: theme);
            dlg.ShowDialog();
            return dlg.UserConfirmed;
        });
    }

    public static async Task<bool> PromptUpdateAsync(string title, string message, string primaryText, string secondaryText, string? newVersion = null, string? currentVersion = null)
    {
        return await RunOnDispatcherAsync(() =>
        {
            string nVer = newVersion ?? "";
            string cVer = currentVersion ?? "";
            if (string.IsNullOrWhiteSpace(nVer))
            {
                var match = System.Text.RegularExpressions.Regex.Match(message, @"v(\d+\.\d+\.\d+)");
                if (match.Success) nVer = match.Groups[1].Value;
            }
            if (string.IsNullOrWhiteSpace(cVer))
            {
                cVer = Services.AppUpdater.GetCurrentVersionString();
            }

            var dialog = new Dialogs.SteamUpdatePromptDialog(nVer, cVer, message);
            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialog.Owner = Application.Current.MainWindow;
            }
            bool? result = dialog.ShowDialog();
            return result == true || dialog.UserChoseUpdate;
        });
    }

    private static Task RunOnDispatcherAsync(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.InvokeAsync(action).Task;
        }
        action();
        return Task.CompletedTask;
    }

    private static Task<T> RunOnDispatcherAsync<T>(Func<T> func)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.InvokeAsync(func).Task;
        }
        return Task.FromResult(func());
    }
}
