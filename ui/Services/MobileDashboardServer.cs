using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Mobile Phone LAN Dashboard Server:
/// Serves a sleek, Steam-styled mobile web dashboard over local Wi-Fi / LAN,
/// enabling real-time game status tracking, snapshot generation, and manual sync from phones and tablets.
/// </summary>
public static class MobileDashboardServer
{
    private static HttpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static bool _isRunning;

    public static bool IsRunning => _isRunning;

    public static string? GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public static string GetDashboardUrl()
    {
        int port = AppSettings.MobileDashboardPort;
        var ip = GetLocalIpAddress();
        return $"http://{ip}:{port}/";
    }

    public static void Start()
    {
        if (_isRunning) return;

        try
        {
            int port = AppSettings.MobileDashboardPort;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _isRunning = true;

            Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
        }
        catch (Exception ex)
        {
            // If binding to http://*:port fails due to lack of admin urlacl reservation, fallback to http://localhost
            try
            {
                int port = AppSettings.MobileDashboardPort;
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();

                _cts = new CancellationTokenSource();
                _isRunning = true;
                Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[MobileDashboard] Could not start server: {ex.Message} / {ex2.Message}");
                _isRunning = false;
            }
        }
    }

    public static void Stop()
    {
        _isRunning = false;
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        finally
        {
            _listener = null;
            _cts = null;
        }
    }

    private static async Task ListenLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var req = context.Request;
            var res = context.Response;

            // Security check: restrict to LAN / loopback IPs
            var remoteIp = req.RemoteEndPoint?.Address?.ToString() ?? "";
            if (!IsPrivateOrLoopbackIp(remoteIp))
            {
                res.StatusCode = (int)HttpStatusCode.Forbidden;
                res.Close();
                return;
            }

            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

            if (path == "/" || path == "/index.html")
            {
                var html = RenderMobileHtml();
                byte[] buffer = Encoding.UTF8.GetBytes(html);
                res.ContentType = "text/html; charset=utf-8";
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else if (path == "/api/status")
            {
                var json = RenderStatusJson();
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else if (path == "/api/sync" && req.HttpMethod == "POST")
            {
                _ = Task.Run(() => SaveUploadWatcherService.TriggerImmediateSync());
                byte[] buffer = Encoding.UTF8.GetBytes("{\"ok\":true,\"message\":\"Sync triggered\"}");
                res.ContentType = "application/json";
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else if (path == "/api/snapshot" && req.HttpMethod == "POST")
            {
                var active = ActiveGameTrackerService.CurrentActiveGame;
                if (active != null && !string.IsNullOrEmpty(active.SaveDirectory))
                {
                    SaveHistoryManager.CreateSnapshot(active.GameName, active.SaveDirectory, "Mobile Dashboard Manual Snapshot", active.AppId.ToString());
                }
                byte[] buffer = Encoding.UTF8.GetBytes("{\"ok\":true,\"message\":\"Snapshot created\"}");
                res.ContentType = "application/json";
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                res.StatusCode = (int)HttpStatusCode.NotFound;
            }

            res.Close();
        }
        catch { }
    }

    private static bool IsPrivateOrLoopbackIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        if (ip == "127.0.0.1" || ip == "::1" || ip.StartsWith("192.168.") || ip.StartsWith("10.")) return true;
        if (ip.StartsWith("172."))
        {
            var parts = ip.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var second) && second >= 16 && second <= 31)
                return true;
        }
        return false;
    }

    private static string RenderStatusJson()
    {
        var active = ActiveGameTrackerService.CurrentActiveGame;
        var ver = AppUpdater.GetCurrentVersionString();

        var data = new
        {
            version = ver,
            activeGame = active != null ? new
            {
                gameName = active.GameName,
                appId = active.AppId,
                processName = active.ProcessName,
                isRunning = active.IsRunning,
                startTime = active.StartTime.ToString("o")
            } : null,
            timestamp = DateTime.Now.ToString("o")
        };

        return JsonSerializer.Serialize(data);
    }

    private static string RenderMobileHtml()
    {
        var active = ActiveGameTrackerService.CurrentActiveGame;
        var gameName = active != null && active.IsRunning ? active.GameName : "No Active Game Detected";
        var appIdStr = active != null && active.IsRunning ? $"AppID: {active.AppId}" : "Steam Idle";
        var pulseColor = active != null && active.IsRunning ? "#A4D007" : "#8F98A0";
        var statusText = active != null && active.IsRunning ? "Actively Playing" : "Standby";

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
            <title>CloudRedirect Mobile Dashboard</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
                body { background-color: #0e141b; color: #c6d4df; padding: 16px; min-height: 100vh; display: flex; flex-direction: column; }
                header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 20px; padding-bottom: 12px; border-bottom: 1px solid #1e2e3e; }
                .logo { display: flex; align-items: center; gap: 8px; font-weight: bold; font-size: 18px; color: #66c0f4; }
                .badge { background: #162432; color: #66c0f4; border: 1px solid #28445f; padding: 4px 8px; border-radius: 6px; font-size: 12px; font-weight: bold; }
                .card { background: #141f2c; border: 1px solid #233446; border-radius: 12px; padding: 18px; margin-bottom: 16px; box-shadow: 0 4px 12px rgba(0,0,0,0.3); }
                .card-title { font-size: 13px; text-transform: uppercase; letter-spacing: 0.5px; color: #8f98a0; margin-bottom: 8px; font-weight: 600; }
                .active-row { display: flex; align-items: center; gap: 10px; margin-bottom: 4px; }
                .pulse-dot { width: 10px; height: 10px; border-radius: 50%; background: {{pulseColor}}; box-shadow: 0 0 8px {{pulseColor}}; }
                .game-title { font-size: 20px; font-weight: bold; color: #ffffff; }
                .app-meta { font-size: 13px; color: #66c0f4; margin-top: 4px; }
                .grid-actions { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px; }
                button { background: #213d5b; border: 1px solid #3d6f9f; color: #ffffff; padding: 14px; border-radius: 10px; font-size: 15px; font-weight: bold; cursor: pointer; display: flex; align-items: center; justify-content: center; gap: 8px; transition: 0.2s; }
                button:active { transform: scale(0.97); }
                button.btn-green { background: #2e4d16; border-color: #5c8f22; color: #a4d007; }
                .status-badge { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 600; padding: 4px 10px; border-radius: 12px; background: #18281b; color: #a4d007; border: 1px solid #365c19; margin-top: 10px; }
                footer { margin-top: auto; text-align: center; font-size: 12px; color: #62717f; padding-top: 16px; }
                .toast { display: none; position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); background: #162a3d; border: 1px solid #00d2ff; color: #00d2ff; padding: 10px 20px; border-radius: 20px; font-size: 14px; font-weight: bold; box-shadow: 0 4px 16px rgba(0,0,0,0.5); z-index: 1000; }
            </style>
        </head>
        <body>
            <header>
                <div class="logo">
                    <span>☁️ CloudRedirect</span>
                </div>
                <div class="badge">LAN Mobile</div>
            </header>

            <div class="card">
                <div class="card-title">Live Session Telemetry</div>
                <div class="active-row">
                    <div class="pulse-dot"></div>
                    <div class="game-title" id="game-name">{{gameName}}</div>
                </div>
                <div class="app-meta" id="app-meta">{{appIdStr}}</div>
                <div class="status-badge" id="status-pill">{{statusText}}</div>
            </div>

            <div class="grid-actions">
                <button class="btn-green" onclick="triggerSync()">⚡ Sync Now</button>
                <button onclick="triggerSnapshot()">📸 Take Snapshot</button>
            </div>

            <div class="card">
                <div class="card-title">Device Link &amp; Features</div>
                <p style="font-size: 13px; line-height: 1.5; color: #8f98a0;">Connected directly via local Wi-Fi. Control cloud backups and snapshots in real-time from your handheld, phone, or tablet while gaming on PC.</p>
            </div>

            <div id="toast" class="toast">Action sent!</div>

            <footer>CloudRedirect • Local LAN Mobile Companion</footer>

            <script>
                function showToast(msg) {
                    const t = document.getElementById('toast');
                    t.innerText = msg;
                    t.style.display = 'block';
                    setTimeout(() => { t.style.display = 'none'; }, 2200);
                }

                async function triggerSync() {
                    try {
                        const r = await fetch('/api/sync', { method: 'POST' });
                        showToast('⚡ Save Sync Triggered');
                    } catch (e) {
                        showToast('Failed to trigger sync');
                    }
                }

                async function triggerSnapshot() {
                    try {
                        const r = await fetch('/api/snapshot', { method: 'POST' });
                        showToast('📸 Save Snapshot Created');
                    } catch (e) {
                        showToast('Failed to create snapshot');
                    }
                }

                async function refresh() {
                    try {
                        const res = await fetch('/api/status');
                        const data = await res.json();
                        if (data.activeGame && data.activeGame.isRunning) {
                            document.getElementById('game-name').innerText = data.activeGame.gameName;
                            document.getElementById('app-meta').innerText = 'AppID: ' + data.activeGame.appId;
                            document.getElementById('status-pill').innerText = 'Actively Playing';
                        } else {
                            document.getElementById('game-name').innerText = 'No Active Game Detected';
                            document.getElementById('app-meta').innerText = 'Steam Idle';
                            document.getElementById('status-pill').innerText = 'Standby';
                        }
                    } catch(e) {}
                }
                setInterval(refresh, 3000);
            </script>
        </body>
        </html>
        """;
    }
}
