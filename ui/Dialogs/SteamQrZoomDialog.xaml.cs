using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CloudRedirect.Resources;
using CloudRedirect.Services;

namespace CloudRedirect.Dialogs;

public partial class SteamQrZoomDialog : Window
{
    private readonly string _payload;
    private readonly OAuthService? _oauth;

    public SteamQrZoomDialog(string payload, string modeLabel, OAuthService? oauth = null, Window? owner = null)
    {
        InitializeComponent();
        _payload = payload;
        _oauth = oauth;

        if (owner != null)
        {
            Owner = owner;
        }

        ModeBadgeText.Text = modeLabel;
        LoadQrImage();
    }

    private void LoadQrImage()
    {
        try
        {
            if (!string.IsNullOrEmpty(_payload))
            {
                // Generate large, high-res crisp QR image (pixelsPerModule: 10)
                QrCodeImage.Source = QrCodeHelper.GenerateQrCode(_payload, 10);
            }
        }
        catch (Exception ex)
        {
            DialogStatusText.Text = $"QR Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Update dialog status if authentication completes or progresses
    /// </summary>
    public void NotifyAuthSuccess()
    {
        Dispatcher.Invoke(async () =>
        {
            DialogSpinner.Visibility = Visibility.Collapsed;
            DialogStatusIcon.Visibility = Visibility.Visible;
            DialogStatusText.Text = S.Get("Wizard_Step2_AuthSuccess");
            await Task.Delay(1200);
            if (IsLoaded)
            {
                Close();
            }
        });
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(_payload))
            {
                Clipboard.SetText(_payload);
                CopyLinkBtn.Content = S.Get("QrDialog_Copied");
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(2000);
                    CopyLinkBtn.Content = S.Get("QrDialog_CopyLink");
                });
            }
        }
        catch (Exception ex)
        {
            App.LogStartup($"Failed to copy QR link: {ex.Message}");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            Close();
        }
    }
}
