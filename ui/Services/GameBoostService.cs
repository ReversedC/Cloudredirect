using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CloudRedirect.Services;

/// <summary>
/// Game Boost Service.
/// Dynamically optimizes system performance when a Steam game launches:
/// 1. Elevates Game Process & I/O Priority to High.
/// 2. Switches Windows Power Plan to High/Ultimate Performance.
/// 3. Safely trims Standby RAM & Working Sets of background processes.
/// 4. Optionally deprioritizes heavy background resource consumers.
/// 5. Automatically reverts power scheme and process states when game exits.
/// </summary>
public static class GameBoostService
{
    private static readonly object _lock = new();
    private static bool _isBoostActive;
    private static string? _boostedGameName;
    private static int _boostedProcessId;
    private static Guid? _originalPowerScheme;
    private static readonly Dictionary<int, ProcessPriorityClass> _throttledProcesses = new();

    public static bool IsBoostActive
    {
        get { lock (_lock) return _isBoostActive; }
    }

    public static string? BoostedGameName
    {
        get { lock (_lock) return _boostedGameName; }
    }

    public static int BoostedProcessId
    {
        get { lock (_lock) return _boostedProcessId; }
    }

    public static long LastFreedRamBytes { get; private set; }

    public static event Action<bool, string?>? OnBoostStateChanged;

    #region Win32 P/Invoke

    private static readonly Guid HighPerformanceGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid UltimatePerformanceGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(
        IntPtr ProcessHandle,
        int ProcessInformationClass,
        ref int ProcessInformation,
        int ProcessInformationLength);

    private const int ProcessIoPriority = 33;
    private const int IoPriorityHigh = 3;

    #endregion

    /// <summary>
    /// Applies Game Boost asynchronously when a game starts.
    /// Fires immediate toast, switches power plan, trims RAM, and resolves process priority.
    /// </summary>
    public static async Task ApplyBoostAsync(Process? gameProcess, string gameName, string? headerUrl = null, string? procName = null, string? installDir = null)
    {
        if (!AppSettings.GameBoostEnabled)
            return;

        await Task.Run(async () =>
        {
            try
            {
                lock (_lock)
                {
                    _boostedGameName = gameName;
                    _isBoostActive = true;
                    if (gameProcess != null && !gameProcess.HasExited)
                    {
                        _boostedProcessId = gameProcess.Id;
                    }
                }

                // Helper to resolve executable path for GPU preference assignment
                string? ResolveExePath(Process? proc)
                {
                    if (proc != null && !proc.HasExited)
                    {
                        string? p = ActiveGameTrackerService.GetProcessFilePath(proc.Id);
                        if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                        try
                        {
                            if (!string.IsNullOrEmpty(proc.MainModule?.FileName) && File.Exists(proc.MainModule.FileName))
                                return proc.MainModule.FileName;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                    {
                        if (!string.IsNullOrEmpty(procName))
                        {
                            string candidate = Path.Combine(installDir, procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? procName : procName + ".exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                        try
                        {
                            var files = Directory.GetFiles(installDir, "*.exe", SearchOption.AllDirectories);
                            var best = files.FirstOrDefault(f => !f.Contains("crash", StringComparison.OrdinalIgnoreCase) && !f.Contains("setup", StringComparison.OrdinalIgnoreCase) && !f.Contains("unitycrash", StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(best)) return best;
                        }
                        catch { }
                    }

                    return null;
                }

                // 1. Auto-Assign High-Performance Dedicated GPU (Laptops / Dual GPU)
                if (AppSettings.GameBoostHighPerformanceGpu)
                {
                    string? targetExe = ResolveExePath(gameProcess);
                    if (!string.IsNullOrEmpty(targetExe))
                    {
                        AssignHighPerformanceGpu(targetExe);
                    }
                }

                // 2. Switch Windows Power Scheme IMMEDIATELY
                if (AppSettings.GameBoostPowerPlan)
                {
                    SwitchToHighPerformancePowerScheme();
                }

                // 3. Trim Standby RAM / Working Sets IMMEDIATELY
                long freedBytes = 0;
                if (AppSettings.GameBoostRamTrim)
                {
                    freedBytes = TrimBackgroundMemory(gameProcess?.Id ?? 0);
                    LastFreedRamBytes = freedBytes;
                }

                // 4. Auto-Freeze Background Apps (Automatic Mode)
                if (AppSettings.GameBoostAutoFreezeBackground)
                {
                    try
                    {
                        var (frozenCount, reclaimedMb) = BackgroundAppFreezer.FreezeBackgroundApps();
                        if (frozenCount > 0)
                        {
                            Debug.WriteLine($"[GameBoost] Auto-froze {frozenCount} background apps, reclaimed ~{reclaimedMb} MB RAM");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GameBoost] Auto-freeze error: {ex.Message}");
                    }
                }

                // 5. Fire Toast Notification & Events IMMEDIATELY
                string ramText = freedBytes > 10 * 1024 * 1024
                    ? $"⚡ {freedBytes / (1024 * 1024):N0} MB Standby RAM Optimized"
                    : "High CPU Priority & Max Power Active";

                if (AppSettings.GameBoostShowToast)
                {
                    GameBoostToastService.Show(gameName, headerUrl, ramText);
                }

                OnBoostStateChanged?.Invoke(true, gameName);

                // 6. Optionally throttle non-essential background processes
                if (AppSettings.GameBoostThrottleBackground)
                {
                    ThrottleBackgroundProcesses(gameProcess?.Id ?? 0);
                }

                // 7. Elevate Game CPU & I/O Priority (dynamically resolve process if still starting)
                if (AppSettings.GameBoostHighPriority)
                {
                    if (gameProcess == null || gameProcess.HasExited)
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            var procs = Process.GetProcesses();
                            foreach (var p in procs)
                            {
                                try
                                {
                                    if (p.Id <= 4) continue;
                                    if (!string.IsNullOrEmpty(procName) && p.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        gameProcess = p;
                                        break;
                                    }
                                    if (!string.IsNullOrEmpty(installDir) && p.MainModule?.FileName.StartsWith(installDir, StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        gameProcess = p;
                                        break;
                                    }
                                }
                                catch { }
                            }

                            if (gameProcess != null && !gameProcess.HasExited)
                                break;

                            await Task.Delay(500);
                        }
                    }

                    if (gameProcess != null && !gameProcess.HasExited)
                    {
                        lock (_lock)
                        {
                            _boostedProcessId = gameProcess.Id;
                        }

                        // Ensure GPU preference is registered now that process is resolved
                        if (AppSettings.GameBoostHighPerformanceGpu)
                        {
                            string? targetExe = ResolveExePath(gameProcess);
                            if (!string.IsNullOrEmpty(targetExe))
                            {
                                AssignHighPerformanceGpu(targetExe);
                            }
                        }

                        try
                        {
                            gameProcess.PriorityClass = ProcessPriorityClass.High;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[GameBoost] Could not set CPU priority: {ex.Message}");
                        }

                        try
                        {
                            int ioPriority = IoPriorityHigh;
                            NtSetInformationProcess(gameProcess.Handle, ProcessIoPriority, ref ioPriority, sizeof(int));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[GameBoost] Could not set I/O priority: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogStartup($"[GameBoost] Failed to apply boost: {ex}");
                Debug.WriteLine($"[GameBoost] Failed to apply boost: {ex}");
            }
        });
    }

    /// <summary>
    /// Reverts all optimizations safely when game closes.
    /// </summary>
    public static void RevertBoost()
    {
        lock (_lock)
        {
            if (!_isBoostActive)
                return;

            try
            {
                // 1. Restore Windows Power Scheme
                if (_originalPowerScheme.HasValue)
                {
                    var prev = _originalPowerScheme.Value;
                    PowerSetActiveScheme(IntPtr.Zero, ref prev);
                    _originalPowerScheme = null;
                }

                // 2. Restore Throttled Background Processes
                foreach (var kvp in _throttledProcesses)
                {
                    try
                    {
                        var proc = Process.GetProcessById(kvp.Key);
                        if (!proc.HasExited)
                        {
                            proc.PriorityClass = kvp.Value;
                        }
                    }
                    catch { }
                }
                _throttledProcesses.Clear();

                // 3. Restore/Thaw Auto-Frozen Background Processes
                if (BackgroundAppFreezer.IsFrozen)
                {
                    try
                    {
                        int thawed = BackgroundAppFreezer.ThawBackgroundApps();
                        Debug.WriteLine($"[GameBoost] Thawed {thawed} frozen background processes on game exit.");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameBoost] Error reverting boost: {ex.Message}");
            }
            finally
            {
                _isBoostActive = false;
                _boostedGameName = null;
                _boostedProcessId = 0;
                _lastSmartRamPurgeTime = DateTime.MinValue;
                OnBoostStateChanged?.Invoke(false, null);
                GameBoostToastService.Dismiss();
            }
        }
    }

    private static void SwitchToHighPerformancePowerScheme()
    {
        try
        {
            // Capture original active scheme first
            if (!_originalPowerScheme.HasValue)
            {
                uint res = PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeGuidPtr);
                if (res == 0 && activeGuidPtr != IntPtr.Zero)
                {
                    _originalPowerScheme = Marshal.PtrToStructure<Guid>(activeGuidPtr);
                    LocalFree(activeGuidPtr);
                }
            }

            // Attempt Ultimate Performance first, then fall back to High Performance
            var targetScheme = UltimatePerformanceGuid;
            uint setRes = PowerSetActiveScheme(IntPtr.Zero, ref targetScheme);
            if (setRes != 0)
            {
                targetScheme = HighPerformanceGuid;
                PowerSetActiveScheme(IntPtr.Zero, ref targetScheme);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoost] Power scheme switch error: {ex.Message}");
        }
    }

    public static long PurgeMemory(int gamePid = 0)
    {
        return TrimBackgroundMemory(gamePid);
    }

    private static long TrimBackgroundMemory(int gamePid)
    {
        long initialWorkingSet = 0;
        long finalWorkingSet = 0;

        try
        {
            int currentPid = Environment.ProcessId;
            var processes = Process.GetProcesses();

            var candidateProcs = new List<Process>();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == gamePid || p.Id == currentPid || p.Id <= 4)
                        continue;

                    var name = p.ProcessName.ToLowerInvariant();
                    // Don't touch core Windows system components
                    if (name is "system" or "csrss" or "smss" or "lsass" or "services" or "winlogon" or "dwm" or "explorer")
                        continue;

                    candidateProcs.Add(p);
                    initialWorkingSet += p.WorkingSet64;
                }
                catch { }
            }

            foreach (var p in candidateProcs)
            {
                try
                {
                    EmptyWorkingSet(p.Handle);
                }
                catch { }
            }

            // Re-read working set of candidate processes
            foreach (var p in candidateProcs)
            {
                try
                {
                    p.Refresh();
                    finalWorkingSet += p.WorkingSet64;
                    p.Dispose();
                }
                catch { }
            }
        }
        catch { }

        long freed = initialWorkingSet - finalWorkingSet;
        return freed > 0 ? freed : 0;
    }

    private static void ThrottleBackgroundProcesses(int gamePid)
    {
        try
        {
            int currentPid = Environment.ProcessId;
            string[] heavyApps = { "chrome", "msedge", "firefox", "discord", "spotify", "epicgameslauncher", "qbittorrent", "steamwebhelper" };

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == gamePid || p.Id == currentPid || p.Id <= 4)
                        continue;

                    var name = p.ProcessName.ToLowerInvariant();
                    if (heavyApps.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!_throttledProcesses.ContainsKey(p.Id))
                        {
                            _throttledProcesses[p.Id] = p.PriorityClass;
                            p.PriorityClass = ProcessPriorityClass.BelowNormal;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static DateTime _lastSmartRamPurgeTime = DateTime.MinValue;
    private static readonly TimeSpan MinSmartRamInterval = TimeSpan.FromMinutes(10);
    private const double SmartRamThresholdPercent = 80.0;

    /// <summary>
    /// Checks memory load during an active game session and triggers a smart RAM purge if RAM load >= 80%
    /// and at least 10 minutes have elapsed since the last purge.
    /// </summary>
    public static void CheckPeriodicSmartRam(int gamePid)
    {
        if (!AppSettings.GameBoostEnabled || !AppSettings.GameBoostPeriodicRamPurge)
            return;

        if (!_isBoostActive)
            return;

        var now = DateTime.Now;
        if (now - _lastSmartRamPurgeTime < MinSmartRamInterval)
            return;

        try
        {
            var telemetry = SystemTelemetryService.ReadTelemetry();
            if (telemetry.RamPercent >= SmartRamThresholdPercent)
            {
                _lastSmartRamPurgeTime = now;
                long freed = PurgeMemory(gamePid);
                LastFreedRamBytes = freed;
                Debug.WriteLine($"[GameBoost] Periodic Smart RAM Purge triggered: RAM load was {telemetry.RamPercent:F0}%. Freed {freed / (1024 * 1024)} MB.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoost] Periodic Smart RAM check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces Windows DirectX graphics preference to use the High-Performance discrete GPU (e.g. NVIDIA/AMD)
    /// instead of the integrated GPU. Especially critical for gaming laptops with dual graphics.
    /// Writes to HKCU\Software\Microsoft\DirectX\UserGpuPreferences: "GpuPreference=2;".
    /// </summary>
    public static void AssignHighPerformanceGpu(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        try
        {
            if (!File.Exists(exePath))
                return;

            string fullPath = Path.GetFullPath(exePath);
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
            if (key == null) return;

            object? existingVal = key.GetValue(fullPath);
            string currentStr = existingVal as string ?? "";

            // Check if GpuPreference is already set to 2 (High Performance)
            if (currentStr.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase))
            {
                return; // Already configured for High Performance GPU
            }

            string newVal;
            if (string.IsNullOrEmpty(currentStr))
            {
                newVal = "GpuPreference=2;";
            }
            else if (currentStr.Contains("GpuPreference=0", StringComparison.OrdinalIgnoreCase))
            {
                newVal = currentStr.Replace("GpuPreference=0", "GpuPreference=2");
            }
            else if (currentStr.Contains("GpuPreference=1", StringComparison.OrdinalIgnoreCase))
            {
                newVal = currentStr.Replace("GpuPreference=1", "GpuPreference=2");
            }
            else
            {
                newVal = currentStr.TrimEnd(';') + ";GpuPreference=2;";
            }

            key.SetValue(fullPath, newVal, RegistryValueKind.String);
            Debug.WriteLine($"[GameBoost] Configured High-Performance Dedicated GPU for: {fullPath} ({newVal})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoost] Could not set GPU preference for {exePath}: {ex.Message}");
        }
    }
}
