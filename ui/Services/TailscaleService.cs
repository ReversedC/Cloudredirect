using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Manages Tailscale Virtual LAN integration for CloudRedirect.
/// Provides a zero-configuration, permanent static WireGuard IP (100.x.y.z)
/// that never expires across PC reboots and bypasses ISP DNS blocks on 4G/5G.
/// </summary>
public sealed class TailscaleService
{
    private static TailscaleService? _instance;
    public static TailscaleService Instance => _instance ??= new TailscaleService();

    private readonly Timer _pollTimer;
    private bool _isRefreshing;

    public bool IsInstalled => File.Exists(GetTailscaleExePath());
    public string? TailscaleIp { get; private set; }
    public bool IsConnected => !string.IsNullOrEmpty(TailscaleIp);
    public string? LoginUrl { get; private set; }

    public event Action? OnStateChanged;

    private TailscaleService()
    {
        // Poll every 5 seconds for Tailscale IP status
        _pollTimer = new Timer(async _ => await RefreshStatusAsync(), null, 1000, 5000);
    }

    public static string GetTailscaleExePath()
    {
        string p1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (File.Exists(p1)) return p1;

        string p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale.exe");
        if (File.Exists(p2)) return p2;

        return p1;
    }

    public static string GetTailscaleIpnPath()
    {
        string p1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale-ipn.exe");
        if (File.Exists(p1)) return p1;

        string p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale-ipn.exe");
        if (File.Exists(p2)) return p2;

        return p1;
    }

    public async Task RefreshStatusAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        try
        {
            string? prevIp = TailscaleIp;
            string? newIp = await QueryTailscaleIpAsync();

            if (newIp != prevIp)
            {
                TailscaleIp = newIp;
                OnStateChanged?.Invoke();
            }

            if (string.IsNullOrEmpty(TailscaleIp) && IsInstalled)
            {
                LoginUrl = await QueryLoginUrlAsync();
            }
            else
            {
                LoginUrl = null;
            }
        }
        catch { }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static async Task<string?> QueryTailscaleIpAsync()
    {
        // 1. Try tailscale.exe ip -4
        string exe = GetTailscaleExePath();
        if (File.Exists(exe))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "ip -4",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                    await proc.WaitForExitAsync();

                    if (IPAddress.TryParse(output, out var addr) && output.StartsWith("100."))
                    {
                        return output;
                    }
                }
            }
            catch { }
        }

        // 2. Fallback: inspect network interfaces for Tailscale adapter (100.64.0.0/10)
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                bool isTs = ni.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                           ni.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string ip = ua.Address.ToString();
                        if (isTs && ip.StartsWith("100."))
                        {
                            return ip;
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static async Task<string?> QueryLoginUrlAsync()
    {
        string exe = GetTailscaleExePath();
        if (!File.Exists(exe)) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string text = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                var match = Regex.Match(text, @"(https://login\.tailscale\.com/a/[a-zA-Z0-9]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<bool> InstallAsync()
    {
        try
        {
            // 1. Try winget install
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install --id Tailscale.Tailscale -e --accept-package-agreements --accept-source-agreements --silent",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                if (IsInstalled)
                {
                    StartTailscaleApp();
                    await RefreshStatusAsync();
                    return true;
                }
            }
        }
        catch { }

        // 2. Direct MSI download fallback
        try
        {
            string tempMsi = Path.Combine(Path.GetTempPath(), "tailscale-setup.msi");
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            using var s = await client.GetStreamAsync("https://pkgs.tailscale.com/stable/tailscale-setup-latest.exe");
            using var fs = new FileStream(tempMsi, FileMode.Create);
            await s.CopyToAsync(fs);
            fs.Close();

            var psi = new ProcessStartInfo
            {
                FileName = tempMsi,
                Arguments = "/quiet",
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                StartTailscaleApp();
                await RefreshStatusAsync();
                return IsInstalled;
            }
        }
        catch { }

        return false;
    }

    public void StartTailscaleApp()
    {
        string ipn = GetTailscaleIpnPath();
        if (File.Exists(ipn))
        {
            try { Process.Start(new ProcessStartInfo(ipn) { UseShellExecute = true }); } catch { }
        }
    }

    public void OpenLogin()
    {
        string? url = LoginUrl;
        if (!string.IsNullOrEmpty(url))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
        else
        {
            string exe = GetTailscaleExePath();
            if (File.Exists(exe))
            {
                try { Process.Start(new ProcessStartInfo(exe, "login") { UseShellExecute = true }); } catch { }
            }
        }
    }
}
