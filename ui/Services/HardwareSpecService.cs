using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace CloudRedirect.Services;

public class UserPcSpecs
{
    [JsonPropertyName("cpu")]
    public string Cpu { get; set; } = "Unknown CPU";

    [JsonPropertyName("cpuCores")]
    public int CpuCores { get; set; } = 4;

    [JsonPropertyName("ramGb")]
    public double RamGb { get; set; } = 8.0;

    [JsonPropertyName("gpu")]
    public string Gpu { get; set; } = "Unknown GPU";

    [JsonPropertyName("vramGb")]
    public double VramGb { get; set; } = 2.0;

    [JsonPropertyName("isIntegratedGpu")]
    public bool IsIntegratedGpu { get; set; } = true;

    [JsonPropertyName("os")]
    public string Os { get; set; } = "Windows 64-bit";
}

/// <summary>
/// Profiles the user's PC hardware (CPU, RAM, GPU, OS) in under 2ms using native Windows APIs
/// for game system requirement compatibility checking.
/// </summary>
public static class HardwareSpecService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private static UserPcSpecs? _cachedSpecs;

    public static UserPcSpecs GetPcSpecs()
    {
        if (_cachedSpecs != null) return _cachedSpecs;

        var specs = new UserPcSpecs();

        // 1. CPU
        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var cpuName = cpuKey?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(cpuName))
            {
                specs.Cpu = cpuName.Trim();
            }
            specs.CpuCores = Environment.ProcessorCount;
        }
        catch { }

        // 2. RAM
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                specs.RamGb = Math.Round((double)memStatus.ullTotalPhys / (1024 * 1024 * 1024), 1);
            }
        }
        catch { }

        // 3. GPU
        try
        {
            var detectedGpus = new List<string>();
            const string videoClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var key = Registry.LocalMachine.OpenSubKey(videoClassKey);
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    if (int.TryParse(subKeyName, out _))
                    {
                        using var sub = key.OpenSubKey(subKeyName);
                        var desc = sub?.GetValue("DriverDesc") as string;
                        if (!string.IsNullOrWhiteSpace(desc))
                        {
                            var d = desc.Trim();
                            if (!d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                                !d.Contains("Remote", StringComparison.OrdinalIgnoreCase) &&
                                !d.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                            {
                                detectedGpus.Add(d);
                            }
                        }
                    }
                }
            }

            if (detectedGpus.Count > 0)
            {
                // Prefer dedicated GPU if multiple found (NVIDIA or Radeon RX / Arc)
                var bestGpu = detectedGpus.Find(g => g.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                                     g.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                                                     g.Contains("RX", StringComparison.OrdinalIgnoreCase) ||
                                                     g.Contains("Arc", StringComparison.OrdinalIgnoreCase)) ?? detectedGpus[0];

                specs.Gpu = bestGpu;
                var lower = bestGpu.ToLowerInvariant();
                specs.IsIntegratedGpu = lower.Contains("intel") ||
                                       (lower.Contains("graphics") && !lower.Contains("rx") && !lower.Contains("gtx") && !lower.Contains("rtx"));
                specs.VramGb = specs.IsIntegratedGpu ? 2.0 : 6.0;
            }
        }
        catch { }

        // 4. OS
        specs.Os = RuntimeInformation.OSDescription;

        _cachedSpecs = specs;
        return specs;
    }
}
