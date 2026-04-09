using System.Runtime.InteropServices;
using Serilog;

namespace ClickRun.Hotkey;

/// <summary>
/// Global hotkey kill switch that toggles ClickRun enabled/disabled state.
/// Uses Win32 RegisterHotKey API with a dedicated message-loop thread.
/// </summary>
public sealed class KillSwitch : IDisposable
{
    // --- P/Invoke declarations (Task 8.1) ---

    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int HOTKEY_ID = 0x1;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    // --- State ---

    private volatile bool _isEnabled = true;
    private readonly ILogger _logger;
    private readonly uint _modifiers;
    private readonly uint _virtualKey;
    private readonly Thread _messageLoopThread;
    private uint _messageLoopThreadId;
    private readonly ManualResetEventSlim _threadStarted = new(false);
    private bool _registered;
    private bool _disposed;

    /// <summary>
    /// Gets whether ClickRun is currently enabled.
    /// Thread-safe via volatile read.
    /// </summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Creates a new KillSwitch and registers the global hotkey.
    /// </summary>
    /// <param name="hotkeyString">Hotkey string, e.g. "Ctrl+Alt+R"</param>
    /// <param name="logger">Serilog logger instance</param>
    public KillSwitch(string hotkeyString, ILogger logger)
    {
        _logger = logger;

        // Task 8.2: Parse hotkey string
        (_modifiers, _virtualKey) = ParseHotkey(hotkeyString);

        // Task 8.3: Start message loop thread and register hotkey
        _messageLoopThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "KillSwitch-MessageLoop"
        };
        _messageLoopThread.Start();

        // Wait for the thread to start and register the hotkey
        _threadStarted.Wait(TimeSpan.FromSeconds(5));
    }

    // --- Task 8.2: Parse hotkey string ---

    /// <summary>
    /// Parses a hotkey string like "Ctrl+Alt+R" into modifier flags and a virtual key code.
    /// </summary>
    internal static (uint modifiers, uint virtualKey) ParseHotkey(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            throw new ArgumentException("Hotkey string cannot be empty.", nameof(hotkeyString));

        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Hotkey string cannot be empty.", nameof(hotkeyString));

        uint modifiers = MOD_NOREPEAT;
        string? keyName = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    keyName = part;
                    break;
            }
        }

        if (keyName is null)
            throw new ArgumentException($"Hotkey string '{hotkeyString}' does not contain a key.", nameof(hotkeyString));

        uint vk = MapKeyNameToVirtualKey(keyName);
        return (modifiers, vk);
    }

    private static uint MapKeyNameToVirtualKey(string keyName)
    {
        var upper = keyName.ToUpperInvariant();

        // Single letter A-Z
        if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z')
            return (uint)upper[0]; // VK_A through VK_Z are 0x41-0x5A

        // Single digit 0-9
        if (upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9')
            return (uint)upper[0]; // VK_0 through VK_9 are 0x30-0x39

        // Function keys F1-F24
        if (upper.StartsWith('F') && int.TryParse(upper.AsSpan(1), out int fNum) && fNum >= 1 && fNum <= 24)
            return (uint)(0x6F + fNum); // VK_F1 = 0x70

        // Named keys
        return upper switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "PAUSE" => 0x13,
            "CAPSLOCK" => 0x14,
            "NUMLOCK" => 0x90,
            "SCROLLLOCK" => 0x91,
            _ => throw new ArgumentException($"Unknown key name: '{keyName}'")
        };
    }

    // --- Task 8.3 & 8.4: Message loop with hotkey registration and toggle ---

    private void MessageLoop()
    {
        _messageLoopThreadId = GetCurrentThreadId();

        // Task 8.3: Register global hotkey at startup
        _registered = RegisterHotKey(IntPtr.Zero, HOTKEY_ID, _modifiers, _virtualKey);

        if (!_registered)
        {
            int error = Marshal.GetLastWin32Error();
            _logger.Warning("Failed to register global hotkey (Win32 error {ErrorCode}). Kill switch will not be available.", error);
        }
        else
        {
            _logger.Information("Kill switch hotkey registered successfully.");
        }

        // Signal that the thread has started and registration is complete
        _threadStarted.Set();

        // Message pump
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
        {
            if (msg.message == WM_HOTKEY && msg.wParam == (IntPtr)HOTKEY_ID)
            {
                // Task 8.4: Toggle isEnabled
                _isEnabled = !_isEnabled;

                if (_isEnabled)
                    _logger.Information("ClickRun ENABLED via kill switch hotkey.");
                else
                    _logger.Information("ClickRun DISABLED via kill switch hotkey.");
            }
        }
    }

    // --- Task 8.5: Unregister hotkey on shutdown ---

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unregister the hotkey
        if (_registered)
        {
            // We must unregister from the same thread that registered,
            // but since we registered with hWnd=0, we can unregister from any thread.
            UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
            _logger.Information("Kill switch hotkey unregistered.");
        }

        // Stop the message loop by posting WM_QUIT
        if (_messageLoopThreadId != 0)
        {
            PostThreadMessage(_messageLoopThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _messageLoopThread.Join(TimeSpan.FromSeconds(2));
        }

        _threadStarted.Dispose();
    }
}
