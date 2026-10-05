using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CloudRedirect.Resources;

namespace CloudRedirect.Pages;

public partial class CloudProviderPage : Page
{
    private Services.OAuthService? _oauth;
    private CancellationTokenSource? _authCts;
    private bool _isAuthenticating;
    private bool _loading = true;
    private readonly StringBuilder _logBuffer = new();

    // Upload in-flight cap (MB). Only shown/saved for Google Drive.
    private const int InFlightDefaultMb = 24;
    private const int InFlightMinMb = 24;
    private const int InFlightMaxMb = 64;
    // Suppresses the slider ValueChanged handler during programmatic load.
    private bool _inFlightLoading;
    private string? _activeQrPayload;
    private Dialogs.SteamQrZoomDialog? _qrZoomDialog;

    public CloudProviderPage()
    {
        _loading = true;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try { await LoadCurrentConfigAsync(); }
            catch { }
        };
        // Cancel in-flight OAuth on Unloaded to stop the loopback listener and prevent leaked references.
        Unloaded += (_, _) =>
        {
            if (_isAuthenticating)
                _authCts?.Cancel();
            if (_qrZoomDialog != null && _qrZoomDialog.IsLoaded)
            {
                _qrZoomDialog.Close();
                _qrZoomDialog = null;
            }
        };
    }

    /// <summary>Off-thread config snapshot for LoadCurrentConfigAsync.</summary>
    private sealed record LoadedConfigSnapshot(
        Services.CloudConfig? Config,
        string DefaultLocalPath,
        string PathTextOverride,
        Services.TokenStatus? TokenStatus,
        int UploadInFlightMb);

    // M14: Read config + token status off UI thread to avoid disk/DPAPI stall.
    private async Task LoadCurrentConfigAsync()
    {
        // Set _loading before I/O to suppress SelectionChanged during init.
        _loading = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var config = Services.SteamDetector.ReadConfig();
                string pathOverride = config?.TokenPath ?? "";

                Services.TokenStatus? tokenStatus = null;
                if (!string.IsNullOrEmpty(pathOverride))
                    tokenStatus = Services.OAuthService.CheckTokenStatus(pathOverride);

                return new LoadedConfigSnapshot(config, "", pathOverride, tokenStatus, ReadUploadInFlightMb());
            });

            ApplyLoadedSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            AuthStatus.Text = S.Format("CloudProvider_ErrorReadingConfig", ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyLoadedSnapshot(LoadedConfigSnapshot snap)
    {
        ApplyUploadInFlight(snap.UploadInFlightMb);

        if (snap.Config == null)
        {
            AuthStatus.Text = S.Get("CloudProvider_NoConfigFound");
            ProviderCombo.SelectedIndex = 0; // Google Drive (default)
            UpdateProviderUI();
            return;
        }

        ProviderCombo.SelectedIndex = 0;

        if (!string.IsNullOrEmpty(snap.PathTextOverride))
            TokenPathBox.Text = snap.PathTextOverride;

        UpdateProviderUI();
        UpdateAuthStatus(snap.TokenStatus);
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        UpdateProviderUI();

        if (ProviderCombo.SelectedItem is ComboBoxItem item)
        {
            TokenPathBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CloudRedirect", "gdrive_tokens.json");
        }

        UpdateAuthStatus();
        // Persist the provider switch (and the path it just set).
        _ = SaveConfigSilent();
    }

    // Auto-save manual path edits when the field loses focus, rather than on
    // every keystroke.
    private void TokenPathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _ = SaveConfigSilent();
    }

    /// <summary>
    /// Updates labels, enabled state, and hints for the selected provider.
    /// </summary>
    private void UpdateProviderUI()
    {
        if (PathLabel == null || TokenPathGrid == null || TokenPathBox == null || BrowseButton == null || SignInButton == null)
            return;

        PathLabel.Visibility = Visibility.Visible;
        TokenPathGrid.Visibility = Visibility.Visible;
        TokenPathBox.Visibility = Visibility.Visible;
        BrowseButton.Visibility = Visibility.Visible;
        TokenPathBox.IsEnabled = true;
        BrowseButton.IsEnabled = true;
        SignInButton.Visibility = Visibility.Visible;
        if (UploadInFlightSection != null)
            UploadInFlightSection.Visibility = Visibility.Visible;

        PathLabel.Text = S.Get("CloudProvider_TokenFilePath");
        TokenPathBox.PlaceholderText = S.Get("CloudProvider_TokenPlaceholder");
        if (PathHint != null)
        {
            PathHint.Text = "";
            PathHint.Visibility = Visibility.Collapsed;
        }
    }

    private void BrowseToken_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = S.Get("CloudProvider_SelectTokenFile"),
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = false
        };

        if (dialog.ShowDialog() == true)
        {
            TokenPathBox.Text = dialog.FileName;
            UpdateAuthStatus();
            _ = SaveConfigSilent();
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_isAuthenticating) return;

        var provider = "gdrive";

        var tokenPath = TokenPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(tokenPath))
        {
            await Services.Dialog.ShowWarningAsync(S.Get("CloudProvider_MissingPath"),
                S.Get("CloudProvider_MissingPathMessage"));
            return;
        }

        _isAuthenticating = true;
        _authCts = new CancellationTokenSource();
        _oauth = new Services.OAuthService();

        SignInButton.IsEnabled = false;
        CopyAuthUrlButton.Visibility = Visibility.Visible;
        CopyAuthUrlButton.IsEnabled = false;
        OpenBrowserButton.Visibility = Visibility.Visible;
        OpenBrowserButton.IsEnabled = false;
        CancelAuthButton.Visibility = Visibility.Visible;
        AuthAssistCard.Visibility = Visibility.Visible;
        ManualCodeInput.Text = string.Empty;
        ManualCodeError.Visibility = Visibility.Collapsed;
        SubmitManualCodeButton.IsEnabled = true;
        ProviderCombo.IsEnabled = false;
        LogBorder.Visibility = Visibility.Visible;
        _logBuffer.Clear();
        LogOutput.Text = "";

        _oauth.AuthUrlReady += url => Dispatcher.BeginInvoke(() =>
        {
            CopyAuthUrlButton.IsEnabled = true;
            OpenBrowserButton.IsEnabled = true;
            if (string.IsNullOrEmpty(_oauth?.MobileHelperUrl))
            {
                QrModeWifiRadio.IsEnabled = false;
                QrModeDirectRadio.IsChecked = true;
            }
            else
            {
                QrModeWifiRadio.IsEnabled = true;
                QrModeWifiRadio.IsChecked = true;
            }
            UpdateQrCode();
        });

        try
        {
            bool success = await _oauth.AuthorizeAsync(
                provider,
                tokenPath,
                msg => Dispatcher.BeginInvoke(() => AppendLog(msg)),
                _authCts.Token);

            if (success)
            {
                _qrZoomDialog?.NotifyAuthSuccess();
                // Also save the config so the DLL picks up the new provider + token path
                await SaveConfigSilent();
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Authentication cancelled.");
            _qrZoomDialog?.Close();
            _qrZoomDialog = null;
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _qrZoomDialog?.Close();
            _qrZoomDialog = null;
        }
        finally
        {
            _oauth?.Dispose();
            _oauth = null;
            _authCts?.Dispose();
            _authCts = null;
            _isAuthenticating = false;

            SignInButton.IsEnabled = true;
            CopyAuthUrlButton.Visibility = Visibility.Collapsed;
            OpenBrowserButton.Visibility = Visibility.Collapsed;
            CancelAuthButton.Visibility = Visibility.Collapsed;
            AuthAssistCard.Visibility = Visibility.Collapsed;
            ManualCodeError.Visibility = Visibility.Collapsed;
            ProviderCombo.IsEnabled = true;

            UpdateAuthStatus();
        }
    }

    private void CopyAuthUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = _oauth?.CurrentAuthUrl;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Clipboard.SetText(url);
                CopyAuthUrlButton.Content = S.Get("CloudProvider_Copied");
                AppendLog("Copied authorization link to clipboard.");
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, ev) =>
                {
                    CopyAuthUrlButton.Content = S.Get("CloudProvider_CopyLink");
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                AppendLog($"Could not copy link to clipboard: {ex.Message}");
            }
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        var url = _oauth?.CurrentAuthUrl;
        if (!string.IsNullOrEmpty(url))
        {
            AppendLog("Retrying browser launch...");
            Services.OAuthService.TryOpenBrowser(url, msg => Dispatcher.BeginInvoke(() => AppendLog(msg)));
        }
    }

    private void SubmitManualCode_Click(object sender, RoutedEventArgs e)
    {
        ManualCodeError.Visibility = Visibility.Collapsed;
        ManualCodeError.Text = string.Empty;

        var text = ManualCodeInput.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ManualCodeError.Text = S.Get("CloudProvider_MissingManualCode");
            ManualCodeError.Visibility = Visibility.Visible;
            return;
        }

        if (_oauth == null)
        {
            ManualCodeError.Text = "Authorization session is not active. Click 'Sign In' first.";
            ManualCodeError.Visibility = Visibility.Visible;
            return;
        }

        if (_oauth.TrySubmitManualCodeOrUrl(text, out var error))
        {
            AppendLog("Manual authorization code accepted. Exchanging for tokens...");
            ManualCodeInput.Text = string.Empty;
            ManualCodeError.Visibility = Visibility.Collapsed;
            SubmitManualCodeButton.IsEnabled = false;
        }
        else
        {
            ManualCodeError.Text = error;
            ManualCodeError.Visibility = Visibility.Visible;
            AppendLog($"Manual code entry error: {error}");
        }
    }

    private void ManualCodeInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitManualCode_Click(sender, e);
        }
    }

    private void UpdateQrCode()
    {
        try
        {
            string? payload = null;
            if (QrModeWifiRadio?.IsChecked == true && !string.IsNullOrEmpty(_oauth?.MobileHelperUrl))
            {
                payload = _oauth.MobileHelperUrl;
                QrModeHintText.Text = S.Get("CloudProvider_QrModeWifiHint");
            }
            else
            {
                payload = _oauth?.CurrentAuthUrl;
                QrModeHintText.Text = S.Get("CloudProvider_QrModeDirectHint");
            }

            _activeQrPayload = payload;

            if (!string.IsNullOrEmpty(payload))
            {
                QrCodeImage.Source = Services.QrCodeHelper.GenerateQrCode(payload, 8);
            }
            else
            {
                QrCodeImage.Source = null;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Notice: QR Code generation error: {ex.Message}");
        }
    }

    private void QrImage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenQrZoomDialog();
    }

    private void EnlargeQr_Click(object sender, RoutedEventArgs e)
    {
        OpenQrZoomDialog();
    }

    private void OpenQrZoomDialog()
    {
        if (string.IsNullOrEmpty(_activeQrPayload)) return;

        if (_qrZoomDialog != null && _qrZoomDialog.IsLoaded)
        {
            _qrZoomDialog.Activate();
            return;
        }

        string modeLabel = QrModeWifiRadio?.IsChecked == true
            ? S.Get("CloudProvider_QrModeWifi")
            : S.Get("CloudProvider_QrModeDirect");

        var owner = Window.GetWindow(this);
        _qrZoomDialog = new Dialogs.SteamQrZoomDialog(_activeQrPayload, modeLabel, _oauth, owner);
        _qrZoomDialog.Closed += (_, _) => _qrZoomDialog = null;
        _qrZoomDialog.Show();
    }

    private void QrMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_isAuthenticating)
        {
            UpdateQrCode();
        }
    }

    private void CancelAuth_Click(object sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        // Don't dispose _oauth here -- the SignIn_Click finally block handles cleanup
        // after the async operation observes cancellation.
    }

    private async Task<bool> SaveConfigSilent()
    {
        var configDir = Services.SteamDetector.GetConfigDir();

        Directory.CreateDirectory(configDir);

        var provider = GetSelectedProvider();
        var tokenPath = TokenPathBox.Text?.Trim() ?? "";

        var configPath = Path.Combine(configDir, "config.json");

        // Read existing token_paths to merge the new entry.
        var tokenPaths = new Dictionary<string, string>();
        if (File.Exists(configPath))
        {
            try
            {
                var existingJson = File.ReadAllText(configPath);
                using var existingDoc = System.Text.Json.JsonDocument.Parse(existingJson);
                if (existingDoc.RootElement.TryGetProperty("token_paths", out var tps) &&
                    tps.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in tps.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                            tokenPaths[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
            }
            catch { }
        }

        // Register Google Drive token path
        if (!string.IsNullOrEmpty(tokenPath))
            tokenPaths["gdrive"] = tokenPath;

        try
        {
            Services.ConfigHelper.SaveConfig(configPath,
                new[] { "provider", "token_path", "token_paths" },
                writer =>
                {
                    writer.WriteString("provider", "gdrive");
                    writer.WriteString("token_path", tokenPath);

                    // Persist per-provider token path registry.
                    writer.WritePropertyName("token_paths");
                    writer.WriteStartObject();
                    foreach (var (key, value) in tokenPaths)
                        writer.WriteString(key, value);
                    writer.WriteEndObject();
                });
            return true;
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Format("CloudProvider_FailedSaveConfig", ex.Message));
            return false;
        }
    }

    private string GetSelectedProvider()
    {
        return "gdrive";
    }

    private void OpenWizard_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(SetupWizardPage));
        }
    }

    private void UpdateAuthStatus(Services.TokenStatus? preCheckedStatus = null)
    {
        var tokenPath = TokenPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(tokenPath))
        {
            AuthStatus.Text = S.Get("CloudProvider_NoTokenFilePath");
            AuthIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldKeyhole24;
            return;
        }

        var status = preCheckedStatus ?? Services.OAuthService.CheckTokenStatus(tokenPath);
        AuthStatus.Text = status.Message;
        AuthIcon.Symbol = status.IsAuthenticated
            ? Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24
            : Wpf.Ui.Controls.SymbolRegular.ShieldKeyhole24;
    }

    /// <summary>Reads upload_inflight_mb from config.json, clamped 24..64.
    /// Absent/invalid -> the 24 MB default. Off the UI thread.</summary>
    private static int ReadUploadInFlightMb()
    {
        try
        {
            var path = Services.SteamDetector.GetConfigFilePath();
            if (!File.Exists(path)) return InFlightDefaultMb;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("upload_inflight_mb", out var inf) && inf.TryGetInt32(out var mb))
                return Math.Clamp(mb, InFlightMinMb, InFlightMaxMb);
        }
        catch { }
        return InFlightDefaultMb;
    }

    private void ApplyUploadInFlight(int mb)
    {
        _inFlightLoading = true;
        try
        {
            UploadInFlightSlider.Value = Math.Clamp(mb, InFlightMinMb, InFlightMaxMb);
            UpdateUploadInFlightValueLabel();
        }
        finally { _inFlightLoading = false; }
    }

    private void UploadInFlightSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateUploadInFlightValueLabel();
        if (_inFlightLoading) return;
        SaveUploadInFlight();
    }

    private void UpdateUploadInFlightValueLabel()
    {
        if (UploadInFlightValue != null)
            UploadInFlightValue.Text = S.Format("CloudProvider_UploadInFlightValue", (int)UploadInFlightSlider.Value);
    }

    /// <summary>Persists upload_inflight_mb (clamped 24..64) into config.json.</summary>
    private void SaveUploadInFlight()
    {
        int mb = Math.Clamp((int)Math.Round(UploadInFlightSlider.Value), InFlightMinMb, InFlightMaxMb);
        Services.ConfigHelper.SaveConfig(Services.SteamDetector.GetConfigFilePath(),
            new[] { "upload_inflight_mb" },
            writer => writer.WriteNumber("upload_inflight_mb", mb));
    }

    private void AppendLog(string message)
    {
        if (_logBuffer.Length > 0)
            _logBuffer.AppendLine();
        _logBuffer.Append(message);
        LogOutput.Text = _logBuffer.ToString();
        LogScroll.ScrollToEnd();
    }
}
