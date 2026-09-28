using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;
using CloudRedirect.Services;

namespace CloudRedirect.Windows;

public partial class SunshineMiniWindow : FluentWindow
{
    private static SunshineMiniWindow? _activeInstance;
    private string _targetUrl = "https://localhost:47990";
    private bool _isInitialized = false;
    private bool _isAutoFit = true;

    public SunshineMiniWindow(string initialUrl = "https://localhost:47990")
    {
        InitializeComponent();
        _targetUrl = initialUrl;
        UrlDisplayBlock.Text = _targetUrl;

        Loaded += SunshineMiniWindow_Loaded;
        SizeChanged += SunshineMiniWindow_SizeChanged;
        PreviewMouseWheel += SunshineMiniWindow_PreviewMouseWheel;

        Closed += (s, e) =>
        {
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
        };
    }

    public static void ShowWindow(string url = "https://localhost:47990")
    {
        if (_activeInstance != null && _activeInstance.IsLoaded)
        {
            _activeInstance.WindowState = WindowState.Normal;
            _activeInstance.Activate();
            _activeInstance.Focus();
            _activeInstance.NavigateTo(url);
        }
        else
        {
            var win = new SunshineMiniWindow(url);
            _activeInstance = win;
            win.Show();
            win.Activate();
        }
    }

    private async void SunshineMiniWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Clamp initial window size to monitor work area
            var workArea = SystemParameters.WorkArea;
            if (Height > workArea.Height - 30)
            {
                Height = Math.Max(480, workArea.Height - 40);
            }
            if (Width > workArea.Width - 30)
            {
                Width = Math.Max(760, workArea.Width - 40);
            }

            var userFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CloudRedirect",
                "SunshineWebView2");

            Directory.CreateDirectory(userFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userFolder);

            await SunshineWebView.EnsureCoreWebView2Async(env);

            if (SunshineWebView.CoreWebView2 != null)
            {
                _isInitialized = true;
                SunshineWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                SunshineWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // 1. Sunshine uses self-signed HTTPS certificate on port 47990.
                // Always bypass the warning inside our mini window so user gets seamless dashboard access!
                SunshineWebView.CoreWebView2.ServerCertificateErrorDetected += (s, args) =>
                {
                    var uri = args.RequestUri ?? string.Empty;
                    if (uri.StartsWith("https://localhost:47990", StringComparison.OrdinalIgnoreCase) ||
                        uri.StartsWith("https://127.0.0.1:47990", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
                    }
                };

                // 2. Auto-authenticate HTTP Basic Auth with Sunshine username and password
                SunshineWebView.CoreWebView2.BasicAuthenticationRequested += (s, args) =>
                {
                    var uri = args.Uri ?? string.Empty;
                    if (uri.StartsWith("https://localhost:47990", StringComparison.OrdinalIgnoreCase) ||
                        uri.StartsWith("https://127.0.0.1:47990", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Response.UserName = AppSettings.SunshineUsername ?? "admin";
                        args.Response.Password = AppSettings.SunshinePassword ?? "";
                    }
                };

                SunshineWebView.CoreWebView2.NavigationStarting += (s, args) =>
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    UrlDisplayBlock.Text = args.Uri;
                };

                SunshineWebView.CoreWebView2.NavigationCompleted += async (s, args) =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    if (SunshineWebView.Source != null)
                    {
                        UrlDisplayBlock.Text = SunshineWebView.Source.ToString();
                    }
                    UpdateNavButtons();

                    // Inject responsive CSS into Sunshine Web UI so it auto-fits nicely inside the mini window
                    try
                    {
                        await SunshineWebView.CoreWebView2.ExecuteScriptAsync(@"
                            (function() {
                                var style = document.getElementById('cloudredirect-autofit');
                                if (!style) {
                                    style = document.createElement('style');
                                    style.id = 'cloudredirect-autofit';
                                    style.innerHTML = 'body { overflow-x: hidden !important; max-width: 100vw !important; } .container, .container-fluid { max-width: 100% !important; padding-left: 12px !important; padding-right: 12px !important; } .navbar { padding-left: 12px !important; padding-right: 12px !important; }';
                                    document.head.appendChild(style);
                                }
                            })();
                        ");
                    }
                    catch { }

                    if (_isAutoFit)
                    {
                        ApplyAutoFitZoom();
                    }
                };

                SunshineWebView.CoreWebView2.SourceChanged += (s, args) =>
                {
                    if (SunshineWebView.Source != null)
                    {
                        UrlDisplayBlock.Text = SunshineWebView.Source.ToString();
                    }
                    UpdateNavButtons();
                };

                NavigateTo(_targetUrl);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SunshineMiniWindow] WebView2 initialization failed: {ex.Message}");
            LoadingOverlay.Visibility = Visibility.Collapsed;
            SunshineWebView.Visibility = Visibility.Collapsed;
            FallbackPanel.Visibility = Visibility.Visible;
        }
    }

    private void SunshineMiniWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isAutoFit && _isInitialized && SunshineWebView.CoreWebView2 != null)
        {
            ApplyAutoFitZoom();
        }
    }

    private void SunshineMiniWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (e.Delta > 0)
                ZoomIn();
            else if (e.Delta < 0)
                ZoomOut();
        }
    }

    public void NavigateTo(string url)
    {
        _targetUrl = url;
        UrlDisplayBlock.Text = url;

        if (_isInitialized && SunshineWebView.CoreWebView2 != null)
        {
            try
            {
                SunshineWebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SunshineMiniWindow] Navigation error: {ex.Message}");
            }
        }
    }

    private void UpdateNavButtons()
    {
        NavBackBtn.IsEnabled = SunshineWebView.CanGoBack;
        NavForwardBtn.IsEnabled = SunshineWebView.CanGoForward;
    }

    private void NavBackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SunshineWebView.CanGoBack)
        {
            SunshineWebView.GoBack();
        }
    }

    private void NavForwardBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SunshineWebView.CanGoForward)
        {
            SunshineWebView.GoForward();
        }
    }

    private void NavRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        SunshineWebView.Reload();
    }

    private void GoDashboardBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("https://localhost:47990");
    }

    private void GoPinBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("https://localhost:47990/pin");
    }

    private void CopyCredsBtn_Click(object sender, RoutedEventArgs e)
    {
        var pass = AppSettings.SunshinePassword;
        if (!string.IsNullOrEmpty(pass))
        {
            try
            {
                Clipboard.SetText(pass);
                CopyCredsBtn.Content = "Copied!";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    CopyCredsBtn.Content = "Copy Password";
                };
                timer.Start();
            }
            catch { }
        }
    }

    private void OpenExternalBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = SunshineWebView.Source != null ? SunshineWebView.Source.ToString() : _targetUrl;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void DownloadWebView2_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
        }
        catch { }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomIn();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomOut();
    }

    private void ZoomIn()
    {
        if (SunshineWebView.CoreWebView2 != null)
        {
            _isAutoFit = false;
            SunshineWebView.ZoomFactor = Math.Min(2.0, Math.Round(SunshineWebView.ZoomFactor + 0.1, 2));
            UpdateZoomDisplay();
        }
    }

    private void ZoomOut()
    {
        if (SunshineWebView.CoreWebView2 != null)
        {
            _isAutoFit = false;
            SunshineWebView.ZoomFactor = Math.Max(0.5, Math.Round(SunshineWebView.ZoomFactor - 0.1, 2));
            UpdateZoomDisplay();
        }
    }

    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => ZoomOut();

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => ZoomIn();

    private void AutoFitBtn_Click(object sender, RoutedEventArgs e)
    {
        _isAutoFit = true;
        ApplyAutoFitZoom();
    }

    private void ZoomPercent_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isAutoFit = !_isAutoFit;
        if (_isAutoFit)
        {
            ApplyAutoFitZoom();
        }
        else
        {
            if (SunshineWebView.CoreWebView2 != null)
            {
                SunshineWebView.ZoomFactor = 1.0;
                UpdateZoomDisplay();
            }
        }
    }

    private void ApplyAutoFitZoom()
    {
        if (SunshineWebView.CoreWebView2 == null) return;
        try
        {
            double availableWidth = SunshineWebView.ActualWidth;
            if (availableWidth > 200)
            {
                // Sunshine web UI standard desktop content width is ~1080px
                double idealScale = Math.Min(1.0, availableWidth / 1060.0);
                SunshineWebView.ZoomFactor = Math.Max(0.65, Math.Round(idealScale, 2));
                ZoomPercentText.Text = $"Auto ({(int)(SunshineWebView.ZoomFactor * 100)}%)";
                ZoomPercentText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
        }
        catch { }
    }

    private void UpdateZoomDisplay()
    {
        if (SunshineWebView.CoreWebView2 == null) return;
        int pct = (int)Math.Round(SunshineWebView.ZoomFactor * 100);
        ZoomPercentText.Text = $"{pct}%";
        ZoomPercentText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x66, 0xC0, 0xF4));
    }
}
