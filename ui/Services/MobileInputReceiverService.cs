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

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private const byte VK_BACK = 0x08;
    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
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

        // Mouse Command: M:<1|0>:<1|2>
        if (msg.StartsWith("M:"))
        {
            var parts = msg.Split(':');
            if (parts.Length >= 3)
            {
                bool down = parts[1] == "1";
                int btn = parts[2] == "2" ? 2 : 1;
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
        byte vk = ResolveVirtualKey(androidCode, label);
        if (vk != 0)
        {
            keybd_event(vk, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        else if (label == "FIRE" || label == "L-CLK")
        {
            DispatchMouse(down, 1);
        }
        else if (label == "AIM" || label == "ADS" || label == "R-CLK")
        {
            DispatchMouse(down, 2);
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
    }

    private void DispatchDpad(bool down, string direction)
    {
        byte vk = direction switch
        {
            "UP" => VK_W,
            "DOWN" => VK_S,
            "LEFT" => VK_A,
            "RIGHT" => VK_D,
            _ => 0
        };

        if (vk != 0)
        {
            keybd_event(vk, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    private void DispatchGamepadButton(bool down, string label)
    {
        switch (label)
        {
            case "A":
                keybd_event(VK_SPACE, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
                break;
            case "B":
                keybd_event(VK_ESCAPE, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
                break;
            case "X":
                keybd_event(0x45, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero); // E (Interact/Reload)
                break;
            case "Y":
                keybd_event(0x46, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero); // F (Action)
                break;
            case "LB":
                keybd_event(0x51, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero); // Q
                break;
            case "RB":
                keybd_event(0x52, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero); // R
                break;
            case "LT":
                DispatchMouse(down, 2); // Right mouse (Aim)
                break;
            case "RT":
                DispatchMouse(down, 1); // Left mouse (Shoot)
                break;
            case "VIEW":
                keybd_event(VK_TAB, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
                break;
            case "MENU":
                keybd_event(VK_ESCAPE, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
                break;
            case "STEAM":
                // Shift + Tab for Steam Overlay
                keybd_event(VK_SHIFT, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_TAB, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
                break;
            default:
                DispatchKey(down, 0, label);
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
            keybd_event(VK_W, 0, _wActive ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        if (wantS != _sActive)
        {
            _sActive = wantS;
            keybd_event(VK_S, 0, _sActive ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        if (wantA != _aActive)
        {
            _aActive = wantA;
            keybd_event(VK_A, 0, _aActive ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        if (wantD != _dActive)
        {
            _dActive = wantD;
            keybd_event(VK_D, 0, _dActive ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
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
            case "TAB":
            case "INVENTORY": return VK_TAB;
            case "ESC":
            case "MENU": return VK_ESCAPE;
            case "ENTER":
            case "CONFIRM": return VK_RETURN;
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

        // Single alphanumeric character
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
            29 => 0x41, // KEYCODE_A
            30 => 0x42, // KEYCODE_B
            31 => 0x43, // KEYCODE_C
            32 => 0x44, // KEYCODE_D
            33 => 0x45, // KEYCODE_E
            34 => 0x46, // KEYCODE_F
            35 => 0x47, // KEYCODE_G
            41 => 0x4D, // KEYCODE_M
            45 => 0x51, // KEYCODE_Q
            46 => 0x52, // KEYCODE_R
            47 => 0x53, // KEYCODE_S
            50 => 0x56, // KEYCODE_V
            51 => 0x57, // KEYCODE_W
            52 => 0x58, // KEYCODE_X
            53 => 0x59, // KEYCODE_Y
            54 => 0x5A, // KEYCODE_Z
            62 => VK_SPACE, // KEYCODE_SPACE
            66 => VK_RETURN, // KEYCODE_ENTER
            111 => VK_ESCAPE, // KEYCODE_ESCAPE
            61 => VK_TAB, // KEYCODE_TAB
            59 => VK_SHIFT, // KEYCODE_SHIFT_LEFT
            113 => VK_CONTROL, // KEYCODE_CTRL_LEFT
            8 => 0x31, // KEYCODE_1
            9 => 0x32, // KEYCODE_2
            10 => 0x33, // KEYCODE_3
            11 => 0x34, // KEYCODE_4
            _ => 0
        };
    }

    private void ReleaseAllHeldKeys()
    {
        if (_wActive) { keybd_event(VK_W, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); _wActive = false; }
        if (_sActive) { keybd_event(VK_S, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); _sActive = false; }
        if (_aActive) { keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); _aActive = false; }
        if (_dActive) { keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); _dActive = false; }
    }
}
