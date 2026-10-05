using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CloudRedirect.Services;

/// <summary>
/// Manages global system-wide keyboard shortcuts (e.g. Ctrl+Shift+C) using Win32 RegisterHotKey.
/// Allows summoning, focusing, or toggling CloudRedirect anytime from any application or game.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private static GlobalHotkeyService? _instance;
    public static GlobalHotkeyService Instance => _instance ??= new GlobalHotkeyService();

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xCD01;
    private const int HOTKEY_GAMESPACE_ID = 0xCD02;
    private const int HOTKEY_STICKYNOTE_ID = 0xCD03;

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private MainWindow? _mainWindow;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _isRegistered;
    private bool _isGameSpaceRegistered;
    private bool _isStickyNoteRegistered;
    private string _currentShortcut = "Ctrl+Shift+C";
    private long _lastHotkeyTicks;
    private long _lastGameSpaceHotkeyTicks;
    private long _lastStickyNoteHotkeyTicks;
    private bool _isHooked;

    public string CurrentShortcut => _currentShortcut;
    public bool IsRegistered => _isRegistered;
    public bool IsGameSpaceRegistered => _isGameSpaceRegistered;
    public bool IsStickyNoteRegistered => _isStickyNoteRegistered;

    public event Action<string, bool>? OnHotkeyRegistrationChanged;

    public void Initialize(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        var helper = new WindowInteropHelper(mainWindow);
        _hwnd = helper.EnsureHandle();

        if (!_isHooked)
        {
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            // Hook the Dispatcher thread pump directly to catch WM_HOTKEY even when MainWindow is hidden/in tray
            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcher_ThreadFilterMessage;
            _isHooked = true;
        }

        if (AppSettings.GlobalHotkeyEnabled)
        {
            RegisterWithFallback(AppSettings.GlobalHotkey, out var actual);
            if (!string.Equals(actual, AppSettings.GlobalHotkey, StringComparison.OrdinalIgnoreCase))
            {
                AppSettings.GlobalHotkey = actual;
            }
        }

        RegisterGameSpaceHotkey();
        RegisterStickyNoteHotkey();
    }

    public bool RegisterWithFallback(string requestedShortcut, out string actualShortcut)
    {
        actualShortcut = requestedShortcut;
        if (Register(requestedShortcut))
        {
            return true;
        }

        // Try standard fallbacks if primary is registered by another program
        var fallbacks = new[] { "Ctrl+Alt+C", "Ctrl+Shift+R", "Ctrl+Shift+S", "Alt+Shift+C", "Ctrl+~" };
        foreach (var fb in fallbacks)
        {
            if (fb.Equals(requestedShortcut, StringComparison.OrdinalIgnoreCase)) continue;
            if (Register(fb))
            {
                actualShortcut = fb;
                return true;
            }
        }

        return false;
    }

    public bool Register(string shortcut)
    {
        if (_hwnd == IntPtr.Zero) return false;

        Unregister();

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            shortcut = "Ctrl+Shift+C";
        }

        if (!ParseShortcut(shortcut, out uint modifiers, out uint vk))
        {
            App.LogStartup($"GlobalHotkeyService.Register: Failed to parse shortcut '{shortcut}'");
            return false;
        }

        // Register with MOD_NOREPEAT so holding the shortcut doesn't spam WM_HOTKEY
        bool success = RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
        if (!success)
        {
            // Fallback without MOD_NOREPEAT if unsupported
            success = RegisterHotKey(_hwnd, HOTKEY_ID, modifiers, vk);
        }

        var err = success ? 0 : Marshal.GetLastWin32Error();
        App.LogStartup($"GlobalHotkeyService.Register: shortcut='{shortcut}', success={success}, win32Error={err}");

        if (success)
        {
            _isRegistered = true;
            _currentShortcut = shortcut;
            AppSettings.GlobalHotkey = shortcut;
            AppSettings.GlobalHotkeyEnabled = true;
        }
        else
        {
            _isRegistered = false;
        }

        OnHotkeyRegistrationChanged?.Invoke(_currentShortcut, _isRegistered);
        return success;
    }

    public void Unregister()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
            OnHotkeyRegistrationChanged?.Invoke(_currentShortcut, false);
        }
    }

    public bool RegisterGameSpaceHotkey()
    {
        if (_hwnd == IntPtr.Zero) return false;
        UnregisterGameSpaceHotkey();

        if (!AppSettings.GameSpaceEnabled) return false;

        string shortcut = AppSettings.GameSpaceHotkey;
        if (string.IsNullOrWhiteSpace(shortcut)) shortcut = "Ctrl+Space";

        if (!ParseShortcut(shortcut, out uint modifiers, out uint vk))
        {
            App.LogStartup($"GlobalHotkeyService: Failed to parse GameSpace shortcut '{shortcut}'");
            return false;
        }

        bool success = RegisterHotKey(_hwnd, HOTKEY_GAMESPACE_ID, modifiers | MOD_NOREPEAT, vk);
        if (!success)
        {
            success = RegisterHotKey(_hwnd, HOTKEY_GAMESPACE_ID, modifiers, vk);
        }

        var err = success ? 0 : Marshal.GetLastWin32Error();
        App.LogStartup($"GlobalHotkeyService: GameSpace hotkey '{shortcut}' registered={success}, err={err}");
        _isGameSpaceRegistered = success;
        return success;
    }

    public void UnregisterGameSpaceHotkey()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_GAMESPACE_ID);
            _isGameSpaceRegistered = false;
        }
    }

    public bool RegisterStickyNoteHotkey()
    {
        if (_hwnd == IntPtr.Zero) return false;
        UnregisterStickyNoteHotkey();

        if (!AppSettings.StickyNoteHotkeyEnabled) return false;

        string shortcut = AppSettings.StickyNoteHotkey;
        if (string.IsNullOrWhiteSpace(shortcut)) shortcut = "Alt+N";

        if (!ParseShortcut(shortcut, out uint modifiers, out uint vk))
        {
            App.LogStartup($"GlobalHotkeyService: Failed to parse StickyNote shortcut '{shortcut}'");
            return false;
        }

        bool success = RegisterHotKey(_hwnd, HOTKEY_STICKYNOTE_ID, modifiers | MOD_NOREPEAT, vk);
        if (!success)
        {
            success = RegisterHotKey(_hwnd, HOTKEY_STICKYNOTE_ID, modifiers, vk);
        }

        // If primary Alt+N is blocked by another app, try fallbacks
        if (!success)
        {
            var fallbacks = new[] { "Ctrl+Alt+N", "Alt+Shift+N", "Ctrl+Shift+N" };
            foreach (var fb in fallbacks)
            {
                if (ParseShortcut(fb, out var mod, out var k))
                {
                    success = RegisterHotKey(_hwnd, HOTKEY_STICKYNOTE_ID, mod | MOD_NOREPEAT, k);
                    if (success)
                    {
                        shortcut = fb;
                        break;
                    }
                }
            }
        }

        var err = success ? 0 : Marshal.GetLastWin32Error();
        App.LogStartup($"GlobalHotkeyService: StickyNote hotkey '{shortcut}' registered={success}, err={err}");
        _isStickyNoteRegistered = success;
        return success;
    }

    public void UnregisterStickyNoteHotkey()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_STICKYNOTE_ID);
            _isStickyNoteRegistered = false;
        }
    }

    private void ComponentDispatcher_ThreadFilterMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == WM_HOTKEY)
        {
            int id = (int)msg.wParam;
            if (id == HOTKEY_ID)
            {
                HandleHotkeyPressed();
                handled = true;
            }
            else if (id == HOTKEY_GAMESPACE_ID)
            {
                HandleGameSpaceHotkeyPressed();
                handled = true;
            }
            else if (id == HOTKEY_STICKYNOTE_ID)
            {
                HandleStickyNoteHotkeyPressed();
                handled = true;
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID)
            {
                HandleHotkeyPressed();
                handled = true;
            }
            else if (id == HOTKEY_GAMESPACE_ID)
            {
                HandleGameSpaceHotkeyPressed();
                handled = true;
            }
            else if (id == HOTKEY_STICKYNOTE_ID)
            {
                HandleStickyNoteHotkeyPressed();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void HandleStickyNoteHotkeyPressed()
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastStickyNoteHotkeyTicks < 350) return; // 350ms debounce
        _lastStickyNoteHotkeyTicks = nowTicks;

        App.LogStartup("GlobalHotkeyService.HandleStickyNoteHotkeyPressed triggered.");
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var game = ActiveGameTrackerService.CurrentGame;
            var gameName = (game != null && !string.IsNullOrWhiteSpace(game.Name)) ? game.Name : "Global";
            var note = StickyNotesService.CreateNote(gameName, "New Note", "");
            StickyNotesService.ShowNoteWindow(note);
        });
    }

    private void HandleGameSpaceHotkeyPressed()
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastGameSpaceHotkeyTicks < 350) return; // 350ms debounce
        _lastGameSpaceHotkeyTicks = nowTicks;

        App.LogStartup("GlobalHotkeyService.HandleGameSpaceHotkeyPressed triggered.");
        GameSpaceService.Instance.Toggle();
    }

    public void HandleHotkeyPressed()
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastHotkeyTicks < 350) return; // 350ms debounce
        _lastHotkeyTicks = nowTicks;

        App.LogStartup("GlobalHotkeyService.HandleHotkeyPressed triggered.");

        if (_mainWindow == null) return;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            // If window is currently open, visible, not minimized, and is active: toggle back to tray
            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized && _mainWindow.IsActive)
            {
                TrayIconService.Instance.MinimizeToTray();
                return;
            }

            // Otherwise summon and restore to foreground with robust focus stealing
            App.BringToForeground();
        });
    }

    private static bool ParseShortcut(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = text.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        string keyPart = "";
        foreach (var p in parts)
        {
            var part = p.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_CONTROL;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_SHIFT;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_ALT;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_WIN;
            }
            else
            {
                keyPart = part;
            }
        }

        if (string.IsNullOrEmpty(keyPart)) return false;

        if (keyPart.Length == 1)
        {
            char c = char.ToUpperInvariant(keyPart[0]);
            if (c >= 'A' && c <= 'Z')
            {
                vk = (uint)c;
                return true;
            }
            if (c >= '0' && c <= '9')
            {
                vk = (uint)c;
                return true;
            }
            if (c == '~' || c == '`')
            {
                vk = 0xC0; // VK_OEM_3
                return true;
            }
        }

        if (keyPart.Equals("F1", StringComparison.OrdinalIgnoreCase)) vk = 0x70;
        else if (keyPart.Equals("F2", StringComparison.OrdinalIgnoreCase)) vk = 0x71;
        else if (keyPart.Equals("F3", StringComparison.OrdinalIgnoreCase)) vk = 0x72;
        else if (keyPart.Equals("F4", StringComparison.OrdinalIgnoreCase)) vk = 0x73;
        else if (keyPart.Equals("F5", StringComparison.OrdinalIgnoreCase)) vk = 0x74;
        else if (keyPart.Equals("F6", StringComparison.OrdinalIgnoreCase)) vk = 0x75;
        else if (keyPart.Equals("F7", StringComparison.OrdinalIgnoreCase)) vk = 0x76;
        else if (keyPart.Equals("F8", StringComparison.OrdinalIgnoreCase)) vk = 0x77;
        else if (keyPart.Equals("F9", StringComparison.OrdinalIgnoreCase)) vk = 0x78;
        else if (keyPart.Equals("F10", StringComparison.OrdinalIgnoreCase)) vk = 0x79;
        else if (keyPart.Equals("F11", StringComparison.OrdinalIgnoreCase)) vk = 0x7A;
        else if (keyPart.Equals("F12", StringComparison.OrdinalIgnoreCase)) vk = 0x7B;
        else if (keyPart.Equals("Space", StringComparison.OrdinalIgnoreCase)) vk = 0x20;
        else if (keyPart.Equals("Tab", StringComparison.OrdinalIgnoreCase)) vk = 0x09;
        else return false;

        return true;
    }

    public void Dispose()
    {
        Unregister();
        UnregisterGameSpaceHotkey();
        UnregisterStickyNoteHotkey();
        if (_isHooked)
        {
            try
            {
                ComponentDispatcher.ThreadFilterMessage -= ComponentDispatcher_ThreadFilterMessage;
                _hwndSource?.RemoveHook(WndProc);
            }
            catch { }
            _isHooked = false;
        }
    }
}
