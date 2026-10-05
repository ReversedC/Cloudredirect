using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CloudRedirect.Resources;
using CloudRedirect.Services;
using Microsoft.Win32;

namespace CloudRedirect.Pages;

public partial class SetupWizardPage : Page
{
    private OAuthService? _oauth;
    private CancellationTokenSource? _authCts;
    private bool _isAuthenticating;
    private bool _languageLoading;
    private string _tokenPath = "";
    private string? _activeQrPayload;
    private Dialogs.SteamQrZoomDialog? _qrZoomDialog;

    public SetupWizardPage()
    {
        InitializeComponent();

        var configDir = SteamDetector.GetConfigDir();
        Directory.CreateDirectory(configDir);
        _tokenPath = Path.Combine(configDir, "gdrive_tokens.json");

        InitializeLanguageSelector();
        LanguageService.OnLanguageChanged += OnLanguageChanged;
        UiZoomManager.Instance.OnZoomChanged += OnZoomChangedHandler;

        Loaded += async (_, _) =>
        {
            await InitializeEnvironmentCheckAsync();
            UpdateQrDisplaySize();
        };

        SizeChanged += (_, _) =>
        {
            UpdateQrDisplaySize();
        };

        Unloaded += (_, _) =>
        {
            LanguageService.OnLanguageChanged -= OnLanguageChanged;
            UiZoomManager.Instance.OnZoomChanged -= OnZoomChangedHandler;
            if (_isAuthenticating)
            {
                _authCts?.Cancel();
                _oauth?.Dispose();
            }
            if (_qrZoomDialog != null && _qrZoomDialog.IsLoaded)
            {
                _qrZoomDialog.Close();
                _qrZoomDialog = null;
            }
        };
    }

    private void OnLanguageChanged()
    {
        InitializeLanguageSelector();
        _ = InitializeEnvironmentCheckAsync();

        if (_isAuthenticating)
        {
            WizardSignInBtn.Content = S.Get("Wizard_Step2_Cancel");
            WizardPhoneSignInBtn.Content = S.Get("Wizard_Step2_SignInPhone");
            WizardAuthStatusTitle.Text = WizardPhoneSignInBtn.IsEnabled
                ? S.Get("Wizard_Step2_WaitingPhone")
                : S.Get("Wizard_Step2_OpeningBrowser");
        }
        else if (Step2NextBtn.IsEnabled)
        {
            WizardSignInBtn.Content = S.Get("Wizard_Step2_SignInAgain");
            WizardPhoneSignInBtn.Content = S.Get("Wizard_Step2_SignInPhone");
            WizardAuthStatusTitle.Text = S.Get("Wizard_Step2_ConnectedTitle");
        }
        else
        {
            WizardSignInBtn.Content = S.Get("Wizard_Step2_SignInBrowser");
            WizardPhoneSignInBtn.Content = S.Get("Wizard_Step2_SignInPhone");
            WizardAuthStatusTitle.Text = S.Get("Wizard_Step2_NotConnectedTitle");
        }
    }

    private void InitializeLanguageSelector()
    {
        _languageLoading = true;
        try
        {
            LanguageComboBox.Items.Clear();
            var currentCode = LanguageService.ReadLanguagePreference();

            int selectedIndex = 0;
            var languages = LanguageService.SupportedLanguages;
            for (int i = 0; i < languages.Length; i++)
            {
                var lang = languages[i];
                var itemText = lang.Code == "system"
                    ? S.Get(lang.ResourceKey)
                    : lang.DisplayName;

                var cbi = new ComboBoxItem
                {
                    Content = itemText,
                    Tag = lang.Code,
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 12, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };

                LanguageComboBox.Items.Add(cbi);
                if (string.Equals(lang.Code, currentCode, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                }
            }

            LanguageComboBox.SelectedIndex = selectedIndex;
        }
        finally
        {
            _languageLoading = false;
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageLoading) return;

        if (LanguageComboBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string code)
        {
            LanguageService.ApplyLanguage(code, save: true);
        }
        else
        {
            var idx = LanguageComboBox.SelectedIndex;
            var languages = LanguageService.SupportedLanguages;
            if (idx >= 0 && idx < languages.Length)
            {
                LanguageService.ApplyLanguage(languages[idx].Code, save: true);
            }
        }
    }

    private async Task InitializeEnvironmentCheckAsync()
    {
        await Task.Yield();
        var steamPath = SteamDetector.FindSteamPath();
        WizardSteamPathText.Text = !string.IsNullOrEmpty(steamPath) ? steamPath : S.Get("Wizard_Step1_SteamNotFound");

        if (string.IsNullOrEmpty(steamPath))
        {
            WizardSteamIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle24;
            WizardSteamIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xDF, 0x56, 0x48));
        }

        var dllPath = !string.IsNullOrEmpty(steamPath) ? Path.Combine(steamPath, "cloud_redirect.dll") : "";
        bool dllOk = File.Exists(dllPath);
        WizardDllText.Text = dllOk ? S.Get("Wizard_Step1_DllCurrent") : S.Get("Wizard_Step1_DllAutoDeploy");
        WizardDbText.Text = S.Get("Wizard_Step1_DbDesc");

        // Check if already authenticated
        var tokenStatus = OAuthService.CheckTokenStatus(_tokenPath);
        if (!tokenStatus.IsAuthenticated)
        {
            var legacyGoogle = Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            if (File.Exists(legacyGoogle))
            {
                var legacyStatus = OAuthService.CheckTokenStatus(legacyGoogle);
                if (legacyStatus.IsAuthenticated)
                {
                    _tokenPath = legacyGoogle;
                    tokenStatus = legacyStatus;
                }
            }
        }

        if (tokenStatus.IsAuthenticated)
        {
            SetAuthenticatedState(tokenStatus.Message);
        }
    }

    private void SetStep(int step)
    {
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
        var activePillBg = new SolidColorBrush(Color.FromRgb(0x19, 0x2A, 0x3A));
        var activePillBorder = new SolidColorBrush(Color.FromRgb(0x38, 0x77, 0xA8));

        var inactiveBrush = (Brush)FindResource("TextFillColorTertiaryBrush");
        var inactivePillBg = new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x28));
        var inactivePillBorder = new SolidColorBrush(Color.FromRgb(0x25, 0x35, 0x48));

        var doneBrush = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));

        // Step 1 pill
        Step1Pill.Background = step == 1 ? activePillBg : (step > 1 ? activePillBg : inactivePillBg);
        Step1Pill.BorderBrush = step == 1 ? activePillBorder : (step > 1 ? doneBrush : inactivePillBorder);
        Step1Num.Foreground = step > 1 ? doneBrush : (step == 1 ? activeBrush : inactiveBrush);
        Step1Text.Foreground = step > 1 ? doneBrush : (step == 1 ? activeBrush : inactiveBrush);

        // Step 2 pill
        Step2Pill.Background = step == 2 ? activePillBg : (step > 2 ? activePillBg : inactivePillBg);
        Step2Pill.BorderBrush = step == 2 ? activePillBorder : (step > 2 ? doneBrush : inactivePillBorder);
        Step2Num.Foreground = step > 2 ? doneBrush : (step == 2 ? activeBrush : inactiveBrush);
        Step2Text.Foreground = step > 2 ? doneBrush : (step == 2 ? activeBrush : inactiveBrush);

        // Step 3 pill
        Step3Pill.Background = step == 3 ? activePillBg : inactivePillBg;
        Step3Pill.BorderBrush = step == 3 ? activePillBorder : inactivePillBorder;
        Step3Num.Foreground = step == 3 ? activeBrush : inactiveBrush;
        Step3Text.Foreground = step == 3 ? activeBrush : inactiveBrush;

        // Immediately trigger responsive Auto-Fit recalculation for the new step's content height
        Dispatcher.InvokeAsync(() =>
        {
            if (AppSettings.AutoFitZoom)
            {
                UiZoomManager.Instance.SetAutoFit(true);
            }
            UiZoomManager.Instance.RecalculateAutoFitImmediate();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Expander_Toggled(object sender, RoutedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UiZoomManager.Instance.RecalculateAutoFitImmediate();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Step1Next_Click(object sender, RoutedEventArgs e)
    {
        SetStep(2);
    }

    private void Step2Back_Click(object sender, RoutedEventArgs e)
    {
        SetStep(1);
    }

    private void Step2Next_Click(object sender, RoutedEventArgs e)
    {
        SetStep(3);
    }

    private async void WizardSignIn_Click(object sender, RoutedEventArgs e)
    {
        await StartAuthorizationFlowAsync(openBrowser: true);
    }

    private async void WizardPhoneSignIn_Click(object sender, RoutedEventArgs e)
    {
        await StartAuthorizationFlowAsync(openBrowser: false);
    }

    private async Task StartAuthorizationFlowAsync(bool openBrowser)
    {
        if (_isAuthenticating)
        {
            _authCts?.Cancel();
            _oauth?.Dispose();
            ResetAuthState(S.Get("Wizard_Step2_AuthCancelled"));
            return;
        }

        _isAuthenticating = true;
        _authCts = new CancellationTokenSource();
        _oauth = new OAuthService();

        WizardSignInBtn.Content = S.Get("Wizard_Step2_Cancel");
        WizardSignInBtn.Style = (Style)FindResource("SteamDangerButtonStyle");
        WizardPhoneSignInBtn.IsEnabled = false;

        WizardAuthSpinner.Visibility = Visibility.Visible;
        WizardAuthStatusIcon.Visibility = Visibility.Collapsed;
        WizardAuthStatusTitle.Text = openBrowser ? S.Get("Wizard_Step2_OpeningBrowser") : S.Get("Wizard_Step2_WaitingPhone");
        WizardAuthStatusDetail.Text = openBrowser
            ? S.Get("Wizard_Step2_OpeningBrowserDesc")
            : S.Get("Wizard_Step2_WaitingPhoneDesc");

        _oauth.AuthUrlReady += url => Dispatcher.BeginInvoke(() =>
        {
            if (string.IsNullOrEmpty(_oauth?.MobileHelperUrl))
            {
                WizardQrWifiRadio.IsEnabled = false;
                WizardQrDirectRadio.IsChecked = true;
            }
            else
            {
                WizardQrWifiRadio.IsEnabled = true;
                WizardQrWifiRadio.IsChecked = true;
            }
            UpdateQrCode();
            UiZoomManager.Instance.TriggerAutoFitRecalculation();
        });

        try
        {
            bool ok = await _oauth.AuthorizeAsync("gdrive", _tokenPath, msg =>
            {
                Dispatcher.Invoke(() =>
                {
                    WizardAuthStatusDetail.Text = msg;
                });
            }, _authCts.Token);

            if (ok)
            {
                PersistGoogleDriveConfig();
                SetAuthenticatedState(S.Get("Wizard_Step2_AuthSuccess"));
                await Task.Delay(1200);
                SetStep(3);
            }
            else
            {
                ResetAuthState(S.Get("Wizard_Step2_AuthIncomplete"));
            }
        }
        catch (OperationCanceledException)
        {
            ResetAuthState(S.Get("Wizard_Step2_AuthCancelled"));
        }
        catch (Exception ex)
        {
            ResetAuthState(string.Format(S.Get("Wizard_Step2_AuthFailed"), ex.Message));
        }
        finally
        {
            _isAuthenticating = false;
        }
    }

    private void OnZoomChangedHandler(double scale, bool isAutoFit)
    {
        UpdateQrDisplaySize();
    }

    private void UpdateQrDisplaySize()
    {
        if (WizardQrContainer == null || WizardQrCodeImage == null) return;

        double zoom = UiZoomManager.Instance.CurrentScale;
        if (zoom <= 0.1) zoom = 1.0;

        // Base physical size we want on screen: at least 120px in standard, or 170px in large
        bool isLarge = WizardQrSizeLargeRadio?.IsChecked == true;
        bool isDirect = WizardQrDirectRadio?.IsChecked == true;
        
        double targetPhysicalPx = isLarge ? 170.0 : (isDirect ? 130.0 : 120.0);

        // Compensate inversely for low zoom level (e.g., at 60% zoom, 120px / 0.6 = 200px layout size)
        double compScale = 1.0 / zoom;
        double calculatedSize = Math.Round(targetPhysicalPx * compScale);

        // Clamp to sensible range (capped at 190 to guarantee ample width for Column 1 options)
        calculatedSize = Math.Max(110.0, Math.Min(190.0, calculatedSize));

        WizardQrContainer.Width = calculatedSize;
        WizardQrContainer.Height = calculatedSize;
        WizardQrCodeImage.Width = calculatedSize - 12;
        WizardQrCodeImage.Height = calculatedSize - 12;
    }

    private void WizardQrSize_Checked(object sender, RoutedEventArgs e)
    {
        UpdateQrDisplaySize();
        Dispatcher.InvokeAsync(() =>
        {
            UiZoomManager.Instance.RecalculateAutoFitImmediate();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void WizardQrContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        OpenQrZoomDialog();
    }

    private void WizardEnlargeQr_Click(object sender, RoutedEventArgs e)
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

        string modeLabel = WizardQrWifiRadio?.IsChecked == true
            ? S.Get("Wizard_Step2_QrWifi")
            : S.Get("Wizard_Step2_QrDirect");

        var owner = Window.GetWindow(this);
        _qrZoomDialog = new Dialogs.SteamQrZoomDialog(_activeQrPayload, modeLabel, _oauth, owner);
        _qrZoomDialog.Closed += (_, _) => _qrZoomDialog = null;
        _qrZoomDialog.Show();
    }

    private void UpdateQrCode()
    {
        try
        {
            string? payload = null;
            if (WizardQrWifiRadio?.IsChecked == true && !string.IsNullOrEmpty(_oauth?.MobileHelperUrl))
            {
                payload = _oauth.MobileHelperUrl;
            }
            else
            {
                payload = _oauth?.CurrentAuthUrl;
            }

            _activeQrPayload = payload;

            if (!string.IsNullOrEmpty(payload))
            {
                WizardQrCodeImage.Source = Services.QrCodeHelper.GenerateQrCode(payload, 8);
                WizardQrCard.Visibility = Visibility.Visible;
                UpdateQrDisplaySize();
            }
            else
            {
                WizardQrCodeImage.Source = null;
                WizardQrCard.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            App.LogStartup($"Wizard QR generation error: {ex.Message}");
        }
    }

    private void WizardQrMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_isAuthenticating)
        {
            UpdateQrCode();
        }
    }

    private void SetAuthenticatedState(string message)
    {
        if (_qrZoomDialog != null && _qrZoomDialog.IsLoaded)
        {
            _qrZoomDialog.NotifyAuthSuccess();
        }

        WizardSignInBtn.Content = S.Get("Wizard_Step2_SignInAgain");
        WizardSignInBtn.Style = (Style)FindResource("SteamSecondaryButtonStyle");
        WizardPhoneSignInBtn.IsEnabled = true;
        WizardPhoneSignInBtn.Content = S.Get("Wizard_Step2_SignInPhone");
        WizardQrCard.Visibility = Visibility.Collapsed;
        WizardQrCodeImage.Source = null;

        WizardAuthSpinner.Visibility = Visibility.Collapsed;
        WizardAuthStatusIcon.Visibility = Visibility.Visible;
        WizardAuthStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
        WizardAuthStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
        WizardAuthStatusTitle.Text = S.Get("Wizard_Step2_ConnectedTitle");
        WizardAuthStatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
        WizardAuthStatusDetail.Text = message;
        Step2NextBtn.IsEnabled = true;
    }

    private void ResetAuthState(string message)
    {
        if (_qrZoomDialog != null && _qrZoomDialog.IsLoaded)
        {
            _qrZoomDialog.Close();
            _qrZoomDialog = null;
        }

        WizardSignInBtn.Content = S.Get("Wizard_Step2_SignInBrowser");
        WizardSignInBtn.Style = (Style)FindResource("SteamPlayButtonStyle");
        WizardPhoneSignInBtn.IsEnabled = true;
        WizardPhoneSignInBtn.Content = S.Get("Wizard_Step2_SignInPhone");
        WizardQrCard.Visibility = Visibility.Collapsed;
        WizardQrCodeImage.Source = null;

        WizardAuthSpinner.Visibility = Visibility.Collapsed;
        WizardAuthStatusIcon.Visibility = Visibility.Visible;
        WizardAuthStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle24;
        WizardAuthStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xDF, 0x56, 0x48));
        WizardAuthStatusTitle.Text = S.Get("Wizard_Step2_NotConnectedTitle");
        WizardAuthStatusTitle.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
        WizardAuthStatusDetail.Text = message;
    }

    private void WizardCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (_oauth?.CurrentAuthUrl != null)
        {
            Clipboard.SetText(_oauth.CurrentAuthUrl);
            WizardAuthStatusDetail.Text = S.Get("Wizard_Step2_LinkCopied");
        }
        else
        {
            WizardAuthStatusDetail.Text = S.Get("Wizard_Step2_ClickSignInFirst");
        }
    }

    private void WizardSubmitCode_Click(object sender, RoutedEventArgs e)
    {
        var codeOrUrl = WizardManualCodeBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(codeOrUrl))
        {
            WizardAuthStatusDetail.Text = S.Get("Wizard_Step2_PasteFirst");
            return;
        }

        string err = "";
        if (_oauth != null && _oauth.TrySubmitManualCodeOrUrl(codeOrUrl, out err))
        {
            WizardAuthStatusDetail.Text = S.Get("Wizard_Step2_CodeSubmitted");
        }
        else
        {
            WizardAuthStatusDetail.Text = !string.IsNullOrEmpty(err) ? err : S.Get("Wizard_Step2_ClickSignInBeforeSubmit");
        }
    }

    private void WizardBrowseTokenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = S.Get("Wizard_Step2_SelectFileTitle")
        };

        if (dlg.ShowDialog() == true)
        {
            var pickedPath = dlg.FileName;
            var status = OAuthService.CheckTokenStatus(pickedPath);
            if (status.IsAuthenticated)
            {
                try
                {
                    File.Copy(pickedPath, _tokenPath, true);
                    PersistGoogleDriveConfig();
                    SetAuthenticatedState(S.Get("Wizard_Step2_ImportSuccess"));
                    SetStep(3);
                }
                catch (Exception ex)
                {
                    ResetAuthState(string.Format(S.Get("Wizard_Step2_ImportFail"), ex.Message));
                }
            }
            else
            {
                ResetAuthState(string.Format(S.Get("Wizard_Step2_InvalidToken"), status.Message));
            }
        }
    }

    private void PersistGoogleDriveConfig()
    {
        try
        {
            var configPath = SteamDetector.GetConfigFilePath();
            ConfigHelper.SaveConfig(configPath,
                new[] { "provider", "token_path", "auto_update_dll", "sync_luas", "sync_achievements", "sync_playtime" },
                writer =>
                {
                    writer.WriteString("provider", "gdrive");
                    writer.WriteString("token_path", _tokenPath);
                    writer.WriteBoolean("auto_update_dll", true);
                    writer.WriteBoolean("sync_luas", true);
                    writer.WriteBoolean("sync_achievements", true);
                    writer.WriteBoolean("sync_playtime", true);
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config: {ex}");
        }
    }

    private void FinishWizard_Click(object sender, RoutedEventArgs e)
    {
        PersistGoogleDriveConfig();

        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(DashboardPage));
        }
    }
}
