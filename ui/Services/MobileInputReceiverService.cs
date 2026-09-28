using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Ultra low-latency (<1ms) UDP input receiver for GameHub Mobile HUD.
/// Listens on UDP port 48999, handles broadcast discovery, and translates
/// mobile touch controls directly into Windows keyboard, mouse, and joystick inputs
/// so games streamed via Steam Link, Moonlight, or Parsec respond seamlessly.
/// </summary>
public class MobileInputReceiverService
{
    public const int DefaultPort = 48999;
    private static readonly Lazy<MobileInputReceiverService> _instance = new(() => new MobileInputReceiverService());
    public static MobileInputReceiverService Instance => _instance.Value;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    // Track active joystick WASD states to only fire on change
    private bool _wActive, _sActive, _aActive, _dActive;

    public bool IsRunning => _isRunning;

    #region Win32 Native Methods
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

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
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint INPUT_MOUSE = 0;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    private const byte VK_BACK = 0x08;
    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_CAPITAL = 0x14; // Caps Lock
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_SPACE = 0x20;
    private const byte VK_LEFT = 0x25;
    private const byte VK_UP = 0x26;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_DOWN = 0x28;
    private const byte VK_W = 0x57;
    private const byte VK_A = 0x41;
    private const byte VK_S = 0x53;
    private const byte VK_D = 0x44;

    private static void SendKeyEvent(byte vk, bool down)
    {
        if (vk == 0) return;
        byte scan = (byte)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC = 0
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        bool isExtended = (vk == VK_UP || vk == VK_DOWN || vk == VK_LEFT || vk == VK_RIGHT ||
                           vk == VK_RETURN || vk == 0x2E /*DEL*/ || vk == 0x2D /*INS*/ ||
                           vk == 0x24 /*HOME*/ || vk == 0x23 /*END*/ || vk == 0x21 /*PRIOR*/ || vk == 0x22 /*NEXT*/);
        if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;

        try
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.wScan = scan;
            inputs[0].u.ki.dwFlags = flags;
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = UIntPtr.Zero;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
        catch { }

        // Also call legacy keybd_event to maximize compatibility across DirectInput and desktop
        keybd_event(vk, scan, flags, UIntPtr.Zero);
    }

    /// <summary>
    /// Types unicode text directly into Windows games and dialogs (e.g. character name creation, in-game chat)
    /// </summary>
    public static void TypeText(string text, bool pressEnter)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (pressEnter)
            {
                SendKeyEvent(VK_RETURN, true);
                Thread.Sleep(10);
                SendKeyEvent(VK_RETURN, false);
            }
            return;
        }

        foreach (char c in text)
        {
            try
            {
                INPUT[] inputs = new INPUT[2];
                // Key down
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = 0;
                inputs[0].u.ki.wScan = (ushort)c;
                inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
                inputs[0].u.ki.time = 0;
                inputs[0].u.ki.dwExtraInfo = UIntPtr.Zero;

                // Key up
                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = 0;
                inputs[1].u.ki.wScan = (ushort)c;
                inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                inputs[1].u.ki.time = 0;
                inputs[1].u.ki.dwExtraInfo = UIntPtr.Zero;

                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                Thread.Sleep(5); // Small delay so game input queues register the keystrokes
            }
            catch { }
        }

        if (pressEnter)
        {
            Thread.Sleep(20);
            SendKeyEvent(VK_RETURN, true);
            Thread.Sleep(15);
            SendKeyEvent(VK_RETURN, false);
        }
    }
    #endregion

    public void Start(int port = DefaultPort)
    {
        if (_isRunning) return;

        try
        {
            _cts = new CancellationTokenSource();
            _udpClient = new UdpClient(port)
            {
                EnableBroadcast = true
            };
            _isRunning = true;
            App.LogStartup($"MobileInputReceiverService started on UDP port {port}");

            Task.Run(() => ListenLoopAsync(_udpClient, _cts.Token));
        }
        catch (Exception ex)
        {
            App.LogStartup($"Failed to start MobileInputReceiverService: {ex.Message}");
            _isRunning = false;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            _udpClient?.Dispose();
        }
        catch { }

        ReleaseAllHeldKeys();
        App.LogStartup("MobileInputReceiverService stopped");
    }

    private async Task ListenLoopAsync(UdpClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(ct);
                var raw = Encoding.UTF8.GetString(result.Buffer);
                ProcessPacket(raw.Trim(), result.RemoteEndPoint, client);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Silently recover on socket glitches
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(50, ct);
                }
            }
        }
    }

    private void ProcessPacket(string msg, IPEndPoint sender, UdpClient client)
    {
        if (string.IsNullOrEmpty(msg)) return;

        // Auto-discovery from mobile HUD
        if (msg == "DISCOVER_GAMEHUB")
        {
            try
            {
                var response = Encoding.UTF8.GetBytes($"GAMEHUB_PC_ACK|{Environment.MachineName}|CloudRedirect");
                client.Send(response, response.Length, sender);
            }
            catch { }
            return;
        }

        if (msg == "PING")
        {
            try
            {
                var response = Encoding.UTF8.GetBytes("PONG");
                client.Send(response, response.Length, sender);
            }
            catch { }
            return;
        }

        // Unicode text input from floating in-game keyboard:
        // TEXT:<string> or TEXT_ENTER:<string>
        if (msg.StartsWith("TEXT_ENTER:"))
        {
            var text = msg.Substring("TEXT_ENTER:".Length);
            Task.Run(() => TypeText(text, pressEnter: true));
            return;
        }

        if (msg.StartsWith("TEXT:"))
        {
            var text = msg.Substring("TEXT:".Length);
            Task.Run(() => TypeText(text, pressEnter: false));
            return;
        }

        // Key Command: K:<1|0>:<keyCode>:<label>
        if (msg.StartsWith("K:"))
        {
            var parts = msg.Split(':');
            if (parts.Length >= 4)
            {
                bool down = parts[1] == "1";
                int.TryParse(parts[2], out var androidCode);
                string label = parts[3].ToUpperInvariant();
                DispatchKey(down, androidCode, label);
            }
            return;
        }

        // Mouse Command: M:<1|0>:<1|2|3>
        if (msg.StartsWith("M:"))
        {
            var parts = msg.Split(':');
            if (parts.Length >= 3)
            {
                bool down = parts[1] == "1";
                int.TryParse(parts[2], out var btn);
                if (btn < 1 || btn > 3) btn = 1;
                DispatchMouse(down, btn);
            }
            return;
        }

        // Gamepad Button Command: PAD:<1|0>:<label>
        if (msg.StartsWith("PAD:"))
        {
            var parts = msg.Split(':');
            if (parts.Length >= 3)
            {
                bool down = parts[1] == "1";
                string label = parts[2].ToUpperInvariant();
                DispatchGamepadButton(down, label);
            }
            return;
        }

        // D-Pad Command: DPAD:<1|0>:<direction>
        if (msg.StartsWith("DPAD:"))
        {
            var parts = msg.Split(':');
            if (parts.Length >= 3)
            {
                bool down = parts[1] == "1";
                string dir = parts[2].ToUpperInvariant();
                DispatchDpad(down, dir);
            }
            return;
        }

        // Left Joystick (Movement / WASD): J:<x>:<y>
        if (msg.StartsWith("J:"))
        {
            var parts = msg.Substring(2).Split(':');
            if (parts.Length >= 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var jx) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var jy))
            {
                DispatchJoystickWasd(jx, jy);
            }
            return;
        }

        // Right Joystick (Camera / Mouse): JR:<x>:<y>
        if (msg.StartsWith("JR:"))
        {
            var parts = msg.Substring(3).Split(':');
            if (parts.Length >= 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rx) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ry))
            {
                DispatchJoystickCamera(rx, ry);
            }
            return;
        }
    }

    private void DispatchKey(bool down, int androidCode, string label)
    {
        // 1. Explicit Gamepad and Shoulder Button check
        // Android KeyCodes:
        // 96 = BUTTON_A, 97 = BUTTON_B, 98 = BUTTON_C, 99 = BUTTON_X, 100 = BUTTON_Y, 101 = BUTTON_Z,
        // 102 = BUTTON_L1 (LB), 103 = BUTTON_R1 (RB), 104 = BUTTON_L2 (LT), 105 = BUTTON_R2 (RT),
        // 106 = BUTTON_THUMBL, 107 = BUTTON_THUMBR, 108 = BUTTON_START, 109 = BUTTON_SELECT, 110 = BUTTON_MODE
        if ((androidCode >= 96 && androidCode <= 110) ||
            label == "LB" || label == "RB" || label == "LT" || label == "RT" ||
            label == "START" || label == "SELECT" || label == "VIEW" || label == "MENU" ||
            label == "STEAM" || label == "THUMBL" || label == "THUMBR" || label == "LS" || label == "RS" ||
            (label == "A" && androidCode == 96) ||
            (label == "B" && androidCode == 97) ||
            (label == "X" && androidCode == 99) ||
            (label == "Y" && androidCode == 100))
        {
            DispatchGamepadButton(down, label);
            return;
        }

        byte vk = ResolveVirtualKey(androidCode, label);
        if (vk != 0)
        {
            SendKeyEvent(vk, down);
        }
        else if (label == "FIRE" || label == "L-CLK" || label == "MOUSE_LEFT")
        {
            DispatchMouse(down, 1);
        }
        else if (label == "AIM" || label == "ADS" || label == "R-CLK" || label == "MOUSE_RIGHT")
        {
            DispatchMouse(down, 2);
        }
        else if (label == "M-CLK" || label == "MOUSE_MIDDLE")
        {
            DispatchMouse(down, 3);
        }
    }

    private void DispatchMouse(bool down, int button)
    {
        if (button == 1) // Left click
        {
            mouse_event(down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
        else if (button == 2) // Right click
        {
            mouse_event(down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }
        else if (button == 3) // Middle click
        {
            mouse_event(down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
        }
    }

    private void DispatchDpad(bool down, string direction)
    {
        byte vk = direction switch
        {
            "UP" => VK_W,
            "DOWN" => VK_S,
            "LEFT" => VK_A,
            "RIGHT" => VK_D,
            "ARROW_UP" or "UP_ARROW" => VK_UP,
            "ARROW_DOWN" or "DOWN_ARROW" => VK_DOWN,
            "ARROW_LEFT" or "LEFT_ARROW" => VK_LEFT,
            "ARROW_RIGHT" or "RIGHT_ARROW" => VK_RIGHT,
            _ => 0
        };

        if (vk != 0)
        {
            SendKeyEvent(vk, down);
        }
    }

    private void DispatchGamepadButton(bool down, string label)
    {
        switch (label)
        {
            case "A":
                SendKeyEvent(VK_SPACE, down);
                break;
            case "B":
                SendKeyEvent(VK_ESCAPE, down);
                break;
            case "X":
                SendKeyEvent(0x45, down); // E (Interact/Reload)
                break;
            case "Y":
                SendKeyEvent(0x46, down); // F (Action)
                break;
            case "LB":
                SendKeyEvent(0x51, down); // Q
                break;
            case "RB":
                SendKeyEvent(0x52, down); // R
                break;
            case "LT":
                DispatchMouse(down, 2); // Right mouse (Aim)
                break;
            case "RT":
                DispatchMouse(down, 1); // Left mouse (Shoot)
                break;
            case "START":
            case "MENU":
                SendKeyEvent(VK_ESCAPE, down);
                break;
            case "SELECT":
            case "VIEW":
                SendKeyEvent(VK_TAB, down);
                break;
            case "THUMBL":
            case "LS":
                SendKeyEvent(VK_SHIFT, down); // Sprint
                break;
            case "THUMBR":
            case "RS":
                SendKeyEvent(0x56, down); // V (Melee)
                break;
            case "STEAM":
                // Shift + Tab for Steam Overlay
                SendKeyEvent(VK_SHIFT, down);
                SendKeyEvent(VK_TAB, down);
                break;
            default:
                byte vk = ResolveVirtualKey(0, label);
                if (vk != 0) SendKeyEvent(vk, down);
                break;
        }
    }

    private void DispatchJoystickWasd(float x, float y)
    {
        bool wantW = y < -0.32f;
        bool wantS = y > 0.32f;
        bool wantA = x < -0.32f;
        bool wantD = x > 0.32f;

        if (wantW != _wActive)
        {
            _wActive = wantW;
            SendKeyEvent(VK_W, _wActive);
        }
        if (wantS != _sActive)
        {
            _sActive = wantS;
            SendKeyEvent(VK_S, _sActive);
        }
        if (wantA != _aActive)
        {
            _aActive = wantA;
            SendKeyEvent(VK_A, _aActive);
        }
        if (wantD != _dActive)
        {
            _dActive = wantD;
            SendKeyEvent(VK_D, _dActive);
        }
    }

    private void DispatchJoystickCamera(float x, float y)
    {
        int dx = (int)Math.Round(x * 14.0f);
        int dy = (int)Math.Round(y * 14.0f);
        if (dx != 0 || dy != 0)
        {
            mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
        }
    }

    private byte ResolveVirtualKey(int androidCode, string label)
    {
        // First match by label
        switch (label)
        {
            case "SPACE":
            case "JUMP": return VK_SPACE;
            case "SHIFT":
            case "SPRINT": return VK_SHIFT;
            case "CTRL":
            case "CROUCH": return VK_CONTROL;
            case "ALT": return VK_MENU;
            case "TAB":
            case "INVENTORY": return VK_TAB;
            case "ESC":
            case "MENU":
            case "PAUSE": return VK_ESCAPE;
            case "ENTER":
            case "CONFIRM": return VK_RETURN;
            case "BACK":
            case "BACKSPACE": return VK_BACK;
            case "CAPS":
            case "CAPSLOCK": return VK_CAPITAL;
            case "UP":
            case "ARROW_UP": return VK_UP;
            case "DOWN":
            case "ARROW_DOWN": return VK_DOWN;
            case "LEFT":
            case "ARROW_LEFT": return VK_LEFT;
            case "RIGHT":
            case "ARROW_RIGHT": return VK_RIGHT;
            case "RELOAD": return 0x52; // R
            case "USE":
            case "INTERACT": return 0x45; // E
            case "ACTION": return 0x46; // F
            case "SKILL": return 0x51; // Q
            case "MAP": return 0x4D; // M
            case "PRONE": return 0x43; // C
            case "MELEE": return 0x56; // V
            case "COVER": return 0x5A; // Z
            case "GRENADE": return 0x47; // G
        }

        // Function keys F1 - F12
        if (label.StartsWith("F") && int.TryParse(label.Substring(1), out var fNum) && fNum >= 1 && fNum <= 12)
        {
            return (byte)(0x6F + fNum); // 0x70 is F1, 0x7B is F12
        }

        // Single alphanumeric character (A-Z, 0-9)
        if (label.Length == 1)
        {
            char c = char.ToUpperInvariant(label[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                return (byte)c;
            }
        }

        // Match by Android KeyCode fallback
        return androidCode switch
        {
            // A - Z
            29 => 0x41, // KEYCODE_A
            30 => 0x42, // KEYCODE_B
            31 => 0x43, // KEYCODE_C
            32 => 0x44, // KEYCODE_D
            33 => 0x45, // KEYCODE_E
            34 => 0x46, // KEYCODE_F
            35 => 0x47, // KEYCODE_G
            36 => 0x48, // KEYCODE_H
            37 => 0x49, // KEYCODE_I
            38 => 0x4A, // KEYCODE_J
            39 => 0x4B, // KEYCODE_K
            40 => 0x4C, // KEYCODE_L
            41 => 0x4D, // KEYCODE_M
            42 => 0x4E, // KEYCODE_N
            43 => 0x4F, // KEYCODE_O
            44 => 0x50, // KEYCODE_P
            45 => 0x51, // KEYCODE_Q
            46 => 0x52, // KEYCODE_R
            47 => 0x53, // KEYCODE_S
            48 => 0x54, // KEYCODE_T
            49 => 0x55, // KEYCODE_U
            50 => 0x56, // KEYCODE_V
            51 => 0x57, // KEYCODE_W
            52 => 0x58, // KEYCODE_X
            53 => 0x59, // KEYCODE_Y
            54 => 0x5A, // KEYCODE_Z

            // Numbers 0 - 9
            7 => 0x30,  // KEYCODE_0
            8 => 0x31,  // KEYCODE_1
            9 => 0x32,  // KEYCODE_2
            10 => 0x33, // KEYCODE_3
            11 => 0x34, // KEYCODE_4
            12 => 0x35, // KEYCODE_5
            13 => 0x36, // KEYCODE_6
            14 => 0x37, // KEYCODE_7
            15 => 0x38, // KEYCODE_8
            16 => 0x39, // KEYCODE_9

            // Action keys
            62 => VK_SPACE,   // KEYCODE_SPACE
            66 => VK_RETURN,  // KEYCODE_ENTER
            111 => VK_ESCAPE, // KEYCODE_ESCAPE
            61 => VK_TAB,     // KEYCODE_TAB
            59 => VK_SHIFT,   // KEYCODE_SHIFT_LEFT
            60 => VK_SHIFT,   // KEYCODE_SHIFT_RIGHT
            113 => VK_CONTROL,// KEYCODE_CTRL_LEFT
            114 => VK_CONTROL,// KEYCODE_CTRL_RIGHT
            57 => VK_MENU,    // KEYCODE_ALT_LEFT
            58 => VK_MENU,    // KEYCODE_ALT_RIGHT
            67 => VK_BACK,    // KEYCODE_DEL / BACKSPACE

            // D-Pad / Arrows
            19 => VK_UP,      // KEYCODE_DPAD_UP
            20 => VK_DOWN,    // KEYCODE_DPAD_DOWN
            21 => VK_LEFT,    // KEYCODE_DPAD_LEFT
            22 => VK_RIGHT,   // KEYCODE_DPAD_RIGHT

            // Function keys
            131 => 0x70, // KEYCODE_F1
            132 => 0x71, // KEYCODE_F2
            133 => 0x72, // KEYCODE_F3
            134 => 0x73, // KEYCODE_F4
            135 => 0x74, // KEYCODE_F5
            136 => 0x75, // KEYCODE_F6
            137 => 0x76, // KEYCODE_F7
            138 => 0x77, // KEYCODE_F8
            139 => 0x78, // KEYCODE_F9
            140 => 0x79, // KEYCODE_F10
            141 => 0x7A, // KEYCODE_F11
            142 => 0x7B, // KEYCODE_F12

            _ => 0
        };
    }

    private void ReleaseAllHeldKeys()
    {
        if (_wActive) { SendKeyEvent(VK_W, false); _wActive = false; }
        if (_sActive) { SendKeyEvent(VK_S, false); _sActive = false; }
        if (_aActive) { SendKeyEvent(VK_A, false); _aActive = false; }
        if (_dActive) { SendKeyEvent(VK_D, false); _dActive = false; }
    }
}
