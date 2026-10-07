using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    private bool _isEncrypted;
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

    public bool IsEncrypted
    {
        get => _isEncrypted;
        set { _isEncrypted = value; OnPropertyChanged(); }
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
/// Steam plugin Lua files (.lua) to and from Cloud storage (Google Drive, Local Sync Folder,
/// or existing cloud_redirect Lua archives).
/// </summary>
public static class LuaCloudSyncService
{
    private const string CloudFolderName = "EncryptedLua";

    public static int CachedCloudGamesCount { get; set; }

    internal static string ResolveTokenPath(CloudConfig config)
    {
        if (!string.IsNullOrEmpty(config.TokenPath) && File.Exists(config.TokenPath))
            return config.TokenPath;
        var configDir = SteamDetector.GetConfigDir();
        var p1 = Path.Combine(configDir, "google_tokens.json");
        if (File.Exists(p1)) return p1;
        var p2 = Path.Combine(configDir, "gdrive_tokens.json");
        if (File.Exists(p2)) return p2;
        return config.TokenPath ?? p1;
    }

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
    /// Loads all Lua games: scanning local stplug-in directory, local cloud storage cache (LuaManifest.json / LuaArchive.zip),
    /// .sync_state, and remote cloud storage (Google Drive / Folder).
    /// Resolves Steam game titles and header poster URLs.
    /// </summary>
    public static async Task<List<LuaGameItem>> LoadLuaGamesAsync(CancellationToken ct = default)
    {
        var resultDict = new ConcurrentDictionary<uint, LuaGameItem>();
        var steam = SteamDetector.FindSteamPath();

        // 1. Scan Local Files in config/stplug-in/*.lua
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

        // 2. Scan Existing Cloud Storage Cache (cloud_redirect/storage/*/0/LuaManifest.json & LuaArchive.zip)
        if (!string.IsNullOrEmpty(steam) && Directory.Exists(steam))
        {
            var storageDir = Path.Combine(steam, "cloud_redirect", "storage");
            if (Directory.Exists(storageDir))
            {
                foreach (var accountDir in Directory.GetDirectories(storageDir))
                {
                    var zeroDir = Path.Combine(accountDir, "0");
                    var manifestPath = Path.Combine(zeroDir, "LuaManifest.json");
                    var zipPath = Path.Combine(zeroDir, "LuaArchive.zip");

                    if (File.Exists(manifestPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(manifestPath);
                            using var doc = JsonDocument.Parse(json);
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                bool isDel = prop.Value.TryGetProperty("del", out var d) && d.GetInt64() > 0;
                                if (isDel) continue;

                                var fname = prop.Name;
                                if (uint.TryParse(Path.GetFileNameWithoutExtension(fname), out var appId))
                                {
                                    long size = prop.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                                    long mod = prop.Value.TryGetProperty("mod", out var m) ? m.GetInt64() : 0;
                                    var modUtc = mod > 0 ? DateTimeOffset.FromUnixTimeSeconds(mod).UtcDateTime : (DateTime?)null;

                                    if (resultDict.TryGetValue(appId, out var existing))
                                    {
                                        existing.IsCloud = true;
                                        if (existing.CloudSizeBytes == 0) existing.CloudSizeBytes = size;
                                        if (existing.CloudModifiedUtc == null) existing.CloudModifiedUtc = modUtc;
                                        existing.CloudFileId ??= $"archive:{zipPath}:{fname}";
                                    }
                                    else
                                    {
                                        resultDict[appId] = new LuaGameItem
                                        {
                                            AppId = appId,
                                            IsCloud = true,
                                            CloudSizeBytes = size,
                                            CloudModifiedUtc = modUtc,
                                            CloudFileId = $"archive:{zipPath}:{fname}"
                                        };
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    else if (File.Exists(zipPath))
                    {
                        try
                        {
                            using var zip = ZipFile.OpenRead(zipPath);
                            foreach (var entry in zip.Entries)
                            {
                                if (uint.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out var appId))
                                {
                                    if (resultDict.TryGetValue(appId, out var existing))
                                    {
                                        existing.IsCloud = true;
                                        if (existing.CloudSizeBytes == 0) existing.CloudSizeBytes = entry.Length;
                                        if (existing.CloudModifiedUtc == null) existing.CloudModifiedUtc = entry.LastWriteTime.UtcDateTime;
                                        existing.CloudFileId ??= $"archive:{zipPath}:{entry.Name}";
                                    }
                                    else
                                    {
                                        resultDict[appId] = new LuaGameItem
                                        {
                                            AppId = appId,
                                            IsCloud = true,
                                            CloudSizeBytes = entry.Length,
                                            CloudModifiedUtc = entry.LastWriteTime.UtcDateTime,
                                            CloudFileId = $"archive:{zipPath}:{entry.Name}"
                                        };
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // Also check .sync_state in config/stplug-in/.sync_state
            var syncStatePath = Path.Combine(steam, "config", "stplug-in", ".sync_state");
            if (File.Exists(syncStatePath))
            {
                try
                {
                    var lines = File.ReadAllLines(syncStatePath);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (uint.TryParse(Path.GetFileNameWithoutExtension(line), out var appId))
                        {
                            if (resultDict.TryGetValue(appId, out var existing))
                            {
                                existing.IsCloud = true;
                            }
                            else
                            {
                                resultDict[appId] = new LuaGameItem
                                {
                                    AppId = appId,
                                    IsCloud = true
                                };
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // 3. Scan Remote Cloud Storage (EncryptedLua in Google Drive or Sync Folder)
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
                    existing.IsEncrypted = true;
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
                        IsEncrypted = true,
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

        // 4. Resolve metadata (Game Name and Header Poster) in batch from Steam Store & local manifests
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
                // Fallback to local Steam appmanifest name
                var localName = SteamDetector.GetGameName(steam, item.AppId);
                item.GameName = !string.IsNullOrWhiteSpace(localName) ? localName : $"App {item.AppId}";
                item.HeaderUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{item.AppId}/header.jpg";
            }
        }

        var sorted = list.OrderBy(x => x.GameName).ToList();
        CachedCloudGamesCount = sorted.Count(x => x.IsCloud);
        return sorted;
    }

    private static async Task<Dictionary<uint, CloudFileInfo>> ListCloudFilesAsync(CancellationToken ct)
    {
        var dict = new Dictionary<uint, CloudFileInfo>();
        var config = SteamDetector.ReadConfig();
        if (config == null) return dict;

        // 1. Google Drive Cloud Storage Detection
        if (config.Provider == "gdrive")
        {
            var tokenPath = ResolveTokenPath(config);
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (!string.IsNullOrEmpty(accessToken))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // A. Check EncryptedLua folder for {AppId}.crlua
                try
                {
                    var rootId = await EnsureDriveFolderAsync(http, "CloudRedirect", null);
                    string? luaFolderId = null;
                    if (!string.IsNullOrEmpty(rootId))
                    {
                        luaFolderId = await EnsureDriveFolderAsync(http, CloudFolderName, rootId);
                    }

                    if (!string.IsNullOrEmpty(luaFolderId))
                    {
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
                                        long.TryParse(sProp.GetString(), out size);

                                    DateTime? modUtc = null;
                                    if (f.TryGetProperty("modifiedTime", out var mProp) &&
                                        DateTime.TryParse(mProp.GetString(), out var parsedDate))
                                        modUtc = parsedDate.ToUniversalTime();

                                    var baseName = Path.GetFileNameWithoutExtension(name);
                                    if (uint.TryParse(baseName, out var appId))
                                    {
                                        dict[appId] = new CloudFileInfo(id, name, size, modUtc);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // B. Check for LuaManifest.json and LuaArchive.zip anywhere on Google Drive
                try
                {
                    string? zipFileId = null;
                    DateTime? zipModUtc = null;
                    long zipSize = 0;

                    // Query for LuaArchive.zip
                    var qZip = Uri.EscapeDataString("name='LuaArchive.zip' and trashed=false");
                    var zipUrl = $"https://www.googleapis.com/drive/v3/files?q={qZip}&fields=files(id,name,size,modifiedTime)&pageSize=10";
                    var zipResp = await http.GetAsync(zipUrl, ct);
                    if (zipResp.IsSuccessStatusCode)
                    {
                        var zipJson = await zipResp.Content.ReadAsStringAsync(ct);
                        using var zipDoc = JsonDocument.Parse(zipJson);
                        if (zipDoc.RootElement.TryGetProperty("files", out var zFiles) && zFiles.GetArrayLength() > 0)
                        {
                            var firstZip = zFiles[0];
                            zipFileId = firstZip.GetProperty("id").GetString();
                            if (firstZip.TryGetProperty("size", out var zs)) long.TryParse(zs.GetString(), out zipSize);
                            if (firstZip.TryGetProperty("modifiedTime", out var zm) && DateTime.TryParse(zm.GetString(), out var zParsed))
                                zipModUtc = zParsed.ToUniversalTime();
                        }
                    }

                    // Query for LuaManifest.json
                    var qManifest = Uri.EscapeDataString("name='LuaManifest.json' and trashed=false");
                    var mUrl = $"https://www.googleapis.com/drive/v3/files?q={qManifest}&fields=files(id,name,size,modifiedTime)&pageSize=10";
                    var mResp = await http.GetAsync(mUrl, ct);
                    bool manifestFound = false;
                    if (mResp.IsSuccessStatusCode)
                    {
                        var mJson = await mResp.Content.ReadAsStringAsync(ct);
                        using var mDoc = JsonDocument.Parse(mJson);
                        if (mDoc.RootElement.TryGetProperty("files", out var mFiles))
                        {
                            foreach (var mf in mFiles.EnumerateArray())
                            {
                                var manifestId = mf.GetProperty("id").GetString();
                                if (string.IsNullOrEmpty(manifestId)) continue;

                                var contentResp = await http.GetAsync($"https://www.googleapis.com/drive/v3/files/{manifestId}?alt=media", ct);
                                if (contentResp.IsSuccessStatusCode)
                                {
                                    var contentJson = await contentResp.Content.ReadAsStringAsync(ct);
                                    using var manifestDoc = JsonDocument.Parse(contentJson);
                                    foreach (var prop in manifestDoc.RootElement.EnumerateObject())
                                    {
                                        bool isDel = prop.Value.TryGetProperty("del", out var d) && d.GetInt64() > 0;
                                        if (isDel) continue;

                                        var fname = prop.Name;
                                        if (uint.TryParse(Path.GetFileNameWithoutExtension(fname), out var appId))
                                        {
                                            long size = prop.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                                            long mod = prop.Value.TryGetProperty("mod", out var m) ? m.GetInt64() : 0;
                                            var modUtc = mod > 0 ? DateTimeOffset.FromUnixTimeSeconds(mod).UtcDateTime : (DateTime?)null;

                                            string fileId = !string.IsNullOrEmpty(zipFileId)
                                                ? $"gdrive_archive:{zipFileId}:{fname}"
                                                : $"gdrive_manifest:{manifestId}:{fname}";

                                            if (!dict.ContainsKey(appId))
                                            {
                                                dict[appId] = new CloudFileInfo(fileId, fname, size, modUtc);
                                                manifestFound = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // If we found LuaArchive.zip on Google Drive but no manifest entries were discovered
                    if (!manifestFound && !string.IsNullOrEmpty(zipFileId))
                    {
                        var steam = SteamDetector.FindSteamPath();
                        var cacheDir = Path.Combine(steam ?? Path.GetTempPath(), "cloud_redirect", "storage", "default", "0");
                        Directory.CreateDirectory(cacheDir);
                        var cacheZip = Path.Combine(cacheDir, "LuaArchive.zip");

                        if (!File.Exists(cacheZip) || new FileInfo(cacheZip).Length == 0)
                        {
                            var bytes = await http.GetByteArrayAsync($"https://www.googleapis.com/drive/v3/files/{zipFileId}?alt=media", ct);
                            await File.WriteAllBytesAsync(cacheZip, bytes, ct);
                        }

                        if (File.Exists(cacheZip))
                        {
                            using var zip = ZipFile.OpenRead(cacheZip);
                            foreach (var entry in zip.Entries)
                            {
                                if (uint.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out var appId))
                                {
                                    if (!dict.ContainsKey(appId))
                                    {
                                        dict[appId] = new CloudFileInfo(
                                            $"gdrive_archive:{zipFileId}:{entry.Name}",
                                            entry.Name,
                                            entry.Length,
                                            entry.LastWriteTime.UtcDateTime);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // 2. Local Sync Folder / Syncthing / OneDrive Detection
        if (!string.IsNullOrEmpty(config.SyncPath) && Directory.Exists(config.SyncPath))
        {
            try
            {
                // A. Check EncryptedLua/*.crlua in sync folder
                var syncLuaDir = Path.Combine(config.SyncPath, CloudFolderName);
                if (Directory.Exists(syncLuaDir))
                {
                    foreach (var crlua in Directory.GetFiles(syncLuaDir, "*.crlua"))
                    {
                        var baseName = Path.GetFileNameWithoutExtension(crlua);
                        if (uint.TryParse(baseName, out var appId))
                        {
                            var fi = new FileInfo(crlua);
                            if (!dict.ContainsKey(appId))
                            {
                                dict[appId] = new CloudFileInfo(crlua, Path.GetFileName(crlua), fi.Length, fi.LastWriteTimeUtc);
                            }
                        }
                    }
                }

                // B. Check LuaArchive.zip in sync folder
                var syncZips = Directory.GetFiles(config.SyncPath, "LuaArchive.zip", SearchOption.AllDirectories);
                foreach (var zPath in syncZips)
                {
                    try
                    {
                        using var zip = ZipFile.OpenRead(zPath);
                        foreach (var entry in zip.Entries)
                        {
                            if (uint.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out var appId))
                            {
                                if (!dict.ContainsKey(appId))
                                {
                                    dict[appId] = new CloudFileInfo(
                                        $"archive:{zPath}:{entry.Name}",
                                        entry.Name,
                                        entry.Length,
                                        entry.LastWriteTime.UtcDateTime);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return dict;
    }

    /// <summary>
    /// Encrypts and uploads the selected items to the Cloud provider (AES-256)
    /// and updates the local Lua storage archive and manifest.
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
        int succeeded = 0;
        int total = itemList.Count;

        if (config?.Provider == "gdrive")
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

                if (existingFiles.TryGetValue(remoteName, out var existingId))
                {
                    try { await http.DeleteAsync($"https://www.googleapis.com/drive/v3/files/{existingId}", ct); } catch { }
                }

                bool ok = await UploadFileToDriveAsync(http, luaFolderId, remoteName, encryptedBytes);
                if (ok)
                {
                    item.IsCloud = true;
                    item.IsEncrypted = true;
                    item.CloudSizeBytes = encryptedBytes.Length;
                    item.CloudModifiedUtc = DateTime.UtcNow;
                    succeeded++;
                }
            }
        }

        // Also save to Sync Folder if configured
        if (!string.IsNullOrEmpty(config?.SyncPath) && Directory.Exists(config.SyncPath))
        {
            try
            {
                var syncLuaDir = Path.Combine(config.SyncPath, CloudFolderName);
                Directory.CreateDirectory(syncLuaDir);

                for (int i = 0; i < total; i++)
                {
                    var item = itemList[i];
                    var localPath = Path.Combine(localDir, $"{item.AppId}.lua");
                    if (!File.Exists(localPath)) continue;

                    byte[] plainBytes = await File.ReadAllBytesAsync(localPath, ct);
                    byte[] encryptedBytes = LuaCrypto.Encrypt(plainBytes);
                    var dest = Path.Combine(syncLuaDir, $"{item.AppId}.crlua");
                    await File.WriteAllBytesAsync(dest, encryptedBytes, ct);

                    item.IsCloud = true;
                    item.IsEncrypted = true;
                    item.CloudSizeBytes = encryptedBytes.Length;
                    item.CloudModifiedUtc = DateTime.UtcNow;
                    item.CloudFileId ??= dest;
                    if (config.Provider != "gdrive") succeeded++;
                }
            }
            catch { }
        }

        // Also update local LuaArchive.zip and LuaManifest.json so C++ core & Dashboard sync stays up-to-date
        var steam = SteamDetector.FindSteamPath();
        if (!string.IsNullOrEmpty(steam) && Directory.Exists(steam))
        {
            try { LuaSyncHelper.ManualBackup(steam); }
            catch { }
        }

        return succeeded;
    }

    /// <summary>
    /// Downloads, decrypts, and restores the selected items into local stplug-in directory.
    /// Supports both new AES-256 .crlua files and existing LuaArchive.zip packages.
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
        var steam = SteamDetector.FindSteamPath();
        int succeeded = 0;
        int total = itemList.Count;

        HttpClient? http = null;
        if (config?.Provider == "gdrive")
        {
            var tokenPath = ResolveTokenPath(config);
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (!string.IsNullOrEmpty(accessToken))
            {
                http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        try
        {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                var localPath = Path.Combine(localDir, $"{item.AppId}.lua");
                bool restored = false;

                // Case 1: Item is in a local or synced zip archive (archive:path:name)
                if (!string.IsNullOrEmpty(item.CloudFileId) && item.CloudFileId.StartsWith("archive:"))
                {
                    var raw = item.CloudFileId.Substring("archive:".Length);
                    var splitIdx = raw.LastIndexOf(':');
                    string zipPath = splitIdx > 0 ? raw.Substring(0, splitIdx) : raw;
                    string entryName = splitIdx > 0 ? raw.Substring(splitIdx + 1) : $"{item.AppId}.lua";

                    if (File.Exists(zipPath))
                    {
                        try
                        {
                            using var zip = ZipFile.OpenRead(zipPath);
                            var entry = zip.GetEntry(entryName) ?? zip.GetEntry($"{item.AppId}.lua");
                            if (entry != null)
                            {
                                entry.ExtractToFile(localPath, overwrite: true);
                                restored = true;
                            }
                        }
                        catch { }
                    }
                }

                // Case 1b: Item is in a Google Drive zip archive (gdrive_archive:zipFileId:entryName)
                if (!restored && http != null && !string.IsNullOrEmpty(item.CloudFileId) && item.CloudFileId.StartsWith("gdrive_archive:"))
                {
                    var raw = item.CloudFileId.Substring("gdrive_archive:".Length);
                    var splitIdx = raw.LastIndexOf(':');
                    string zipFileId = splitIdx > 0 ? raw.Substring(0, splitIdx) : raw;
                    string entryName = splitIdx > 0 ? raw.Substring(splitIdx + 1) : $"{item.AppId}.lua";

                    string cachedZipDir = Path.Combine(steam ?? SteamDetector.FindSteamPath() ?? Path.GetTempPath(), "cloud_redirect", "storage", "default", "0");
                    Directory.CreateDirectory(cachedZipDir);
                    string cachedZipPath = Path.Combine(cachedZipDir, "LuaArchive.zip");

                    if (!File.Exists(cachedZipPath) || new FileInfo(cachedZipPath).Length == 0)
                    {
                        try
                        {
                            var zipBytes = await http.GetByteArrayAsync($"https://www.googleapis.com/drive/v3/files/{zipFileId}?alt=media", ct);
                            await File.WriteAllBytesAsync(cachedZipPath, zipBytes, ct);
                        }
                        catch { }
                    }

                    if (File.Exists(cachedZipPath))
                    {
                        try
                        {
                            using var zip = ZipFile.OpenRead(cachedZipPath);
                            var entry = zip.GetEntry(entryName) ?? zip.GetEntry($"{item.AppId}.lua");
                            if (entry != null)
                            {
                                entry.ExtractToFile(localPath, overwrite: true);
                                restored = true;
                            }
                        }
                        catch { }
                    }
                }

                // Case 1c: Item is from Google Drive manifest without direct zip id (gdrive_manifest:manifestId:entryName)
                if (!restored && http != null && !string.IsNullOrEmpty(item.CloudFileId) && item.CloudFileId.StartsWith("gdrive_manifest:"))
                {
                    var raw = item.CloudFileId.Substring("gdrive_manifest:".Length);
                    var splitIdx = raw.LastIndexOf(':');
                    string entryName = splitIdx > 0 ? raw.Substring(splitIdx + 1) : $"{item.AppId}.lua";

                    string cachedZipDir = Path.Combine(steam ?? SteamDetector.FindSteamPath() ?? Path.GetTempPath(), "cloud_redirect", "storage", "default", "0");
                    Directory.CreateDirectory(cachedZipDir);
                    string cachedZipPath = Path.Combine(cachedZipDir, "LuaArchive.zip");

                    if (!File.Exists(cachedZipPath) || new FileInfo(cachedZipPath).Length == 0)
                    {
                        try
                        {
                            var qZip = Uri.EscapeDataString("name='LuaArchive.zip' and trashed=false");
                            var zipUrl = $"https://www.googleapis.com/drive/v3/files?q={qZip}&fields=files(id)&pageSize=1";
                            var zResp = await http.GetAsync(zipUrl, ct);
                            if (zResp.IsSuccessStatusCode)
                            {
                                var zJson = await zResp.Content.ReadAsStringAsync(ct);
                                using var zDoc = JsonDocument.Parse(zJson);
                                if (zDoc.RootElement.TryGetProperty("files", out var zf) && zf.GetArrayLength() > 0)
                                {
                                    var zId = zf[0].GetProperty("id").GetString();
                                    if (!string.IsNullOrEmpty(zId))
                                    {
                                        var zipBytes = await http.GetByteArrayAsync($"https://www.googleapis.com/drive/v3/files/{zId}?alt=media", ct);
                                        await File.WriteAllBytesAsync(cachedZipPath, zipBytes, ct);
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (File.Exists(cachedZipPath))
                    {
                        try
                        {
                            using var zip = ZipFile.OpenRead(cachedZipPath);
                            var entry = zip.GetEntry(entryName) ?? zip.GetEntry($"{item.AppId}.lua");
                            if (entry != null)
                            {
                                entry.ExtractToFile(localPath, overwrite: true);
                                restored = true;
                            }
                        }
                        catch { }
                    }
                }

                // Case 2: Item has a Google Drive File ID for encrypted .crlua
                if (!restored && http != null && !string.IsNullOrEmpty(item.CloudFileId) && !item.CloudFileId.Contains(Path.DirectorySeparatorChar) && !item.CloudFileId.StartsWith("archive:") && !item.CloudFileId.StartsWith("gdrive_"))
                {
                    try
                    {
                        var getUrl = $"https://www.googleapis.com/drive/v3/files/{item.CloudFileId}?alt=media";
                        var resp = await http.GetAsync(getUrl, ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            byte[] encryptedBytes = await resp.Content.ReadAsByteArrayAsync(ct);
                            byte[] plainBytes = LuaCrypto.Decrypt(encryptedBytes);
                            await File.WriteAllBytesAsync(localPath, plainBytes, ct);
                            restored = true;
                        }
                    }
                    catch { }
                }

                // Case 3: Item is a local file path to .crlua
                if (!restored && !string.IsNullOrEmpty(item.CloudFileId) && File.Exists(item.CloudFileId) && item.CloudFileId.EndsWith(".crlua", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] encryptedBytes = await File.ReadAllBytesAsync(item.CloudFileId, ct);
                        byte[] plainBytes = LuaCrypto.Decrypt(encryptedBytes);
                        await File.WriteAllBytesAsync(localPath, plainBytes, ct);
                        restored = true;
                    }
                    catch { }
                }

                // Case 4: Fallback to searching any LuaArchive.zip in storage or sync folder
                if (!restored)
                {
                    var searchDirs = new List<string>();
                    if (!string.IsNullOrEmpty(steam)) searchDirs.Add(Path.Combine(steam, "cloud_redirect", "storage"));
                    if (!string.IsNullOrEmpty(config?.SyncPath) && Directory.Exists(config.SyncPath)) searchDirs.Add(config.SyncPath);

                    foreach (var sDir in searchDirs)
                    {
                        if (!Directory.Exists(sDir)) continue;
                        var zips = Directory.GetFiles(sDir, "LuaArchive.zip", SearchOption.AllDirectories);
                        foreach (var z in zips)
                        {
                            try
                            {
                                using var zip = ZipFile.OpenRead(z);
                                var entry = zip.GetEntry($"{item.AppId}.lua");
                                if (entry != null)
                                {
                                    entry.ExtractToFile(localPath, overwrite: true);
                                    restored = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                        if (restored) break;
                    }
                }

                if (restored)
                {
                    var fi = new FileInfo(localPath);
                    item.IsLocal = true;
                    item.LocalSizeBytes = fi.Exists ? fi.Length : 0;
                    item.LocalModifiedUtc = fi.Exists ? fi.LastWriteTimeUtc : DateTime.UtcNow;
                    succeeded++;
                }
            }

            // Keep .sync_state in sync after restoring
            if (succeeded > 0 && !string.IsNullOrEmpty(localDir) && Directory.Exists(localDir))
            {
                try
                {
                    var syncStatePath = Path.Combine(localDir, ".sync_state");
                    var currentLuas = Directory.GetFiles(localDir, "*.lua").Select(Path.GetFileName).Where(f => !string.IsNullOrEmpty(f)).ToList();
                    var lines = new List<string> { DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() };
                    lines.AddRange(currentLuas!);
                    File.WriteAllLines(syncStatePath, lines);
                }
                catch { }
            }
        }
        finally
        {
            http?.Dispose();
        }

        return succeeded;
    }

    /// <summary>
    /// Deletes the selected Lua backups from the cloud (Google Drive, folder, and local storage archives).
    /// </summary>
    public static async Task<int> DeleteCloudLuaFilesAsync(
        IEnumerable<LuaGameItem> items,
        Action<int, int, string>? progress = null,
        CancellationToken ct = default)
    {
        var itemList = items.Where(x => x.IsCloud).ToList();
        if (itemList.Count == 0) return 0;

        var config = SteamDetector.ReadConfig();
        var steam = SteamDetector.FindSteamPath();
        int succeeded = 0;
        int total = itemList.Count;

        HttpClient? http = null;
        if (config?.Provider == "gdrive")
        {
            var tokenPath = ResolveTokenPath(config);
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (!string.IsNullOrEmpty(accessToken))
            {
                http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        try
        {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemList[i];
                progress?.Invoke(i + 1, total, item.GameName);

                bool deleted = false;

                // 1. Delete Google Drive encrypted file if present
                if (http != null && !string.IsNullOrEmpty(item.CloudFileId) && !item.CloudFileId.Contains(Path.DirectorySeparatorChar) && !item.CloudFileId.StartsWith("archive:"))
                {
                    try
                    {
                        var delUrl = $"https://www.googleapis.com/drive/v3/files/{item.CloudFileId}";
                        var resp = await http.DeleteAsync(delUrl, ct);
                        if (resp.IsSuccessStatusCode) deleted = true;
                    }
                    catch { }
                }

                // 2. Delete local encrypted .crlua file if present
                if (!string.IsNullOrEmpty(item.CloudFileId) && File.Exists(item.CloudFileId) && item.CloudFileId.EndsWith(".crlua", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(item.CloudFileId);
                        deleted = true;
                    }
                    catch { }
                }

                // 3. Remove entry from LuaArchive.zip and LuaManifest.json in cloud_redirect/storage
                if (!string.IsNullOrEmpty(steam) && Directory.Exists(steam))
                {
                    var storageBase = Path.Combine(steam, "cloud_redirect", "storage");
                    if (Directory.Exists(storageBase))
                    {
                        foreach (var acct in Directory.GetDirectories(storageBase))
                        {
                            var zeroDir = Path.Combine(acct, "0");
                            var zipPath = Path.Combine(zeroDir, "LuaArchive.zip");
                            var manifestPath = Path.Combine(zeroDir, "LuaManifest.json");

                            if (File.Exists(zipPath))
                            {
                                try
                                {
                                    using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
                                    var entry = zip.GetEntry($"{item.AppId}.lua");
                                    if (entry != null)
                                    {
                                        entry.Delete();
                                        deleted = true;
                                    }
                                }
                                catch { }
                            }

                            if (File.Exists(manifestPath))
                            {
                                try
                                {
                                    var json = File.ReadAllText(manifestPath);
                                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                                    if (dict != null && dict.Remove($"{item.AppId}.lua"))
                                    {
                                        File.WriteAllText(manifestPath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                                        deleted = true;
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // Remove from .sync_state
                    var syncStatePath = Path.Combine(steam, "config", "stplug-in", ".sync_state");
                    if (File.Exists(syncStatePath))
                    {
                        try
                        {
                            var lines = File.ReadAllLines(syncStatePath);
                            var newLines = lines.Where(l => !string.Equals(l.Trim(), $"{item.AppId}.lua", StringComparison.OrdinalIgnoreCase)).ToList();
                            if (newLines.Count < lines.Length)
                            {
                                File.WriteAllLines(syncStatePath, newLines);
                                deleted = true;
                            }
                        }
                        catch { }
                    }
                }


                if (deleted || !string.IsNullOrEmpty(item.CloudFileId))
                {
                    item.IsCloud = false;
                    item.IsEncrypted = false;
                    item.CloudFileId = null;
                    item.CloudSizeBytes = 0;
                    item.CloudModifiedUtc = null;
                    succeeded++;
                }
            }
        }
        finally
        {
            http?.Dispose();
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
            var boundary = "cr_boundary_" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartContent("related", boundary);

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
