using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Active Game Priority &amp; Performance Auto-Booster Service:
/// Elevates running game process priority (AboveNormal/High) while automatically throttling
/// CloudRedirect's background CPU/IO priority during gameplay to eliminate any frame drops,
/// then immediately restores normal priorities and triggers clean sync on exit.
/// </summary>
public static class GamePerformanceBoosterService
{
    private static bool _isInitialized;
    private static int? _boostedPid;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        ActiveGameTrackerService.OnActiveGameChanged += HandleActiveGameChanged;
    }

    private static void HandleActiveGameChanged(ActiveGameInfo? game)
    {
        if (!AppSettings.EnableGamePerformanceBooster)
            return;

        if (game != null && game.IsRunning)
        {
            ApplyBoost(game);
        }
        else
        {
            RevertBoost();
        }
    }

    private static void ApplyBoost(ActiveGameInfo game)
    {
        Task.Run(() =>
        {
            try
            {
                // 1. Lower CloudRedirect's own process priority so it never competes for GPU/CPU frames
                using (var currentProc = Process.GetCurrentProcess())
                {
                    currentProc.PriorityClass = ProcessPriorityClass.BelowNormal;
                }

                // 2. Locate the game process and elevate it
                var proc = FindProcess(game);
                if (proc != null)
                {
                    _boostedPid = proc.Id;
                    if (proc.PriorityClass < ProcessPriorityClass.AboveNormal)
                    {
                        proc.PriorityClass = ProcessPriorityClass.AboveNormal;
                        Debug.WriteLine($"[Booster] Elevated game process '{proc.ProcessName}' (PID {proc.Id}) to AboveNormal priority.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Booster] Failed to elevate game process: {ex.Message}");
            }
        });
    }

    private static void RevertBoost()
    {
        Task.Run(() =>
        {
            try
            {
                // Restore CloudRedirect process priority to Normal
                using (var currentProc = Process.GetCurrentProcess())
                {
                    currentProc.PriorityClass = ProcessPriorityClass.Normal;
                }

                if (_boostedPid.HasValue)
                {
                    try
                    {
                        var proc = Process.GetProcessById(_boostedPid.Value);
                        if (!proc.HasExited && proc.PriorityClass > ProcessPriorityClass.Normal)
                        {
                            proc.PriorityClass = ProcessPriorityClass.Normal;
                        }
                    }
                    catch { }
                    _boostedPid = null;
                }
            }
            catch { }
        });
    }

    private static Process? FindProcess(ActiveGameInfo game)
    {
        if (!string.IsNullOrEmpty(game.ProcessName))
        {
            var clean = game.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? game.ProcessName[..^4]
                : game.ProcessName;

            var procs = Process.GetProcessesByName(clean);
            if (procs.Length > 0)
                return procs[0];
        }

        // Search by window title containing game name
        if (!string.IsNullOrEmpty(game.GameName))
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                        p.MainWindowTitle.Contains(game.GameName, StringComparison.OrdinalIgnoreCase))
                    {
                        return p;
                    }
                }
                catch { }
            }
        }

        return null;
    }
}
