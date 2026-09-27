using System;
using System.Runtime.InteropServices;

namespace CloudRedirect.Services;

/// <summary>
/// High-speed Win32 SendInput simulator for keyboard, mouse, and macro injection.
/// Translates SUO Link remote mobile inputs into native Windows input events.
/// </summary>
public static class RemoteInputSimulator
{
    #region Win32 API Definitions

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern ushort MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    #endregion

    /// <summary>
    /// Send keyboard key down or key up using Virtual Key code.
    /// Uses hardware scan codes for maximum game compatibility (DirectInput / RawInput).
    /// </summary>
    public static void SendKey(ushort vkCode, bool isDown)
    {
        ushort scanCode = MapVirtualKey(vkCode, 0);
        uint flags = KEYEVENTF_SCANCODE;
        if (!isDown) flags |= KEYEVENTF_KEYUP;

        // Check if extended key (arrows, numpad enter, insert/delete/etc)
        if (vkCode is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send relative mouse movement (ideal for FPS / 3D look camera).
    /// </summary>
    public static void SendMouseMoveRelative(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send absolute mouse movement (ideal for strategy, RPG, cursor point & click).
    /// normalizedX and normalizedY are in range 0.0f to 1.0f.
    /// </summary>
    public static void SendMouseMoveAbsolute(float normalizedX, float normalizedY)
    {
        int absX = (int)Math.Clamp(normalizedX * 65535f, 0f, 65535f);
        int absY = (int)Math.Clamp(normalizedY * 65535f, 0f, 65535f);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send mouse button down / up.
    /// button: 0=Left, 1=Right, 2=Middle
    /// </summary>
    public static void SendMouseButton(int button, bool isDown)
    {
        uint flag = button switch
        {
            0 => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            1 => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            2 => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };

        if (flag == 0) return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flag,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send mouse scroll wheel delta. Positive=Up, Negative=Down.
    /// </summary>
    public static void SendMouseWheel(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = (uint)delta,
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Parse common key names (WASD, Space, Shift, Tab, etc.) to Win32 Virtual Key code.
    /// </summary>
    public static ushort ParseKeyToVk(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return 0;
        string upper = keyName.Trim().ToUpperInvariant();

        return upper switch
        {
            // Letters
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44, "E" => 0x45,
            "F" => 0x46, "G" => 0x47, "H" => 0x48, "I" => 0x49, "J" => 0x4A,
            "K" => 0x4B, "L" => 0x4C, "M" => 0x4D, "N" => 0x4E, "O" => 0x4F,
            "P" => 0x50, "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58, "Y" => 0x59, "Z" => 0x5A,

            // Digits
            "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33, "4" => 0x34,
            "5" => 0x35, "6" => 0x36, "7" => 0x37, "8" => 0x38, "9" => 0x39,

            // Common Gaming Keys
            "SPACE" => 0x20,
            "SHIFT" or "LSHIFT" => 0x10,
            "RSHIFT" => 0xA1,
            "CTRL" or "LCTRL" => 0x11,
            "RCTRL" => 0xA3,
            "ALT" or "LALT" => 0x12,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "ENTER" or "RETURN" => 0x0D,
            "BACKSPACE" => 0x08,
            "CAPSLOCK" or "CAPS" => 0x14,

            // Arrows
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,

            // Navigation
            "INSERT" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,

            // Function Keys
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,

            // Punctuation
            "TILDE" or "`" => 0xC0,
            "-" or "MINUS" => 0xBD,
            "=" or "EQUALS" => 0xBB,
            "[" or "LBRACKET" => 0xDB,
            "]" or "RBRACKET" => 0xDD,
            "\\" or "BACKSLASH" => 0xDC,
            ";" or "SEMICOLON" => 0xBA,
            "'" or "QUOTE" => 0xDE,
            "," or "COMMA" => 0xBC,
            "." or "PERIOD" => 0xBE,
            "/" or "SLASH" => 0xBF,

            _ => 0
        };
    }
}
