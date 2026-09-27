using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Manages automatic multi-network connectivity for SUO Link Remote Play.
/// Handles:
/// 1. UPnP NAT port mapping for direct router forwarding.
/// 2. Cloudflare Quick Tunnel (trycloudflare.com) for zero-configuration
///    access across cellular (4G/5G), CGNAT, and foreign Wi-Fi networks.
/// </summary>
public sealed class RemoteTunnelService
{
    private static RemoteTunnelService? _instance;
    public static RemoteTunnelService Instance => _instance ??= new RemoteTunnelService();

    private Process? _tunnelProcess;
    private CancellationTokenSource? _tunnelCts;

    public string? TunnelUrl { get; private set; }
    public string? PublicWanIp { get; private set; }
    public bool IsUpnpMapped { get; private set; }
    public bool IsTunnelActive => !string.IsNullOrEmpty(TunnelUrl) && _tunnelProcess != null && !_tunnelProcess.HasExited;

    public event Action<string>? OnLog;
    public event Action? OnStateChanged;

    private void Log(string msg) => OnLog?.Invoke($"[RemoteTunnel] {msg}");

    private static string GetBinDirectory()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudRedirect", "bin");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string GetCloudflaredExePath()
    {
        return Path.Combine(GetBinDirectory(), "cloudflared.exe");
    }

    /// <summary>
    /// Starts both UPnP port mapping and Cloudflare Quick Tunnel in background.
    /// </summary>
    public Task StartAsync(int localPort = 8585, string pairingToken = "")
    {
        _tunnelCts?.Cancel();
        _tunnelCts = new CancellationTokenSource();
        var token = _tunnelCts.Token;

        // 1. Try UPnP in background
        _ = Task.Run(() => SetupUpnpMappingAsync(localPort, token), token);

        // 2. Start Cloudflare Quick Tunnel for seamless 4G/5G and multi-network connectivity
        _ = Task.Run(() => StartCloudflareTunnelAsync(localPort, pairingToken, token), token);

        return Task.CompletedTask;
    }

    public void Stop()
    {
        _tunnelCts?.Cancel();
        TunnelUrl = null;

        try
        {
            if (_tunnelProcess != null && !_tunnelProcess.HasExited)
            {
                _tunnelProcess.Kill(entireProcessTree: true);
                _tunnelProcess.Dispose();
            }
        }
        catch { }
        finally
        {
            _tunnelProcess = null;
        }

        OnStateChanged?.Invoke();
        Log("Remote Tunnel stopped.");
    }

    #region Cloudflare Quick Tunnel

    private async Task StartCloudflareTunnelAsync(int localPort, string pairingToken, CancellationToken cancel)
    {
        try
        {
            string exe = GetCloudflaredExePath();
            if (!File.Exists(exe))
            {
                Log("Cloudflared not found. Downloading zero-config tunnel engine...");
                bool ok = await DownloadCloudflaredAsync(exe, cancel);
                if (!ok || cancel.IsCancellationRequested) return;
            }

            Log("Starting Cloudflare Quick Tunnel for remote network access...");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"tunnel --url http://127.0.0.1:{localPort} --no-autoupdate",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _tunnelProcess = new Process { StartInfo = psi };
            _tunnelProcess.Start();

            // Cloudflare logs the quick tunnel URL to standard error:
            // "https://[a-zA-Z0-9-]+\.trycloudflare\.com"
            var regex = new Regex(@"https://[a-zA-Z0-9\-]+\.trycloudflare\.com", RegexOptions.Compiled);

            var readTask = Task.Run(async () =>
            {
                using var reader = _tunnelProcess.StandardError;
                while (!cancel.IsCancellationRequested && !_tunnelProcess.HasExited)
                {
                    string? line = await reader.ReadLineAsync(cancel);
                    if (line == null) break;

                    var match = regex.Match(line);
                    if (match.Success && string.IsNullOrEmpty(TunnelUrl))
                    {
                        string baseTunnel = match.Value;
                        TunnelUrl = string.IsNullOrEmpty(pairingToken) 
                            ? baseTunnel 
                            : $"{baseTunnel}/?auth={pairingToken}";
                        Log($"Quick Tunnel established: {TunnelUrl}");
                        OnStateChanged?.Invoke();
                    }
                }
            }, cancel);

            await Task.WhenAny(readTask, Task.Delay(25000, cancel));

            if (string.IsNullOrEmpty(TunnelUrl))
            {
                Log("Quick Tunnel initialization timed out or waiting for URL.");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Quick Tunnel error: {ex.Message}");
        }
    }

    private async Task<bool> DownloadCloudflaredAsync(string targetPath, CancellationToken cancel)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            const string downloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

            using var resp = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            resp.EnsureSuccessStatusCode();

            string tempPath = targetPath + ".tmp";
            using (var stream = await resp.Content.ReadAsStreamAsync(cancel))
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fs, cancel);
            }

            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tempPath, targetPath);
            Log("Cloudflared tunnel engine downloaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to download tunnel engine: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region UPnP NAT Port Mapping

    private async Task SetupUpnpMappingAsync(int port, CancellationToken cancel)
    {
        try
        {
            Log("Discovering UPnP router on local network...");
            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Client.ReceiveTimeout = 2500;

            const string ssdp = "M-SEARCH * HTTP/1.1\r\n" +
                                "HOST: 239.255.255.250:1900\r\n" +
                                "MAN: \"ssdp:discover\"\r\n" +
                                "MX: 2\r\n" +
                                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";

            byte[] reqBytes = Encoding.ASCII.GetBytes(ssdp);
            var ep = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            await udp.SendAsync(reqBytes, reqBytes.Length, ep);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cts.CancelAfter(3000);

            var resp = await udp.ReceiveAsync(cts.Token);
            string respText = Encoding.ASCII.GetString(resp.Buffer);

            var locMatch = Regex.Match(respText, @"LOCATION:\s*(http://[^\r\n]+)", RegexOptions.IgnoreCase);
            if (!locMatch.Success)
            {
                Log("No UPnP router location found.");
                return;
            }

            string descUrl = locMatch.Groups[1].Value.Trim();
            Log($"Found UPnP router description at {descUrl}");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            string descXml = await http.GetStringAsync(descUrl, cancel);

            // Locate WANIPConnection or WANPPPConnection
            var serviceMatch = Regex.Match(descXml, @"<service>[\s\S]*?<serviceType>(urn:schemas-upnp-org:service:(WANIPConnection|WANPPPConnection):1)</serviceType>[\s\S]*?<controlURL>([^<]+)</controlURL>[\s\S]*?</service>", RegexOptions.IgnoreCase);

            if (!serviceMatch.Success)
            {
                Log("Router does not expose WANIPConnection or WANPPPConnection.");
                return;
            }

            string serviceType = serviceMatch.Groups[1].Value;
            string controlUrlPart = serviceMatch.Groups[3].Value.Trim();

            Uri baseUri = new Uri(descUrl);
            Uri controlUri = new Uri(baseUri, controlUrlPart);

            string localIp = RemotePlayServer.GetLocalLanIp() ?? "127.0.0.1";

            // 1. Send AddPortMapping SOAP
            string soapAdd = "<?xml version=\"1.0\"?>\r\n" +
                             "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                             "  <s:Body>\r\n" +
                             $"    <u:AddPortMapping xmlns:u=\"{serviceType}\">\r\n" +
                             "      <NewRemoteHost></NewRemoteHost>\r\n" +
                             $"      <NewExternalPort>{port}</NewExternalPort>\r\n" +
                             "      <NewProtocol>TCP</NewProtocol>\r\n" +
                             $"      <NewInternalPort>{port}</NewInternalPort>\r\n" +
                             $"      <NewInternalClient>{localIp}</NewInternalClient>\r\n" +
                             "      <NewEnabled>1</NewEnabled>\r\n" +
                             "      <NewPortMappingDescription>CloudRedirect SUO Link</NewPortMappingDescription>\r\n" +
                             "      <NewLeaseDuration>0</NewLeaseDuration>\r\n" +
                             "    </u:AddPortMapping>\r\n" +
                             "  </s:Body>\r\n" +
                             "</s:Envelope>";

            using (var content = new StringContent(soapAdd, Encoding.UTF8, "text/xml"))
            {
                content.Headers.Add("SOAPAction", $"\"{serviceType}#AddPortMapping\"");
                var res = await http.PostAsync(controlUri, content, cancel);
                if (res.IsSuccessStatusCode)
                {
                    IsUpnpMapped = true;
                    Log($"UPnP port mapping successful: TCP {port} -> {localIp}:{port}");
                }
            }

            // 2. Query GetExternalIPAddress SOAP
            string soapIp = "<?xml version=\"1.0\"?>\r\n" +
                            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                            "  <s:Body>\r\n" +
                            $"    <u:GetExternalIPAddress xmlns:u=\"{serviceType}\"/>\r\n" +
                            "  </s:Body>\r\n" +
                            "</s:Envelope>";

            using (var content = new StringContent(soapIp, Encoding.UTF8, "text/xml"))
            {
                content.Headers.Add("SOAPAction", $"\"{serviceType}#GetExternalIPAddress\"");
                var res = await http.PostAsync(controlUri, content, cancel);
                if (res.IsSuccessStatusCode)
                {
                    string xml = await res.Content.ReadAsStringAsync(cancel);
                    var ipMatch = Regex.Match(xml, @"<NewExternalIPAddress>([^<]+)</NewExternalIPAddress>");
                    if (ipMatch.Success)
                    {
                        PublicWanIp = ipMatch.Groups[1].Value.Trim();
                        Log($"UPnP Public WAN IP: {PublicWanIp}");
                    }
                }
            }

            OnStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"UPnP notice: {ex.Message}");
        }
    }

    #endregion
}
