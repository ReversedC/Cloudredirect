using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace CloudRedirect.Services;

/// <summary>
/// Freezes non-game background apps (browsers, chat, launchers) to Idle priority
/// and flushes their working set memory to maximize CPU & RAM headroom for games.
/// </summary>
public static class BackgroundAppFreezer
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    private static readonly HashSet<string> TargetProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "discord",
        "spotify",
        "slack",
        "epicgameslauncher",
        "battle.net",
        "eadesktop"
    };

    private static readonly HashSet<int> _frozenPids = new();
    public static bool IsFrozen => _frozenPids.Count > 0;

    public static event Action<bool, int>? FreezeStateChanged;

    public static (int frozenCount, long estimatedMbReclaimed) FreezeBackgroundApps()
    {
        int count = 0;
        long bytesBefore = 0;
        long bytesAfter = 0;

        _frozenPids.Clear();

        var currentPid = Environment.ProcessId;
        var activeGamePid = ActiveGameTrackerService.CurrentGame?.ProcessId ?? -1;

        foreach (var procName in TargetProcesses)
        {
            try
            {
                var procs = Process.GetProcessesByName(procName);
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == currentPid || p.Id == activeGamePid) continue;

                        bytesBefore += p.WorkingSet64;

                        // Lower priority to Idle to yield CPU cycles to game
                        p.PriorityClass = ProcessPriorityClass.Idle;

                        // Flush physical memory working set to disk/pagefile
                        EmptyWorkingSet(p.Handle);

                        p.Refresh();
                        bytesAfter += p.WorkingSet64;

                        _frozenPids.Add(p.Id);
                        count++;
                    }
                    catch
                    {
                        // Some processes may have access restrictions
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        long reclaimedMb = Math.Max(0, (bytesBefore - bytesAfter) / (1024 * 1024));
        FreezeStateChanged?.Invoke(IsFrozen, count);
        return (count, reclaimedMb);
    }

    public static int ThawBackgroundApps()
    {
        int thawed = 0;
        foreach (var pid in _frozenPids.ToList())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.PriorityClass = ProcessPriorityClass.Normal;
                thawed++;
            }
            catch
            {
            }
        }

        _frozenPids.Clear();
        FreezeStateChanged?.Invoke(false, 0);
        return thawed;
    }

    public static bool ToggleFreeze()
    {
        if (IsFrozen)
        {
            ThawBackgroundApps();
            return false;
        }
        else
        {
            FreezeBackgroundApps();
            return true;
        }
    }
}
