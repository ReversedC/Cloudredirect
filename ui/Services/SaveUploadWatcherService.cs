using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public sealed record SaveUploadEvent(
    string GameName,
    uint AppId,
    string FileName,
    long Bytes,
    bool IsUploading,
    bool IsSuccess,
    DateTime Timestamp);

/// <summary>
/// Monitors cloud_redirect.log for save upload events in real-time,
/// emitting notifications and progress updates for auto-saved game data.
/// </summary>
public static class SaveUploadWatcherService
{
    public static event Action<SaveUploadEvent>? OnSaveActivity;

    private static System.Threading.Timer? _pollTimer;
    private static long _lastLogPosition = 0;
    private static string? _logPath;
    private static readonly object _lock = new();

    private static string? _steamPath;
    private static uint _lastActiveAppId = 0;
    private static string? _lastSaveFileName;

    private static readonly Regex CommittedRegex = new(
        @"\[NS-UP\]\s+committed:\s+(?<file>[^\s]+)\s+\((?<bytes>\d+)\s+bytes\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProviderUploadRegex = new(
        @"\[(?:GDriveProvider|OneDriveProvider|S3Provider|R2Provider|FolderProvider)\]\s+Uploaded\s+(?:(?<acc>\d+)/(?<app>\d+)/)?(?<file>[^\s]+)\s+\((?<bytes>\d+)\s+bytes\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BatchUploadRegex = new(
        @"\[(?:GDriveProvider|OneDriveProvider|S3Provider|R2Provider|FolderProvider)\]\s+UploadBatch:\s+(?<count>\d+)\s+file\(s\)\s+\((?<uploaded>\d+)\s+uploaded",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AppCompleteRegex = new(
        @"\[NS\]\s+CompleteBatch\s+app=(?<app>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AppPublishRegex = new(
        @"\[AppState\]\s+PublishCloudState\s+app\s+(?<app>\d+):\s+published\s+CN=(?<cn>\d+),\s+(?<files>\d+)\s+files",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AppExitSyncDoneRegex = new(
        @"Cloud\.SignalAppExitSyncDone#\d+\s+app=(?<app>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ReleaseSessionRegex = new(
        @"\[AppState\]\s+ReleaseCloudSession\s+app\s+(?<app>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Start()
    {
        lock (_lock)
        {
            if (_pollTimer != null) return;

            _steamPath = SteamDetector.FindSteamPath();
            if (string.IsNullOrEmpty(_steamPath)) return;

            _logPath = Path.Combine(_steamPath, "cloud_redirect.log");
            if (File.Exists(_logPath))
            {
                try
                {
                    // Start from end minus small buffer to capture recent events
                    var info = new FileInfo(_logPath);
                    _lastLogPosition = Math.Max(0, info.Length - 4096);
                }
                catch { }
            }

            _pollTimer = new System.Threading.Timer(_ => CheckLogUpdates(), null, 500, 500);
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }

    public static void TriggerImmediateSync()
    {
        _ = UniversalCloudSyncService.SyncAllProfilesAsync();
    }

    private static void CheckLogUpdates()
    {
        if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath)) return;

        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < _lastLogPosition)
            {
                // File was truncated / recreated
                _lastLogPosition = 0;
            }

            if (fs.Length == _lastLogPosition) return;

            fs.Seek(_lastLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                ParseLogLine(line);
            }

            _lastLogPosition = fs.Position;
        }
        catch { }
    }

    private static void ParseLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // 1. Check for NS-UP committed (game save written and queued for cloud)
        var mCommit = CommittedRegex.Match(line);
        if (mCommit.Success)
        {
            var rawFile = mCommit.Groups["file"].Value;
            long.TryParse(mCommit.Groups["bytes"].Value, out var bytes);

            var friendlyName = CleanSaveName(rawFile);
            _lastSaveFileName = friendlyName;

            var gameTitle = _lastActiveAppId > 0
                ? SteamDetector.GetGameName(_steamPath, _lastActiveAppId)
                : "Steam Game";

            EmitEvent(new SaveUploadEvent(
                GameName: gameTitle,
                AppId: _lastActiveAppId,
                FileName: friendlyName,
                Bytes: bytes,
                IsUploading: true,
                IsSuccess: true,
                Timestamp: DateTime.Now));
            return;
        }

        // 2. Check for Provider file upload
        var mUpload = ProviderUploadRegex.Match(line);
        if (mUpload.Success)
        {
            var rawFile = mUpload.Groups["file"].Value;
            long.TryParse(mUpload.Groups["bytes"].Value, out var bytes);
            uint.TryParse(mUpload.Groups["app"].Value, out var appId);

            if (appId > 0 && appId != 0)
                _lastActiveAppId = appId;

            var activeId = appId > 0 ? appId : _lastActiveAppId;
            var gameTitle = activeId > 0
                ? SteamDetector.GetGameName(_steamPath, activeId)
                : "Steam Game";

            var isMeta = rawFile.EndsWith(".cloudredirect", StringComparison.OrdinalIgnoreCase) ||
                         rawFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

            var friendlyName = isMeta
                ? (_lastSaveFileName ?? "Save Data")
                : CleanSaveName(rawFile);

            if (!isMeta) _lastSaveFileName = friendlyName;

            EmitEvent(new SaveUploadEvent(
                GameName: gameTitle,
                AppId: activeId,
                FileName: friendlyName,
                Bytes: bytes,
                IsUploading: false,
                IsSuccess: true,
                Timestamp: DateTime.Now));
            return;
        }

        // 3. Check for Game Exit & Save Sync Completion
        var mExit = AppExitSyncDoneRegex.Match(line);
        if (mExit.Success)
        {
            if (uint.TryParse(mExit.Groups["app"].Value, out var appId) && appId > 0)
            {
                _lastActiveAppId = appId;
                TriggerAutoSnapshotForApp(appId, "Auto-Backup on Game Exit");
            }
            return;
        }

        var mRelease = ReleaseSessionRegex.Match(line);
        if (mRelease.Success)
        {
            if (uint.TryParse(mRelease.Groups["app"].Value, out var appId) && appId > 0)
            {
                _lastActiveAppId = appId;
                TriggerAutoSnapshotForApp(appId, "Auto-Backup on Game Exit");
            }
            return;
        }

        // 4. Check for PublishCloudState
        var mPublish = AppPublishRegex.Match(line);
        if (mPublish.Success)
        {
            uint.TryParse(mPublish.Groups["app"].Value, out var appId);
            int.TryParse(mPublish.Groups["files"].Value, out var fileCount);

            if (appId > 0) _lastActiveAppId = appId;

            var activeId = appId > 0 ? appId : _lastActiveAppId;
            var gameTitle = activeId > 0
                ? SteamDetector.GetGameName(_steamPath, activeId)
                : "Steam Game";

            if (activeId > 0)
            {
                TriggerAutoSnapshotForApp(activeId, "Cloud Synchronization");
            }

            EmitEvent(new SaveUploadEvent(
                GameName: gameTitle,
                AppId: activeId,
                FileName: $"{fileCount} save file(s)",
                Bytes: 0,
                IsUploading: false,
                IsSuccess: true,
                Timestamp: DateTime.Now));
            return;
        }

        // 5. Check for CompleteBatch
        var mComplete = AppCompleteRegex.Match(line);
        if (mComplete.Success)
        {
            uint.TryParse(mComplete.Groups["app"].Value, out var appId);
            if (appId > 0)
            {
                _lastActiveAppId = appId;
                TriggerAutoSnapshotForApp(appId, "Auto-Save Backup");
            }
            return;
        }

        // 6. Check for Batch upload
        var mBatch = BatchUploadRegex.Match(line);
        if (mBatch.Success)
        {
            int.TryParse(mBatch.Groups["count"].Value, out var count);
            var gameTitle = _lastActiveAppId > 0
                ? SteamDetector.GetGameName(_steamPath, _lastActiveAppId)
                : "Steam Game";

            EmitEvent(new SaveUploadEvent(
                GameName: gameTitle,
                AppId: _lastActiveAppId,
                FileName: $"{count} file(s)",
                Bytes: 0,
                IsUploading: false,
                IsSuccess: true,
                Timestamp: DateTime.Now));
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, DateTime> _lastSnapshotTimes = new();

    public static void TriggerAutoSnapshotForApp(uint appId, string reason)
    {
        if (appId == 0) return;

        var now = DateTime.Now;
        if (_lastSnapshotTimes.TryGetValue(appId, out var lastTime) && (now - lastTime).TotalSeconds < 15)
        {
            // Debounce rapid duplicate events for same app
            return;
        }
        _lastSnapshotTimes[appId] = now;

        Task.Run(async () =>
        {
            try
            {
                // Brief pause to ensure all files in batch are completely flushed to disk by Steam
                await Task.Delay(1500);

                var steamPath = _steamPath ?? SteamDetector.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath)) return;

                var appDir = SaveHistoryManager.FindAppStorageDir(steamPath, appId);
                if (appDir == null || !Directory.Exists(appDir)) return;

                var gameTitle = SteamDetector.GetGameName(steamPath, appId);
                if (string.IsNullOrWhiteSpace(gameTitle))
                    gameTitle = appId.ToString();
                var snapshot = SaveHistoryManager.CreateSnapshot(gameTitle, appDir, reason, appId.ToString());
                if (snapshot != null && AppSettings.ShowSyncNotifications)
                {
                    TrayIconService.Instance.ShowNotification(
                        "CloudRedirect Save Protection",
                        $"{gameTitle}: Save snapshot created ({snapshot.FileCount} file(s), {snapshot.FormattedSize}).");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed auto-snapshot for app {appId}: {ex}");
            }
        });
    }

    private static string CleanSaveName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "save_data";

        // If path like blobs/.../file.json/hash or folder/file.json, pick the filename
        var parts = raw.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var p = parts[i];
            // If it's a 40-char SHA1 hash, take the preceding part
            if (p.Length == 40 && !p.Contains('.'))
                continue;
            return p;
        }
        return Path.GetFileName(raw);
    }

    private static void EmitEvent(SaveUploadEvent ev)
    {
        try
        {
            OnSaveActivity?.Invoke(ev);
        }
        catch { }
    }
}
