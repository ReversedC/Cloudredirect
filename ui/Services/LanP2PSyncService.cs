using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record LanPeer(
    string DeviceId,
    string DeviceName,
    string DeviceType, // "PC", "Steam Deck", "Handheld", "Laptop"
    string IpAddress,
    int Port,
    DateTime LastSeen
)
{
    public string DisplayTitle => $"{DeviceName} ({DeviceType})";
}

/// <summary>
/// Local LAN P2P Quick-Sync Service:
/// Automatic UDP zero-config peer discovery across the local subnet, enabling blazing-fast
/// direct save transfer between PC and Steam Deck / handhelds without cloud upload delays.
/// </summary>
public static class LanP2PSyncService
{
    public const int DiscoveryPort = 38401;
    public const int TransferPort = 38402;

    private static readonly ConcurrentDictionary<string, LanPeer> _peers = new();
    private static UdpClient? _udpClient;
    private static HttpListener? _httpListener;
    private static CancellationTokenSource? _cts;
    private static bool _isRunning;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static event Action? OnPeersChanged;

    public static List<LanPeer> GetActivePeers()
    {
        var now = DateTime.Now;
        // Purge peers not seen in last 25 seconds
        foreach (var p in _peers.Values)
        {
            if ((now - p.LastSeen).TotalSeconds > 25)
                _peers.TryRemove(p.DeviceId, out _);
        }
        return _peers.Values.OrderByDescending(p => p.LastSeen).ToList();
    }

    public static void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();

        StartUdpDiscovery(_cts.Token);
        StartTransferListener(_cts.Token);
    }

    public static void Stop()
    {
        _isRunning = false;
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            _httpListener?.Close();
        }
        catch { }
        finally
        {
            _udpClient = null;
            _httpListener = null;
            _cts = null;
            _peers.Clear();
        }
    }

    private static void StartUdpDiscovery(CancellationToken ct)
    {
        Task.Run(async () =>
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _udpClient.EnableBroadcast = true;

                // Start receiver
                _ = Task.Run(() => ReceiveBeaconsLoopAsync(_udpClient, ct), ct);

                // Start periodic beacon broadcaster (every 5 seconds)
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var beacon = new
                        {
                            deviceId = Environment.MachineName + "_" + TransferPort,
                            deviceName = Environment.MachineName,
                            deviceType = DetectDeviceType(),
                            port = TransferPort,
                            version = AppUpdater.GetCurrentVersionString()
                        };

                        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(beacon));
                        await _udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                    }
                    catch { }

                    await Task.Delay(5000, ct);
                }
            }
            catch { }
        }, ct);
    }

    private static async Task ReceiveBeaconsLoopAsync(UdpClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(ct);
                var text = Encoding.UTF8.GetString(result.Buffer);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                var devId = root.GetProperty("deviceId").GetString() ?? "";
                var devName = root.GetProperty("deviceName").GetString() ?? "";
                var devType = root.GetProperty("deviceType").GetString() ?? "PC";
                var port = root.TryGetProperty("port", out var pProp) ? pProp.GetInt32() : TransferPort;
                var ip = result.RemoteEndPoint.Address.ToString();

                // Ignore our own machine beacon
                if (devId.StartsWith(Environment.MachineName + "_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var peer = new LanPeer(devId, devName, devType, ip, port, DateTime.Now);
                bool updated = _peers.ContainsKey(devId);
                _peers[devId] = peer;

                if (!updated)
                {
                    OnPeersChanged?.Invoke();
                }
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private static void StartTransferListener(CancellationToken ct)
    {
        Task.Run(() =>
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://*:{TransferPort}/");
                _httpListener.Start();

                while (!ct.IsCancellationRequested && _httpListener.IsListening)
                {
                    var ctx = _httpListener.GetContext();
                    _ = Task.Run(() => HandleTransferRequest(ctx));
                }
            }
            catch { }
        }, ct);
    }

    private static void HandleTransferRequest(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

            if (path == "/api/p2p/download")
            {
                var gameId = req.QueryString["game"] ?? "unknown";
                var active = ActiveGameTrackerService.CurrentActiveGame;
                var savePath = active != null ? active.SaveDirectory : null;

                if (!string.IsNullOrEmpty(savePath) && Directory.Exists(savePath))
                {
                    var tempZip = Path.Combine(Path.GetTempPath(), $"p2p_{Guid.NewGuid():N}.zip");
                    ZipFile.CreateFromDirectory(savePath, tempZip, CompressionLevel.Optimal, false);
                    byte[] data = File.ReadAllBytes(tempZip);
                    try { File.Delete(tempZip); } catch { }

                    res.ContentType = "application/zip";
                    res.ContentLength64 = data.Length;
                    res.OutputStream.Write(data, 0, data.Length);
                }
                else
                {
                    res.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            else if (path == "/api/p2p/upload" && req.HttpMethod == "POST")
            {
                var gameId = req.QueryString["game"] ?? "unknown";
                var active = ActiveGameTrackerService.CurrentActiveGame;
                var savePath = active != null ? active.SaveDirectory : null;

                if (!string.IsNullOrEmpty(savePath))
                {
                    var tempZip = Path.Combine(Path.GetTempPath(), $"p2p_in_{Guid.NewGuid():N}.zip");
                    using (var fs = File.Create(tempZip))
                    {
                        req.InputStream.CopyTo(fs);
                    }

                    SaveArchiveService.ImportSaveArchiveAsync(tempZip, savePath, gameId).GetAwaiter().GetResult();
                    try { File.Delete(tempZip); } catch { }

                    byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    res.ContentType = "application/json";
                    res.OutputStream.Write(ok, 0, ok.Length);
                }
                else
                {
                    res.StatusCode = (int)HttpStatusCode.BadRequest;
                }
            }
            else
            {
                res.StatusCode = (int)HttpStatusCode.NotFound;
            }

            res.Close();
        }
        catch { }
    }

    public static async Task<(bool Success, string? Error)> PushSaveToPeerAsync(LanPeer peer, string gameIdentifier, string savePath)
    {
        try
        {
            if (!Directory.Exists(savePath)) return (false, "Source save path not found.");

            var tempZip = Path.Combine(Path.GetTempPath(), $"p2p_send_{Guid.NewGuid():N}.zip");
            await SaveArchiveService.ExportSaveArchiveAsync(gameIdentifier, savePath, tempZip);

            var url = $"http://{peer.IpAddress}:{peer.Port}/api/p2p/upload?game={Uri.EscapeDataString(gameIdentifier)}";
            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(tempZip));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

            var response = await _httpClient.PostAsync(url, content);
            try { File.Delete(tempZip); } catch { }

            if (response.IsSuccessStatusCode)
                return (true, null);

            return (false, $"Peer returned HTTP {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool Success, string? Error)> PullSaveFromPeerAsync(LanPeer peer, string gameIdentifier, string targetSavePath)
    {
        try
        {
            var url = $"http://{peer.IpAddress}:{peer.Port}/api/p2p/download?game={Uri.EscapeDataString(gameIdentifier)}";
            var bytes = await _httpClient.GetByteArrayAsync(url);

            var tempZip = Path.Combine(Path.GetTempPath(), $"p2p_recv_{Guid.NewGuid():N}.zip");
            await File.WriteAllBytesAsync(tempZip, bytes);

            var (ok, err, _) = await SaveArchiveService.ImportSaveArchiveAsync(tempZip, targetSavePath, gameIdentifier);
            try { File.Delete(tempZip); } catch { }

            return (ok, err);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string DetectDeviceType()
    {
        var machine = Environment.MachineName.ToLowerInvariant();
        if (machine.Contains("deck") || machine.Contains("steamdeck")) return "Steam Deck";
        if (machine.Contains("ally") || machine.Contains("legion") || machine.Contains("gpd") || machine.Contains("handheld")) return "Handheld";
        if (machine.Contains("laptop") || machine.Contains("book")) return "Laptop";
        return "PC";
    }
}
