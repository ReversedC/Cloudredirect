using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record RemoteGameItem(
    string AppId,
    string Name,
    string? HeaderUrl,
    bool IsRunning,
    bool HasCloudSave,
    string Source // "Steam" or "Universal"
);

/// <summary>
/// High-throughput LAN and WAN host server for SUO Link.
/// Manages streaming, input relay, GameHub mobile interface, and UDP discovery beacons.
/// </summary>
public sealed class RemotePlayServer : IDisposable
{
    private static RemotePlayServer? _instance;
    public static RemotePlayServer Instance => _instance ??= new RemotePlayServer();

    public const int DefaultPort = 8585;
    public const int BeaconPort = 8586;

    private TcpListener? _listener;
    private UdpClient? _udpBeacon;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _beaconTask;

    public string? LanIp { get; private set; }
    public int Port { get; private set; } = DefaultPort;
    public string ServerUrl { get; private set; } = "";
    public string PairingToken { get; private set; } = Guid.NewGuid().ToString("N")[..8];
    public bool IsRunning => _listener != null;
    public int ConnectedClientsCount { get; private set; }
    public string? TunnelUrl => RemoteTunnelService.Instance.TunnelUrl;
    public string SmartConnectUrl
    {
        get
        {
            string? tunnel = RemoteTunnelService.Instance.TunnelUrl;
            if (!string.IsNullOrEmpty(tunnel))
            {
                return $"{ServerUrl}&tunnel={Uri.EscapeDataString(tunnel)}";
            }
            return ServerUrl;
        }
    }

    public event Action<string>? OnLog;
    public event Action? OnStateChanged;

    private void OnTunnelStateChanged() => OnStateChanged?.Invoke();

    public bool Start(int port = DefaultPort)
    {
        if (IsRunning) return true;

        LanIp = GetLocalLanIp() ?? "127.0.0.1";
        Port = port;
        ServerUrl = $"http://{LanIp}:{Port}/?auth={PairingToken}";

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();

            // Start screen capture engine
            ScreenCaptureService.Instance.Start();

            // Start automated multi-network tunnel & UPnP in background
            RemoteTunnelService.Instance.OnStateChanged -= OnTunnelStateChanged;
            RemoteTunnelService.Instance.OnStateChanged += OnTunnelStateChanged;
            _ = RemoteTunnelService.Instance.StartAsync(Port, PairingToken);

            _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _beaconTask = Task.Run(() => BeaconLoopAsync(_cts.Token));

            Log($"SUO Link Server started on {ServerUrl}");
            OnStateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to start SUO Link Server on port {Port}: {ex.Message}");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _udpBeacon?.Close(); } catch { }
        _listener = null;
        _udpBeacon = null;

        ScreenCaptureService.Instance.Stop();
        RemoteTunnelService.Instance.Stop();
        RemoteTunnelService.Instance.OnStateChanged -= OnTunnelStateChanged;

        Log("SUO Link Server stopped.");
        OnStateChanged?.Invoke();
    }

    private void Log(string msg) => OnLog?.Invoke($"[SUO Link] {msg}");

    private async Task AcceptLoopAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancel);
                _ = Task.Run(() => HandleClientAsync(client, cancel), cancel);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested) break;
                Log($"Accept error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Broadcasts UDP discovery packets every 2 seconds so the SUO Link Android app
    /// can automatically discover the PC without any manual IP configuration.
    /// </summary>
    private async Task BeaconLoopAsync(CancellationToken cancel)
    {
        try
        {
            _udpBeacon = new UdpClient { EnableBroadcast = true };
            var endpoint = new IPEndPoint(IPAddress.Broadcast, BeaconPort);

            while (!cancel.IsCancellationRequested)
            {
                var (appVer, appVerCode) = GetCurrentAppVersion();
                var beacon = new
                {
                    service = "SUO_LINK_HOST",
                    name = Environment.MachineName,
                    ip = LanIp,
                    port = Port,
                    auth = PairingToken,
                    game = ActiveGameTrackerService.CurrentGame?.Name ?? "",
                    tunnel = RemoteTunnelService.Instance.TunnelUrl ?? "",
                    version = appVer,
                    versionCode = appVerCode
                };

                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(beacon));
                try
                {
                    await _udpBeacon.SendAsync(bytes, bytes.Length, endpoint);
                }
                catch { }

                await Task.Delay(2000, cancel);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Beacon warning: {ex.Message}");
        }
    }

    public static (string version, int versionCode) GetCurrentAppVersion()
    {
        var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (asmVer != null)
        {
            int code = (asmVer.Major * 10000) + (asmVer.Minor * 100) + (asmVer.Build > 0 ? asmVer.Build : 0);
            return ($"{asmVer.Major}.{asmVer.Minor}.{(asmVer.Build > 0 ? asmVer.Build : 0)}", code);
        }
        return ("2.9.23", 20923);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancel)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);

                using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                clientCts.CancelAfter(TimeSpan.FromSeconds(20));

                string? requestLine = await reader.ReadLineAsync(clientCts.Token);
                if (string.IsNullOrEmpty(requestLine)) return;

                var lineParts = requestLine.Split(' ');
                if (lineParts.Length < 2) return;
                string method = lineParts[0].ToUpperInvariant();
                string rawUrl = lineParts[1];

                string path = rawUrl;
                string query = "";
                int qIdx = rawUrl.IndexOf('?');
                if (qIdx >= 0)
                {
                    path = rawUrl.Substring(0, qIdx);
                    query = rawUrl.Substring(qIdx + 1);
                }

                // Read headers
                int contentLength = 0;
                string? wsKey = null;
                bool isUpgrade = false;
                string? header;

                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(clientCts.Token)))
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(header.Substring(15).Trim(), out contentLength);
                    }
                    else if (header.StartsWith("Upgrade:", StringComparison.OrdinalIgnoreCase) &&
                             header.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isUpgrade = true;
                    }
                    else if (header.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    {
                        wsKey = header.Substring(18).Trim();
                    }
                }

                // CORS Preflight
                if (method == "OPTIONS")
                {
                    string cors = "HTTP/1.1 204 No Content\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(cors), cancel);
                    return;
                }

                // WebSocket Upgrade (Input Relay)
                if (isUpgrade && !string.IsNullOrEmpty(wsKey) && path.StartsWith("/ws/input"))
                {
                    await UpgradeAndHandleWebSocketAsync(stream, wsKey, cancel);
                    return;
                }

                // Route: MJPEG Video Stream
                if (method == "GET" && path == "/api/stream")
                {
                    await StreamMjpegLoopAsync(stream, cancel);
                    return;
                }

                // Route: APK Download
                if (method == "GET" && (path == "/download/suo-link.apk" || path == "/suo-link.apk"))
                {
                    await ServeApkAsync(stream, cancel);
                    return;
                }

                // Route: APK Version Check (for SUO Link APK auto-update on launch)
                if (method == "GET" && path == "/api/version")
                {
                    var (appVer, appVerCode) = GetCurrentAppVersion();
                    var ver = new
                    {
                        version = appVer,
                        versionCode = appVerCode,
                        apkUrl = "/download/suo-link.apk",
                        tunnelUrl = RemoteTunnelService.Instance.TunnelUrl ?? ""
                    };
                    await SendJsonResponseAsync(stream, JsonSerializer.Serialize(ver), cancel);
                    return;
                }

                // Route: Remote Tunnel & Multi-Network Status
                if (method == "GET" && path == "/api/tunnel")
                {
                    var tunnelInfo = new
                    {
                        tunnelUrl = RemoteTunnelService.Instance.TunnelUrl,
                        publicWanIp = RemoteTunnelService.Instance.PublicWanIp,
                        isUpnpMapped = RemoteTunnelService.Instance.IsUpnpMapped,
                        isTunnelActive = RemoteTunnelService.Instance.IsTunnelActive
                    };
                    await SendJsonResponseAsync(stream, JsonSerializer.Serialize(tunnelInfo), cancel);
                    return;
                }

                // Route: Games API
                if (method == "GET" && path == "/api/games")
                {
                    var games = await GetScannedGamesListAsync();
                    string json = JsonSerializer.Serialize(games);
                    await SendJsonResponseAsync(stream, json, cancel);
                    return;
                }

                // Route: Status API
                if (method == "GET" && path == "/api/status")
                {
                    var status = new
                    {
                        server = "SUO Link Host",
                        hostName = Environment.MachineName,
                        ip = LanIp,
                        port = Port,
                        fps = ScreenCaptureService.Instance.TargetFps,
                        quality = ScreenCaptureService.Instance.JpegQuality,
                        currentGame = ActiveGameTrackerService.CurrentGame?.Name,
                        currentAppId = ActiveGameTrackerService.CurrentGame?.AppId,
                        connectedClients = ConnectedClientsCount
                    };
                    await SendJsonResponseAsync(stream, JsonSerializer.Serialize(status), cancel);
                    return;
                }

                // Route: Launch Game
                if (method == "POST" && path == "/api/launch")
                {
                    string body = await ReadBodyAsync(reader, contentLength, cancel);
                    bool launched = LaunchGameFromBody(body);
                    await SendJsonResponseAsync(stream, JsonSerializer.Serialize(new { success = launched }), cancel);
                    return;
                }

                // Route: Layout Profiles GET/POST
                if (path.StartsWith("/api/layouts/"))
                {
                    string appId = path.Substring("/api/layouts/".Length).Trim();
                    if (method == "GET")
                    {
                        var profile = RemoteProfileStore.GetProfileForGame(appId);
                        await SendJsonResponseAsync(stream, JsonSerializer.Serialize(profile), cancel);
                        return;
                    }
                    else if (method == "POST")
                    {
                        string body = await ReadBodyAsync(reader, contentLength, cancel);
                        var profile = JsonSerializer.Deserialize<ControllerLayoutProfile>(body);
                        if (profile != null)
                        {
                            RemoteProfileStore.SaveProfile(profile);
                            await SendJsonResponseAsync(stream, JsonSerializer.Serialize(new { success = true }), cancel);
                            return;
                        }
                    }
                }

                // Route: HTTP Direct Input Injection
                if (method == "POST" && path == "/api/input")
                {
                    string body = await ReadBodyAsync(reader, contentLength, cancel);
                    ProcessInputJson(body);
                    await SendJsonResponseAsync(stream, "{\"ok\":true}", cancel);
                    return;
                }

                // Route: Default GameHub Mobile SPA UI
                string html = SuoLinkWebUi.GetHtml(PairingToken, LanIp ?? "127.0.0.1", Port);
                byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                string response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {htmlBytes.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancel);
                await stream.WriteAsync(htmlBytes, cancel);
                await stream.FlushAsync(cancel);
            }
            catch (Exception ex)
            {
                Log($"HTTP Request error: {ex.Message}");
            }
        }
    }

    private async Task UpgradeAndHandleWebSocketAsync(NetworkStream stream, string secWebSocketKey, CancellationToken cancel)
    {
        try
        {
            string acceptKey = ComputeWebSocketAcceptKey(secWebSocketKey);
            string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                              "Upgrade: websocket\r\n" +
                              "Connection: Upgrade\r\n" +
                              $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

            byte[] headBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(headBytes, cancel);
            await stream.FlushAsync(cancel);

            using var ws = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, TimeSpan.FromSeconds(30));
            ConnectedClientsCount++;
            OnStateChanged?.Invoke();
            Log("SUO Link Client connected (WebSocket input active)");

            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancel);
                    break;
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessInputJson(json);
                }
            }
        }
        catch { }
        finally
        {
            ConnectedClientsCount = Math.Max(0, ConnectedClientsCount - 1);
            OnStateChanged?.Invoke();
            Log("SUO Link Client disconnected");
        }
    }

    private static string ComputeWebSocketAcceptKey(string key)
    {
        const string guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key.Trim() + guid));
        return Convert.ToBase64String(hash);
    }

    private async Task StreamMjpegLoopAsync(NetworkStream stream, CancellationToken cancel)
    {
        try
        {
            string header = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: multipart/x-mixed-replace; boundary=--frame\r\n" +
                            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancel);

            var capture = ScreenCaptureService.Instance;
            long lastSentIndex = -1;

            while (!cancel.IsCancellationRequested && clientConnected(stream))
            {
                long currentIndex = capture.FrameIndex;
                if (currentIndex != lastSentIndex)
                {
                    byte[]? frame = capture.GetLatestFrame();
                    if (frame != null && frame.Length > 0)
                    {
                        string partHeader = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                        byte[] partHeaderBytes = Encoding.ASCII.GetBytes(partHeader);
                        byte[] partFooterBytes = Encoding.ASCII.GetBytes("\r\n");

                        await stream.WriteAsync(partHeaderBytes, cancel);
                        await stream.WriteAsync(frame, cancel);
                        await stream.WriteAsync(partFooterBytes, cancel);
                        await stream.FlushAsync(cancel);

                        lastSentIndex = currentIndex;
                    }
                }

                await Task.Delay(15, cancel);
            }
        }
        catch { }
    }

    private static bool clientConnected(NetworkStream stream)
    {
        try
        {
            return stream.Socket != null && stream.Socket.Connected;
        }
        catch { return false; }
    }

    private async Task ServeApkAsync(NetworkStream stream, CancellationToken cancel)
    {
        string[] candidatePaths = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SUO-Link.apk"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "SUO-Link.apk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudRedirect", "SUO-Link.apk")
        };

        string? found = candidatePaths.FirstOrDefault(File.Exists);
        if (found == null)
        {
            string notFound = "HTTP/1.1 404 Not Found\r\nContent-Type: text/plain\r\nContent-Length: 35\r\n\r\nSUO-Link.apk is being generated...";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(notFound), cancel);
            return;
        }

        byte[] apkBytes = await File.ReadAllBytesAsync(found, cancel);
        string resp = $"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.android.package-archive\r\nContent-Disposition: attachment; filename=\"SUO-Link.apk\"\r\nContent-Length: {apkBytes.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), cancel);
        await stream.WriteAsync(apkBytes, cancel);
        await stream.FlushAsync(cancel);
    }

    private async Task<List<RemoteGameItem>> GetScannedGamesListAsync()
    {
        var list = new List<RemoteGameItem>();
        try
        {
            var summary = await SteamGameScannerService.ScanInstalledSteamGamesAsync(autoEnroll: false);
            var activeAppId = ActiveGameTrackerService.CurrentGame?.AppId;

            foreach (var g in summary.Games)
            {
                string headerUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{g.AppId}/header.jpg";
                bool isRunning = (activeAppId.HasValue && activeAppId.Value == g.AppId);
                list.Add(new RemoteGameItem(
                    g.AppId.ToString(),
                    g.Name,
                    headerUrl,
                    isRunning,
                    g.HasSteamCloud || g.AutoEnrolled,
                    g.IsLuaGame ? "Redirected" : "Steam"
                ));
            }
        }
        catch (Exception ex)
        {
            Log($"Error reading game list: {ex.Message}");
        }

        return list;
    }

    private static bool LaunchGameFromBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("appId", out var idProp))
            {
                string appId = idProp.GetString() ?? "";
                if (uint.TryParse(appId, out uint uAppId) && uAppId > 0)
                {
                    Process.Start(new ProcessStartInfo($"steam://run/{uAppId}") { UseShellExecute = true });
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void ProcessInputJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            string type = typeProp.GetString() ?? "";

            switch (type)
            {
                case "key_down":
                    if (root.TryGetProperty("key", out var kd))
                    {
                        ushort vk = RemoteInputSimulator.ParseKeyToVk(kd.GetString() ?? "");
                        if (vk != 0) RemoteInputSimulator.SendKey(vk, true);
                    }
                    break;

                case "key_up":
                    if (root.TryGetProperty("key", out var ku))
                    {
                        ushort vk = RemoteInputSimulator.ParseKeyToVk(ku.GetString() ?? "");
                        if (vk != 0) RemoteInputSimulator.SendKey(vk, false);
                    }
                    break;

                case "mouse_down":
                    if (root.TryGetProperty("button", out var btnDown))
                        RemoteInputSimulator.SendMouseButton(btnDown.GetInt32(), true);
                    break;

                case "mouse_up":
                    if (root.TryGetProperty("button", out var btnUp))
                        RemoteInputSimulator.SendMouseButton(btnUp.GetInt32(), false);
                    break;

                case "mouse_move_relative":
                    int dx = root.GetProperty("dx").GetInt32();
                    int dy = root.GetProperty("dy").GetInt32();
                    RemoteInputSimulator.SendMouseMoveRelative(dx, dy);
                    break;

                case "mouse_move_absolute":
                    float nx = root.GetProperty("x").GetSingle();
                    float ny = root.GetProperty("y").GetSingle();
                    RemoteInputSimulator.SendMouseMoveAbsolute(nx, ny);
                    break;

                case "mouse_wheel":
                    int delta = root.GetProperty("delta").GetInt32();
                    RemoteInputSimulator.SendMouseWheel(delta);
                    break;

                case "stick_vector":
                    // Handle virtual WASD stick vector from touch controls
                    float vx = root.GetProperty("x").GetSingle(); // -1.0 to 1.0
                    float vy = root.GetProperty("y").GetSingle(); // -1.0 to 1.0
                    HandleStickWasd(vx, vy);
                    break;
            }
        }
        catch { }
    }

    private static bool _wDown, _sDown, _aDown, _dDown;
    private static void HandleStickWasd(float x, float y)
    {
        const float deadzone = 0.25f;
        bool wantW = y < -deadzone;
        bool wantS = y > deadzone;
        bool wantA = x < -deadzone;
        bool wantD = x > deadzone;

        if (wantW != _wDown) { RemoteInputSimulator.SendKey(0x57, wantW); _wDown = wantW; }
        if (wantS != _sDown) { RemoteInputSimulator.SendKey(0x53, wantS); _sDown = wantS; }
        if (wantA != _aDown) { RemoteInputSimulator.SendKey(0x41, wantA); _aDown = wantA; }
        if (wantD != _dDown) { RemoteInputSimulator.SendKey(0x44, wantD); _dDown = wantD; }
    }

    private static async Task SendJsonResponseAsync(NetworkStream stream, string json, CancellationToken cancel)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        string header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancel);
        await stream.WriteAsync(body, cancel);
        await stream.FlushAsync(cancel);
    }

    private static async Task<string> ReadBodyAsync(StreamReader reader, int contentLength, CancellationToken cancel)
    {
        if (contentLength <= 0) return "";
        char[] buf = new char[contentLength];
        int total = 0;
        while (total < contentLength)
        {
            int r = await reader.ReadAsync(buf, total, contentLength - total);
            if (r <= 0) break;
            total += r;
        }
        return new string(buf, 0, total);
    }

    public static string? GetLocalLanIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                string ip = ep.Address.ToString();
                if (!ip.StartsWith("127.") && !ip.StartsWith("169.254.")) return ip;
            }
        }
        catch { }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = ua.Address.ToString();
                        if (!ip.StartsWith("127.") && !ip.StartsWith("169.254.")) return ip;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public void Dispose() => Stop();
}
