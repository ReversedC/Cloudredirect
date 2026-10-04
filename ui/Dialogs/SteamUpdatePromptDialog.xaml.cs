using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace CloudRedirect.Dialogs;

public partial class SteamUpdatePromptDialog : Window
{
    public bool UserChoseUpdate { get; private set; }

    public SteamUpdatePromptDialog(string newVersion, string currentVersion, string? customMessage = null)
    {
        InitializeComponent();

        string cleanNew = newVersion.TrimStart('v', 'V');
        string cleanCurrent = currentVersion.TrimStart('v', 'V');

        NewVersionText.Text = $"v{cleanNew}";
        CurrentVersionText.Text = $"Installed: v{cleanCurrent}";

        TitleText.Text = $"CloudRedirect v{cleanNew} is available!";

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            // If custom message contains clean text, display it
            MessageText.Text = customMessage.Trim();
        }
        else
        {
            MessageText.Text = "A new version with performance improvements, enhanced cloud synchronization, and bug fixes is ready. Would you like to update now or later?";
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        UserChoseUpdate = true;
        DialogResult = true;
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        UserChoseUpdate = false;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UpdateNowButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            LaterButton_Click(sender, e);
        }
    }
}
