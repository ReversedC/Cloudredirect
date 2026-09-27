using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudRedirect.Resources;

namespace CloudRedirect.Services;

public enum LuaSyncStatus
{
    Synced,
    LocalOnly,
    CloudOnly
}

public class LuaGameItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isLocal;
    private bool _isCloud;
    private string _gameName = "";
    private string? _headerUrl;

    public uint AppId { get; set; }

    public string GameName
    {
        get => _gameName;
        set { _gameName = value; OnPropertyChanged(); }
    }

    public string? HeaderUrl
    {
        get => _headerUrl;
        set { _headerUrl = value; OnPropertyChanged(); }
    }

    public bool IsLocal
    {
        get => _isLocal;
        set { _isLocal = value; OnPropertyChanged(); UpdateStatus(); }
    }

    public bool IsCloud
    {
        get => _isCloud;
        set { _isCloud = value; OnPropertyChanged(); UpdateStatus(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public DateTime? LocalModifiedUtc { get; set; }
    public DateTime? CloudModifiedUtc { get; set; }
    public long LocalSizeBytes { get; set; }
    public long CloudSizeBytes { get; set; }
    public string? CloudFileId { get; set; }

    public LuaSyncStatus Status
    {
        get
        {
            if (IsLocal && IsCloud) return LuaSyncStatus.Synced;
            if (IsLocal) return LuaSyncStatus.LocalOnly;
            return LuaSyncStatus.CloudOnly;
        }
    }

    public string StatusText => Status switch
    {
        LuaSyncStatus.Synced => S.Get("LuaSync_StatusSynced"),
        LuaSyncStatus.LocalOnly => S.Get("LuaSync_StatusLocalOnly"),
        LuaSyncStatus.CloudOnly => S.Get("LuaSync_StatusCloudOnly"),
        _ => ""
    };

    public string StatusBadgeColor => Status switch
    {
        LuaSyncStatus.Synced => "#5C7E10",    // Steam Play Green
        LuaSyncStatus.CloudOnly => "#00D2FF", // Cyan Cloud
        LuaSyncStatus.LocalOnly => "#E5A93C", // Amber Local
        _ => "#8F98A0"
    };

    public string StatusBadgeBg => Status switch
    {
        LuaSyncStatus.Synced => "#1A2B14",
        LuaSyncStatus.CloudOnly => "#132738",
        LuaSyncStatus.LocalOnly => "#2E2413",
        _ => "#171D25"
    };

    private void UpdateStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(StatusBadgeBg));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

/// <summary>
/// Service for listing, encrypting, uploading, restoring, and deleting
/// Steam plugin Lua files (.lua) to and from Cloud storage (Google Drive or Local Sync Folder).
/// </summary>
public static class LuaCloudSyncService
{
    private const string CloudFolderName = "EncryptedLua";

    public static string? GetStPluginDir()
    {
        var steam = SteamDetector.FindSteamPath();
        if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam)) return null;
        var dir = Path.Combine(steam, "config", "stplug-in");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }
        return dir;
    }

    private record CloudFileInfo(string FileId, string FileName, long SizeBytes, DateTime? ModifiedUtc);

    /// <summary>
    /// Loads all Lua games: scanning local stplug-in directory and remote cloud storage.
    /// Resolves Steam game titles and header poster URLs.
    /// </summary>
    public static async Task<List<LuaGameItem>> LoadLuaGamesAsync(CancellationToken ct = default)
    {
        var resultDict = new ConcurrentDictionary<uint, LuaGameItem>();

        // 1. Scan Local Files
        var localDir = GetStPluginDir();
        if (localDir != null && Directory.Exists(localDir))
        {
            var files = Directory.GetFiles(localDir, "*.lua");
            foreach (var f in files)
            {
                var fname = Path.GetFileNameWithoutExtension(f);
                if (uint.TryParse(fname, out var appId))
                {
                    var fi = new FileInfo(f);
                    resultDict[appId] = new LuaGameItem
                    {
                        AppId = appId,
                        IsLocal = true,
                        LocalModifiedUtc = fi.LastWriteTimeUtc,
                        LocalSizeBytes = fi.Length
                    };
                }
            }
        }

        // 2. Scan Cloud Storage
        try
        {
            var cloudFiles = await ListCloudFilesAsync(ct);
            foreach (var kvp in cloudFiles)
            {
                var appId = kvp.Key;
                var cInfo = kvp.Value;

                if (resultDict.TryGetValue(appId, out var existing))
                {
                    existing.IsCloud = true;
                    existing.CloudFileId = cInfo.FileId;
                    existing.CloudSizeBytes = cInfo.SizeBytes;
                    existing.CloudModifiedUtc = cInfo.ModifiedUtc;
                }
                else
                {
                    resultDict[appId] = new LuaGameItem
                    {
                        AppId = appId,
                        IsCloud = true,
                        CloudFileId = cInfo.FileId,
                        CloudSizeBytes = cInfo.SizeBytes,
                        CloudModifiedUtc = cInfo.ModifiedUtc
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LuaCloudSyncService: Failed to list cloud files: {ex.Message}");
        }

        var list = resultDict.Values.OrderBy(x => x.AppId).ToList();

        // 3. Resolve metadata (Game Name and Header Poster) in batch from Steam store client
        var allAppIds = list.Select(x => x.AppId).Distinct().ToList();
        var storeDict = await SteamStoreClient.Shared.GetAppInfoAsync(allAppIds);

        foreach (var item in list)
        {
            if (storeDict.TryGetValue(item.AppId, out var info) && !string.IsNullOrWhiteSpace(info.Name))
            {
                item.GameName = info.Name;
                item.HeaderUrl = info.HeaderUrl;
            }
            else
            {
                item.GameName = $"App {item.AppId}";
                item.HeaderUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{item.AppId}/header.jpg";
            }
        }

        return list.OrderBy(x => x.GameName).ToList();
    }

    private static async Task<Dictionary<uint, CloudFileInfo>> ListCloudFilesAsync(CancellationToken ct)
    {
        var dict = new Dictionary<uint, CloudFileInfo>();
        var config = SteamDetector.ReadConfig();
        if (config == null) return dict;

        if (config.Provider == "gdrive")
        {
            var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (string.IsNullOrEmpty(accessToken)) return dict;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var rootId = await EnsureDriveFolderAsync(http, "CloudRedirect", null);
            if (string.IsNullOrEmpty(rootId)) return dict;

            var luaFolderId = await EnsureDriveFolderAsync(http, CloudFolderName, rootId);
            if (string.IsNullOrEmpty(luaFolderId)) return dict;

            var q = Uri.EscapeDataString($"'{luaFolderId}' in parents and mimeType!='application/vnd.google-apps.folder' and trashed=false");
            var url = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name,size,modifiedTime)&pageSize=1000";

            var resp = await http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files))
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var name = f.GetProperty("name").GetString() ?? "";
                        var id = f.GetProperty("id").GetString() ?? "";
                        long size = 0;
                        if (f.TryGetProperty("size", out var sProp))
                        {
                            long.TryParse(sProp.GetString(), out size);
                        }

                        DateTime? modUtc = null;
                        if (f.TryGetProperty("modifiedTime", out var mProp) &&
                            DateTime.TryParse(mProp.GetString(), out var parsedDate))
                        {
                            modUtc = parsedDate.ToUniversalTime();
                        }

                        // Files named {AppId}.crlua
                        var baseName = Path.GetFileNameWithoutExtension(name);
                        if (uint.TryParse(baseName, out var appId))
                        {
                            dict[appId] = new CloudFileInfo(id, name, size, modUtc);
                        }
                    }
                }
            }
        }
        else if (config.IsFolder || config.IsLocal || !string.IsNullOrEmpty(config.SyncPath))
        {
            var syncPath = config.SyncPath ?? "";
            if (Directory.Exists(syncPath))
            {
                var cloudDir = Path.Combine(syncPath, CloudFolderName);
                if (Directory.Exists(cloudDir))
                {
                    foreach (var f in Directory.GetFiles(cloudDir, "*.crlua"))
                    {
                        var fname = Path.GetFileNameWithoutExtension(f);
                        if (uint.TryParse(fname, out var appId))
                        {
                            var fi = new FileInfo(f);
                            dict[appId] = new CloudFileInfo(f, Path.GetFileName(f), fi.Length, fi.LastWriteTimeUtc);
                        }
                    }
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Encrypts and uploads the selected items to the Cloud provider.
    /// </summary>
    public static async Task<int> BackupLuaFilesAsync(
        IEnumerable<LuaGameItem> items,
        Action<int, int, string>? progress = null,
        CancellationToken ct = default)
    {
        var localDir = GetStPluginDir();
        if (string.IsNullOrEmpty(localDir) || !Directory.Exists(localDir))
            throw new InvalidOperationException("Local stplug-in directory not found.");

        var itemList = items.Where(x => x.IsLocal).ToList();
        if (itemList.Count == 0) return 0;

        var config = SteamDetector.ReadConfig();
        if (config == null)
            throw new InvalidOperationException("No cloud provider configured.");

        int succeeded = 0;
        int total = itemList.Count;

        if (config.Provider == "gdrive")
        {
            var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("Google Drive authentication token expired. Please re-authenticate in Cloud Settings.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var rootId = await EnsureDriveFolderAsync(http, "CloudRedirect", null);
            var luaFolderId = await EnsureDriveFolderAsync(http, CloudFolderName, rootId);
            if (string.IsNullOrEmpty(luaFolderId))
                throw new InvalidOperationException("Failed to access cloud destination folder.");

            // List existing files in destination to avoid duplicates
            var existingFiles = await ListDriveFolderFilesSimpleAsync(http, luaFolderId);

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                var localPath = Path.Combine(localDir, $"{item.AppId}.lua");
                if (!File.Exists(localPath)) continue;

                byte[] plainBytes = await File.ReadAllBytesAsync(localPath, ct);
                byte[] encryptedBytes = LuaCrypto.Encrypt(plainBytes);

                string remoteName = $"{item.AppId}.crlua";

                // If file already exists in cloud, delete old copy before upload
                if (existingFiles.TryGetValue(remoteName, out var existingId))
                {
                    try { await http.DeleteAsync($"https://www.googleapis.com/drive/v3/files/{existingId}", ct); } catch { }
                }

                bool ok = await UploadFileToDriveAsync(http, luaFolderId, remoteName, encryptedBytes);
                if (ok)
                {
                    item.IsCloud = true;
                    item.CloudSizeBytes = encryptedBytes.Length;
                    item.CloudModifiedUtc = DateTime.UtcNow;
                    succeeded++;
                }
            }
        }
        else if (config.IsFolder || config.IsLocal || !string.IsNullOrEmpty(config.SyncPath))
        {
            var syncPath = config.SyncPath ?? "";
            var cloudDir = Path.Combine(syncPath, CloudFolderName);
            if (!Directory.Exists(cloudDir)) Directory.CreateDirectory(cloudDir);

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                var localPath = Path.Combine(localDir, $"{item.AppId}.lua");
                if (!File.Exists(localPath)) continue;

                byte[] plainBytes = await File.ReadAllBytesAsync(localPath, ct);
                byte[] encryptedBytes = LuaCrypto.Encrypt(plainBytes);

                var destPath = Path.Combine(cloudDir, $"{item.AppId}.crlua");
                await File.WriteAllBytesAsync(destPath, encryptedBytes, ct);

                item.IsCloud = true;
                item.CloudFileId = destPath;
                item.CloudSizeBytes = encryptedBytes.Length;
                item.CloudModifiedUtc = DateTime.UtcNow;
                succeeded++;
            }
        }

        return succeeded;
    }

    /// <summary>
    /// Downloads, decrypts, and restores the selected items into local stplug-in directory.
    /// </summary>
    public static async Task<int> RestoreLuaFilesAsync(
        IEnumerable<LuaGameItem> items,
        Action<int, int, string>? progress = null,
        CancellationToken ct = default)
    {
        var localDir = GetStPluginDir();
        if (string.IsNullOrEmpty(localDir))
            throw new InvalidOperationException("Steam directory not found.");

        if (!Directory.Exists(localDir)) Directory.CreateDirectory(localDir);

        var itemList = items.Where(x => x.IsCloud).ToList();
        if (itemList.Count == 0) return 0;

        var config = SteamDetector.ReadConfig();
        if (config == null)
            throw new InvalidOperationException("No cloud provider configured.");

        int succeeded = 0;
        int total = itemList.Count;

        if (config.Provider == "gdrive")
        {
            var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("Google Drive authentication token expired.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                if (string.IsNullOrEmpty(item.CloudFileId)) continue;

                var getUrl = $"https://www.googleapis.com/drive/v3/files/{item.CloudFileId}?alt=media";
                var resp = await http.GetAsync(getUrl, ct);
                if (resp.IsSuccessStatusCode)
                {
                    byte[] encryptedBytes = await resp.Content.ReadAsByteArrayAsync(ct);
                    byte[] plainBytes = LuaCrypto.Decrypt(encryptedBytes);

                    var localPath = Path.Combine(localDir, $"{item.AppId}.lua");
                    await File.WriteAllBytesAsync(localPath, plainBytes, ct);

                    item.IsLocal = true;
                    item.LocalSizeBytes = plainBytes.Length;
                    item.LocalModifiedUtc = DateTime.UtcNow;
                    succeeded++;
                }
            }
        }
        else if (config.IsFolder || config.IsLocal || !string.IsNullOrEmpty(config.SyncPath))
        {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                if (string.IsNullOrEmpty(item.CloudFileId) || !File.Exists(item.CloudFileId))
                    continue;

                byte[] encryptedBytes = await File.ReadAllBytesAsync(item.CloudFileId, ct);
                byte[] plainBytes = LuaCrypto.Decrypt(encryptedBytes);

                var localPath = Path.Combine(localDir, $"{item.AppId}.lua");
                await File.WriteAllBytesAsync(localPath, plainBytes, ct);

                item.IsLocal = true;
                item.LocalSizeBytes = plainBytes.Length;
                item.LocalModifiedUtc = DateTime.UtcNow;
                succeeded++;
            }
        }

        return succeeded;
    }

    /// <summary>
    /// Deletes the selected Lua encrypted backups from the cloud.
    /// </summary>
    public static async Task<int> DeleteCloudLuaFilesAsync(
        IEnumerable<LuaGameItem> items,
        Action<int, int, string>? progress = null,
        CancellationToken ct = default)
    {
        var itemList = items.Where(x => x.IsCloud).ToList();
        if (itemList.Count == 0) return 0;

        var config = SteamDetector.ReadConfig();
        if (config == null) return 0;

        int succeeded = 0;
        int total = itemList.Count;

        if (config.Provider == "gdrive")
        {
            var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("Google Drive authentication token expired.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                if (string.IsNullOrEmpty(item.CloudFileId)) continue;

                var delUrl = $"https://www.googleapis.com/drive/v3/files/{item.CloudFileId}";
                var resp = await http.DeleteAsync(delUrl, ct);
                if (resp.IsSuccessStatusCode)
                {
                    item.IsCloud = false;
                    item.CloudFileId = null;
                    item.CloudSizeBytes = 0;
                    item.CloudModifiedUtc = null;
                    succeeded++;
                }
            }
        }
        else if (config.IsFolder || config.IsLocal || !string.IsNullOrEmpty(config.SyncPath))
        {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                if (!string.IsNullOrEmpty(item.CloudFileId) && File.Exists(item.CloudFileId))
                {
                    File.Delete(item.CloudFileId);
                    item.IsCloud = false;
                    item.CloudFileId = null;
                    item.CloudSizeBytes = 0;
                    item.CloudModifiedUtc = null;
                    succeeded++;
                }
            }
        }

        return succeeded;
    }

    private static async Task<string?> EnsureDriveFolderAsync(HttpClient http, string name, string? parentId)
    {
        try
        {
            var escapedName = name.Replace("'", "\\'");
            var parentClause = string.IsNullOrEmpty(parentId) ? "'root' in parents" : $"'{parentId}' in parents";
            var q = Uri.EscapeDataString($"name='{escapedName}' and mimeType='application/vnd.google-apps.folder' and {parentClause} and trashed=false");
            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name)&pageSize=1";

            var resp = await http.GetAsync(searchUrl);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                {
                    return files[0].GetProperty("id").GetString();
                }
            }

            var createObj = new Dictionary<string, object>
            {
                ["name"] = name,
                ["mimeType"] = "application/vnd.google-apps.folder"
            };
            if (!string.IsNullOrEmpty(parentId))
            {
                createObj["parents"] = new[] { parentId };
            }

            var createContent = new StringContent(JsonSerializer.Serialize(createObj), Encoding.UTF8, "application/json");
            var postResp = await http.PostAsync("https://www.googleapis.com/drive/v3/files?fields=id", createContent);
            if (postResp.IsSuccessStatusCode)
            {
                var postJson = await postResp.Content.ReadAsStringAsync();
                using var postDoc = JsonDocument.Parse(postJson);
                return postDoc.RootElement.GetProperty("id").GetString();
            }
        }
        catch { }
        return null;
    }

    private static async Task<Dictionary<string, string>> ListDriveFolderFilesSimpleAsync(HttpClient http, string folderId)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var q = Uri.EscapeDataString($"'{folderId}' in parents and mimeType!='application/vnd.google-apps.folder' and trashed=false");
            var url = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name)&pageSize=1000";

            var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files))
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var name = f.GetProperty("name").GetString();
                        var id = f.GetProperty("id").GetString();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                        {
                            dict[name] = id;
                        }
                    }
                }
            }
        }
        catch { }
        return dict;
    }

    private static async Task<bool> UploadFileToDriveAsync(HttpClient http, string parentFolderId, string fileName, byte[] content)
    {
        try
        {
            var boundary = "----CloudRedirectUploadBoundary" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartFormDataContent(boundary);

            var metaObj = new
            {
                name = fileName,
                parents = new[] { parentFolderId }
            };
            var metaContent = new StringContent(JsonSerializer.Serialize(metaObj), Encoding.UTF8, "application/json");
            multipart.Add(metaContent);

            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent);

            var url = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
            var resp = await http.PostAsync(url, multipart);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
