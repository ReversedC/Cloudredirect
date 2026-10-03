using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace CloudRedirect.Windows;

public partial class GameBoostToastWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private bool _isClosing;

    public GameBoostToastWindow(string gameName, string? headerUrl, string boostSummary)
    {
        InitializeComponent();

        GameTitleText.Text = gameName;
        if (!string.IsNullOrWhiteSpace(boostSummary))
        {
            BoostMetricsText.Text = boostSummary;
        }

        if (!string.IsNullOrWhiteSpace(headerUrl))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(headerUrl);
                bmp.EndInit();

                GameArtworkImage.Source = bmp;
                ArtworkFallbackIcon.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ArtworkFallbackIcon.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ArtworkFallbackIcon.Visibility = Visibility.Visible;
        }

        // Position at top-middle of the screen (middle and above)
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + 24;

        Loaded += GameBoostToastWindow_Loaded;
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

    private void GameBoostToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StartEntranceAnimation();
    }

    private void StartEntranceAnimation()
    {
        RootToastBorder.Opacity = 0.0;
        ToastTranslate.Y = -30;

        var slideIn = new DoubleAnimation(-30, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280));

        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        RootToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        // Progress line countdown animation (3.5 seconds)
        var progressAnim = new DoubleAnimation(390, 0, TimeSpan.FromMilliseconds(3500));
        progressAnim.Completed += (_, _) => StartExitAnimation();
        ProgressCountdown.BeginAnimation(WidthProperty, progressAnim);
    }

    private void StartExitAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;

        var slideOut = new DoubleAnimation(0, -30, TimeSpan.FromMilliseconds(280))
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
