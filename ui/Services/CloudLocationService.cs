using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace CloudRedirect.Services;

/// <summary>
/// Service to locate and open game save locations (Google Drive in-app explorer, local storage, userdata).
/// </summary>
public static class CloudLocationService
{
    /// <summary>
    /// Opens the in-app cloud storage explorer for an app without an external browser.
    /// </summary>
    public static async Task OpenCloudLocationAsync(string accountId, string appId, string? displayName = null)
    {
        var title = !string.IsNullOrEmpty(displayName) ? displayName : appId;
        uint.TryParse(appId, out uint parsedAppId);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = new Dialogs.CloudFolderBrowserDialog(title, null, parsedAppId, accountId);
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dlg.Owner = Application.Current.MainWindow;
            }
            dlg.ShowDialog();
        });
    }

    /// <summary>
    /// Opens the external browser directly to Google Drive or cloud provider for this app.
    /// </summary>
    public static async Task OpenCloudLocationInBrowserAsync(string accountId, string appId, string? displayName = null)
    {
        var config = SteamDetector.ReadConfig();

        if (config != null && config.Provider == "gdrive")
        {
            try
            {
                var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
                var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);

                if (!string.IsNullOrEmpty(accessToken))
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    // Search for folder with this appId or game title
                    string query;
                    if (!string.IsNullOrEmpty(displayName) && !string.Equals(displayName, appId, StringComparison.OrdinalIgnoreCase))
                    {
                        var safeName = displayName.Replace("'", "\\'");
                        query = Uri.EscapeDataString($"(name='{appId}' or name='{safeName}') and mimeType='application/vnd.google-apps.folder' and trashed=false");
                    }
                    else
                    {
                        query = Uri.EscapeDataString($"name='{appId}' and mimeType='application/vnd.google-apps.folder' and trashed=false");
                    }

                    var url = $"https://www.googleapis.com/drive/v3/files?q={query}&fields=files(id,name,webViewLink,parents,modifiedTime)&orderBy=modifiedTime desc";

                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                        {
                            var folder = files[0];
                            if (folder.TryGetProperty("webViewLink", out var linkProp))
                            {
                                var link = linkProp.GetString();
                                if (!string.IsNullOrEmpty(link))
                                {
                                    Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
                                    return;
                                }
                            }
                        }
                    }
                }

                // Fallback to Google Drive search for this app or game name
                var searchTerm = !string.IsNullOrEmpty(displayName) ? displayName : appId;
                var searchUrl = $"https://drive.google.com/drive/search?q={Uri.EscapeDataString(searchTerm)}";
                Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true })?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening Google Drive location: {ex}");
                var searchTerm = !string.IsNullOrEmpty(displayName) ? displayName : appId;
                var searchUrl = $"https://drive.google.com/drive/search?q={Uri.EscapeDataString(searchTerm)}";
                Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true })?.Dispose();
                return;
            }
        }

        // Fallback: Open local CloudRedirect storage folder
        OpenLocalStorageFolder(accountId, appId);
    }

    /// <summary>
    /// Opens the local Steam CloudRedirect storage folder for this app in Windows Explorer.
    /// </summary>
    public static void OpenLocalStorageFolder(string accountId, string appId)
    {
        var steamPath = SteamDetector.FindSteamPath();
        if (steamPath != null)
        {
            var appDir = Path.Combine(steamPath, "cloud_redirect", "storage", accountId, appId);
            if (Directory.Exists(appDir))
            {
                Process.Start(new ProcessStartInfo { FileName = appDir, UseShellExecute = true })?.Dispose();
                return;
            }

            var storageRoot = Path.Combine(steamPath, "cloud_redirect", "storage");
            if (Directory.Exists(storageRoot))
            {
                Process.Start(new ProcessStartInfo { FileName = storageRoot, UseShellExecute = true })?.Dispose();
                return;
            }
        }
    }

    /// <summary>
    /// Opens the native Steam userdata folder for this app in Windows Explorer.
    /// </summary>
    public static void OpenSteamUserdataFolder(string accountId, string appId)
    {
        var steamPath = SteamDetector.FindSteamPath();
        if (steamPath != null)
        {
            var userDir = Path.Combine(steamPath, "userdata", accountId, appId);
            if (Directory.Exists(userDir))
            {
                Process.Start(new ProcessStartInfo { FileName = userDir, UseShellExecute = true })?.Dispose();
                return;
            }

            var userRoot = Path.Combine(steamPath, "userdata", accountId);
            if (Directory.Exists(userRoot))
            {
                Process.Start(new ProcessStartInfo { FileName = userRoot, UseShellExecute = true })?.Dispose();
                return;
            }
        }
    }
}
