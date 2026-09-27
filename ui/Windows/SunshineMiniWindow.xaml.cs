using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;
using CloudRedirect.Services;

namespace CloudRedirect.Windows;

public partial class SunshineMiniWindow : FluentWindow
{
    private static SunshineMiniWindow? _activeInstance;
    private string _targetUrl = "https://localhost:47990";
    private bool _isInitialized = false;

    public SunshineMiniWindow(string initialUrl = "https://localhost:47990")
    {
        InitializeComponent();
        _targetUrl = initialUrl;
        UrlDisplayBlock.Text = _targetUrl;

        Loaded += SunshineMiniWindow_Loaded;
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

                // Sunshine uses self-signed HTTPS certificate on port 47990.
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

                SunshineWebView.CoreWebView2.NavigationStarting += (s, args) =>
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    UrlDisplayBlock.Text = args.Uri;
                };

                SunshineWebView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    if (SunshineWebView.Source != null)
                    {
                        UrlDisplayBlock.Text = SunshineWebView.Source.ToString();
                    }
                    UpdateNavButtons();
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
}
