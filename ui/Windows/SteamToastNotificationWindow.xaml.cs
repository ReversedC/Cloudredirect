using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CloudRedirect.Windows;

public enum ToastNotificationType
{
    Backup,
    Restore,
    Warning,
    AutoHeal,
    Info,
    GameBoost
}

public partial class SteamToastNotificationWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly ToastNotificationType _type;
    private bool _isClosing;

    public SteamToastNotificationWindow(string title, string message, ToastNotificationType type)
    {
        InitializeComponent();
        _type = type;

        TitleText.Text = title;
        MessageText.Text = message;

        ConfigureAppearance(type);

        // Position at bottom-right corner above taskbar
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        Loaded += SteamToastNotificationWindow_Loaded;
    }

    private void ConfigureAppearance(ToastNotificationType type)
    {
        switch (type)
        {
            case ToastNotificationType.Backup:
                CategoryBadgeText.Text = "CLOUD SYNC";
                CategoryBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                CategoryBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x14));
                CategoryBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x68, 0x1C));
                IconContainer.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x28, 0x18));
                IconContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x68, 0x1C));
                SymbolText.Text = "☁️";
                ToastShadow.Color = Color.FromRgb(0x5C, 0x7E, 0x10);
                ProgressCountdown.Fill = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                break;

            case ToastNotificationType.Restore:
                CategoryBadgeText.Text = "SAVE RESTORE";
                CategoryBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                CategoryBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x2B, 0x3D));
                CategoryBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x42, 0x5F));
                IconContainer.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x32));
                IconContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x42, 0x5F));
                SymbolText.Text = "↺";
                SymbolText.FontSize = 26;
                SymbolText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                ToastShadow.Color = Color.FromRgb(0x1A, 0x9F, 0xFF);
                ProgressCountdown.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                break;

            case ToastNotificationType.Warning:
                CategoryBadgeText.Text = "SYNC ISSUE";
                CategoryBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30));
                CategoryBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x1F, 0x08));
                CategoryBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x48, 0x10));
                IconContainer.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x1A, 0x0A));
                IconContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x48, 0x10));
                SymbolText.Text = "⚠️";
                ToastShadow.Color = Color.FromRgb(0xE5, 0xA1, 0x30);
                ProgressCountdown.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30));
                break;

            case ToastNotificationType.AutoHeal:
                CategoryBadgeText.Text = "AUTO-HEAL";
                CategoryBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                CategoryBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x2B, 0x33));
                CategoryBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x5B));
                IconContainer.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x23, 0x2A));
                IconContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x5B));
                SymbolText.Text = "🩹";
                ToastShadow.Color = Color.FromRgb(0x00, 0xD2, 0xFF);
                ProgressCountdown.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                break;

            case ToastNotificationType.GameBoost:
                CategoryBadgeText.Text = "GAME BOOST";
                CategoryBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                CategoryBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x2B, 0x3D));
                CategoryBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                IconContainer.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x32));
                IconContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                SymbolText.Text = "🚀";
                ToastShadow.Color = Color.FromRgb(0x00, 0xD2, 0xFF);
                ProgressCountdown.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                break;

            default:
                CategoryBadgeText.Text = "NOTIFICATION";
                CategoryBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
                CategoryBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x24, 0x33));
                CategoryBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3D, 0x54));
                IconContainer.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x23, 0x32));
                IconContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3D, 0x54));
                SymbolText.Text = "🎮";
                ToastShadow.Color = Color.FromRgb(0x66, 0xC0, 0xF4);
                ProgressCountdown.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                break;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var helper = new WindowInteropHelper(this);
            var hwnd = helper.Handle;
            int currentExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, currentExStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private void SteamToastNotificationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StartEntranceAnimation();
    }

    private void StartEntranceAnimation()
    {
        RootToastBorder.Opacity = 0.0;
        ToastTranslate.Y = 30;

        // 1. Toast window slide in & fade in
        var slideIn = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280));

        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        RootToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        // 2. Mini Symbol Animation based on notification type
        TriggerSymbolMicroAnimation();

        // 3. Progress countdown animation (3.8 seconds)
        var progressAnim = new DoubleAnimation(400, 0, TimeSpan.FromMilliseconds(3800));
        progressAnim.Completed += (_, _) => StartExitAnimation();
        ProgressCountdown.BeginAnimation(WidthProperty, progressAnim);
    }

    private void TriggerSymbolMicroAnimation()
    {
        switch (_type)
        {
            case ToastNotificationType.Restore:
                // Gentle 360-degree rotation spin
                var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(650))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                SymbolRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
                break;

            case ToastNotificationType.Backup:
                // Floating up & gentle pulse
                var floatUp = new DoubleAnimation(6, -2, TimeSpan.FromMilliseconds(450))
                {
                    AutoReverse = true,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                SymbolTranslate.BeginAnimation(TranslateTransform.YProperty, floatUp);

                var pulse = new DoubleAnimation(0.9, 1.1, TimeSpan.FromMilliseconds(400))
                {
                    AutoReverse = true,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                SymbolScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                SymbolScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                break;

            case ToastNotificationType.Warning:
                // Alert scale pulse
                var alertPulse = new DoubleAnimation(1.0, 1.25, TimeSpan.FromMilliseconds(200))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                SymbolScale.BeginAnimation(ScaleTransform.ScaleXProperty, alertPulse);
                SymbolScale.BeginAnimation(ScaleTransform.ScaleYProperty, alertPulse);
                break;

            case ToastNotificationType.AutoHeal:
                // Shimmer scale pop & angle tilt
                var pop = new DoubleAnimation(0.7, 1.15, TimeSpan.FromMilliseconds(350))
                {
                    AutoReverse = true,
                    EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
                };
                var tilt = new DoubleAnimation(-15, 0, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                SymbolScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                SymbolScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                SymbolRotate.BeginAnimation(RotateTransform.AngleProperty, tilt);
                break;

            case ToastNotificationType.GameBoost:
                // Rocket thrust upwards
                var thrust = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
                };
                SymbolTranslate.BeginAnimation(TranslateTransform.YProperty, thrust);
                break;
        }
    }

    private void StartExitAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;

        var slideOut = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(250));
        fadeOut.Completed += (_, _) => Close();

        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
        RootToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void CloseButton_Click(object sender, MouseButtonEventArgs e)
    {
        StartExitAnimation();
    }
}
