using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CloudRedirect.Services;
using Microsoft.Web.WebView2.Core;

namespace CloudRedirect.Windows;

public partial class MiniBrowserWindow : Window
{
    private static MiniBrowserWindow? _instance;
    private bool _isBrowserInitialized;

    private static readonly string[] AdDomains = new[]
    {
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "adnxs.com",
        "taboola.com",
        "outbrain.com",
        "criteo.com",
        "amazon-adsystem.com",
        "scorecardresearch.com",
        "rubiconproject.com",
        "pubmatic.com",
        "adroll.com",
        "adsystem",
        "adskeeper",
        "adservice",
        "fandom-ads"
    };

    public static void Open(string? targetUrl = null)
    {
        if (_instance == null || !_instance.IsLoaded)
        {
            _instance = new MiniBrowserWindow();
            _instance.Closed += (_, _) => _instance = null;
        }

        if (_instance.WindowState == WindowState.Minimized)
        {
            _instance.WindowState = WindowState.Normal;
        }

        _instance.Show();
        _instance.Activate();

        _instance.RefreshGameContext();

        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            _instance.NavigateBrowser(targetUrl);
        }
        else if (!_instance._isBrowserInitialized)
        {
            _instance.NavigateBrowser(_instance.BrowserUrlInput.Text);
        }
    }

    public static void CloseBrowser()
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher?.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_instance != null)
                {
                    _instance.Close();
                    _instance = null;
                }
            }
            catch { }
        }));
    }

    public MiniBrowserWindow()
    {
        InitializeComponent();
        RefreshGameContext();
        Loaded += MiniBrowserWindow_Loaded;
    }

    private async void MiniBrowserWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitWebBrowserAsync();
    }

    private void RefreshGameContext()
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null && !string.IsNullOrWhiteSpace(game.Name))
        {
            ActiveGameNameText.Text = game.Name;
            ActiveGameBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ActiveGameNameText.Text = "Standalone";
            ActiveGameBadge.Visibility = Visibility.Collapsed;
        }
    }

    private async Task InitWebBrowserAsync()
    {
        if (_isBrowserInitialized) return;

        try
        {
            BrowserLoadingIndicator.Visibility = Visibility.Visible;

            // Ensure native WebView2Loader.dll is deployed and registered for single-file runtime
            WebView2Helper.EnsureLoaderConfigured();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(appData, "CloudRedirect", "gamespace_browser");
            Directory.CreateDirectory(userDataFolder);

            var options = new CoreWebView2EnvironmentOptions();
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await BrowserWebView.EnsureCoreWebView2Async(env);

            BrowserWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0C, 0x12, 0x19);
            BrowserWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // 1. Network-level Ad & Tracker Blocker
            BrowserWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            BrowserWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            // 2. Cosmetic CSS Injection for clean ad slot suppression
            await BrowserWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                (function() {
                    const css = `
                        .ad-container, .top-ads-container, .bottom-ads-container, 
                        [id*='google_ads'], [id*='aswift'], [class*='advertisement'], 
                        .fandom-sticky-header, .gpt-ad, .ad-slot, .ad_wrapper,
                        [data-ad-unit], ins.adsbygoogle {
                            display: none !important;
                            visibility: hidden !important;
                            height: 0 !important;
                            max-height: 0 !important;
                            pointer-events: none !important;
                        }
                    `;
                    const style = document.createElement('style');
                    style.type = 'text/css';
                    style.appendChild(document.createTextNode(css));
                    (document.head || document.documentElement).appendChild(style);
                })();
            ");

            BrowserWebView.NavigationStarting += (_, args) =>
            {
                BrowserLoadingIndicator.Visibility = Visibility.Visible;
                BrowserUrlInput.Text = args.Uri;
            };

            BrowserWebView.NavigationCompleted += (_, args) =>
            {
                BrowserLoadingIndicator.Visibility = Visibility.Collapsed;
                if (!args.IsSuccess)
                {
                    App.LogStartup($"MiniBrowser navigation status: {args.WebErrorStatus}");
                }
            };

            _isBrowserInitialized = true;
            App.LogStartup($"MiniBrowser initialized successfully. Navigating to: {BrowserUrlInput.Text}");
            BrowserWebView.CoreWebView2.Navigate(BrowserUrlInput.Text);
        }
        catch (Exception ex)
        {
            BrowserLoadingIndicator.Visibility = Visibility.Collapsed;
            App.LogStartup($"MiniBrowser init failed: {ex}");
        }
    }

    private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri;
            foreach (var domain in AdDomains)
            {
                if (uri.Contains(domain, StringComparison.OrdinalIgnoreCase))
                {
                    var response = BrowserWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                        new MemoryStream(), 204, "No Content", "Content-Type: text/plain");
                    e.Response = response;
                    return;
                }
            }
        }
        catch { }
    }

    public void NavigateBrowser(string input)
    {
        string url = input.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains('.') && !url.Contains(' '))
            {
                url = "https://" + url;
            }
            else
            {
                url = "https://www.google.com/search?q=" + Uri.EscapeDataString(url);
            }
        }

        BrowserUrlInput.Text = url;
        if (_isBrowserInitialized && BrowserWebView.CoreWebView2 != null)
        {
            BrowserWebView.CoreWebView2.Navigate(url);
        }
    }

    private void BrowserBack_Click(object sender, RoutedEventArgs e)
    {
        if (_isBrowserInitialized && BrowserWebView.CanGoBack)
        {
            BrowserWebView.GoBack();
        }
    }

    private void BrowserForward_Click(object sender, RoutedEventArgs e)
    {
        if (_isBrowserInitialized && BrowserWebView.CanGoForward)
        {
            BrowserWebView.GoForward();
        }
    }

    private void BrowserReload_Click(object sender, RoutedEventArgs e)
    {
        if (_isBrowserInitialized)
        {
            BrowserWebView.Reload();
        }
    }

    private void BrowserGo_Click(object sender, RoutedEventArgs e)
    {
        NavigateBrowser(BrowserUrlInput.Text);
    }

    private void BrowserUrlInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateBrowser(BrowserUrlInput.Text);
        }
    }

    private void BookmarkMapGenie_Click(object sender, RoutedEventArgs e)
    {
        NavigateBrowser("https://mapgenie.io");
    }

    private void BookmarkSteamGuides_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null && game.AppId > 0)
        {
            NavigateBrowser($"https://steamcommunity.com/app/{game.AppId}/guides/");
        }
        else
        {
            NavigateBrowser("https://steamcommunity.com/?subsection=guides");
        }
    }

    private void BookmarkWiki_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null && !string.IsNullOrWhiteSpace(game.Name))
        {
            NavigateBrowser($"https://www.google.com/search?q={Uri.EscapeDataString(game.Name + " wiki guide")}");
        }
        else
        {
            NavigateBrowser("https://www.fandom.com");
        }
    }

    private void BookmarkGoogle_Click(object sender, RoutedEventArgs e)
    {
        NavigateBrowser("https://www.google.com");
    }

    private void BookmarkIgnGuides_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null && !string.IsNullOrWhiteSpace(game.Name))
        {
            NavigateBrowser($"https://www.google.com/search?q={Uri.EscapeDataString(game.Name + " site:ign.com/wikis")}");
        }
        else
        {
            NavigateBrowser("https://www.ign.com/wikis");
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        if (Topmost)
        {
            PinLabelText.Text = "Pinned";
            PinLabelText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            PinButton.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x22));
            PinButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x6D, 0x24));
        }
        else
        {
            PinLabelText.Text = "Unpinned";
            PinLabelText.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            PinButton.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x36, 0x4B));
            PinButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x52, 0x72));
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeButtonGlyph.Text = "🗖";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeButtonGlyph.Text = "🗗";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
