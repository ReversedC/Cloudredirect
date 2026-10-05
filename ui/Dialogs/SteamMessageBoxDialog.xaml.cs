using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CloudRedirect.Dialogs;

public partial class SteamMessageBoxDialog : Window
{
    public bool UserConfirmed { get; private set; }
    private DispatcherTimer? _countdownTimer;

    public enum ButtonTheme
    {
        PlayGreen,
        StoreBlue,
        Danger
    }

    public enum DialogIconType
    {
        None,
        Info,
        Success,
        Warning,
        Error,
        Question
    }

    public SteamMessageBoxDialog(
        string title,
        string message,
        string primaryButtonText = "OK",
        string? secondaryButtonText = null,
        ButtonTheme primaryTheme = ButtonTheme.PlayGreen,
        DialogIconType iconType = DialogIconType.None)
    {
        InitializeComponent();

        if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            Owner = Application.Current.MainWindow;
        }

        DialogTitleText.Text = title.ToUpperInvariant();
        DialogMessageText.Text = message;
        DialogMessageText.Visibility = Visibility.Visible;
        DialogCustomContent.Visibility = Visibility.Collapsed;

        ConfigureButtons(primaryButtonText, secondaryButtonText, primaryTheme);
        ConfigureIcon(iconType);
    }

    public SteamMessageBoxDialog(
        string title,
        UIElement content,
        string primaryButtonText = "OK",
        string? secondaryButtonText = null,
        ButtonTheme primaryTheme = ButtonTheme.PlayGreen)
    {
        InitializeComponent();

        if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            Owner = Application.Current.MainWindow;
        }

        DialogTitleText.Text = title.ToUpperInvariant();
        DialogMessageText.Visibility = Visibility.Collapsed;
        DialogCustomContent.Content = content;
        DialogCustomContent.Visibility = Visibility.Visible;

        ConfigureButtons(primaryButtonText, secondaryButtonText, primaryTheme);
        ConfigureIcon(DialogIconType.None);
    }

    private void ConfigureButtons(string primaryText, string? secondaryText, ButtonTheme theme)
    {
        PrimaryBtn.Content = primaryText;

        switch (theme)
        {
            case ButtonTheme.Danger:
                PrimaryBtn.Style = (Style)FindResource("SteamDangerButtonStyle");
                break;
            case ButtonTheme.StoreBlue:
                PrimaryBtn.Style = (Style)FindResource("SteamBlueButtonStyle");
                break;
            case ButtonTheme.PlayGreen:
            default:
                PrimaryBtn.Style = (Style)FindResource("SteamPlayButtonStyle");
                break;
        }

        if (!string.IsNullOrEmpty(secondaryText))
        {
            SecondaryBtn.Content = secondaryText;
            SecondaryBtn.Visibility = Visibility.Visible;
        }
        else
        {
            SecondaryBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigureIcon(DialogIconType iconType)
    {
        if (iconType == DialogIconType.None)
        {
            StatusIconBorder.Visibility = Visibility.Collapsed;
            return;
        }

        StatusIconBorder.Visibility = Visibility.Visible;
        switch (iconType)
        {
            case DialogIconType.Success:
                StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                break;
            case DialogIconType.Warning:
                StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning24;
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));
                break;
            case DialogIconType.Error:
                StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle24;
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xDF, 0x56, 0x48));
                break;
            case DialogIconType.Question:
                StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.QuestionCircle24;
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                break;
            case DialogIconType.Info:
            default:
                StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Info24;
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                break;
        }
    }

    public void EnableCountdown(int seconds, string formatString, string enabledText)
    {
        if (seconds <= 0) return;

        PrimaryBtn.IsEnabled = false;
        int remaining = seconds;
        PrimaryBtn.Content = string.Format(formatString, remaining);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                _countdownTimer.Stop();
                PrimaryBtn.IsEnabled = true;
                PrimaryBtn.Content = enabledText;
            }
            else
            {
                PrimaryBtn.Content = string.Format(formatString, remaining);
            }
        };
        _countdownTimer.Start();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer?.Stop();
        UserConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer?.Stop();
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer?.Stop();
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PrimaryBtn.IsEnabled)
        {
            PrimaryBtn_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CloseButton_Click(sender, e);
        }
    }
}
