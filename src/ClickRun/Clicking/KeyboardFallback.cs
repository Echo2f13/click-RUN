using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Clicking;

/// <summary>
/// Keyboard fallback for Electron/webview panels where UI Automation cannot click buttons.
/// Detects numbered option patterns (e.g., "1 Yes", "2 No") in context text, focuses the
/// target window, and sends the corresponding key press via SendInput.
/// </summary>
public sealed class KeyboardFallback
{
    private readonly ILogger _log;

    private static readonly Regex NumberedOptionPattern = new(
        @"(?:^|\n|\s)(\d)\s*[.)\s]\s*(Yes|Allow|Run|Accept|Approve|Continue|Trust)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // --- Win32 P/Invoke ---

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int SW_RESTORE = 9;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
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

    public KeyboardFallback(ILogger logger)
    {
        _log = logger.ForContext<KeyboardFallback>();
    }

    /// <summary>
    /// Attempts to find a numbered safe option in the context text, focus the target window,
    /// and send the corresponding key press.
    /// </summary>
    public bool TryFallback(
        string contextText,
        string windowTitle,
        string processName,
        IntPtr windowHandle,
        Configuration config,
        bool dryRun)
    {
        if (string.IsNullOrEmpty(contextText))
            return false;

        var fullContext = $"{windowTitle} {contextText}";

        // Dangerous context check — never send keys in dangerous contexts
        foreach (var dangerous in config.DangerousContextKeywords)
        {
            if (fullContext.Contains(dangerous, StringComparison.OrdinalIgnoreCase))
            {
                _log.Debug("KeyboardFallback: Dangerous context detected ('{Keyword}'), skipping", dangerous);
                return false;
            }
        }

        // Find numbered options
        var matches = NumberedOptionPattern.Matches(contextText);
        if (matches.Count == 0)
            return false;

        // Find the best (lowest number) safe option
        string? bestKey = null;
        string? bestLabel = null;
        int bestNumber = int.MaxValue;

        foreach (Match match in matches)
        {
            var number = int.Parse(match.Groups[1].Value);
            var label = match.Groups[2].Value;

            // Verify whitelisted
            bool isWhitelisted = false;
            foreach (var entry in config.Whitelist)
            {
                if (!string.Equals(entry.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var allowed in entry.ButtonLabels)
                {
                    if (label.Contains(allowed, StringComparison.OrdinalIgnoreCase) ||
                        allowed.Contains(label, StringComparison.OrdinalIgnoreCase))
                    { isWhitelisted = true; break; }
                }
                if (isWhitelisted) break;
            }

            if (!isWhitelisted)
            {
                _log.Debug("KeyboardFallback: Option '{Number} {Label}' not in whitelist, skipping", number, label);
                continue;
            }

            // Check blocklist
            bool isBlocked = false;
            foreach (var blocked in config.BlockedLabels)
            {
                if (label.Contains(blocked, StringComparison.OrdinalIgnoreCase))
                { isBlocked = true; break; }
            }
            if (isBlocked)
            {
                _log.Debug("KeyboardFallback: Option '{Number} {Label}' is blocked, skipping", number, label);
                continue;
            }

            if (number < bestNumber)
            {
                bestNumber = number;
                bestKey = match.Groups[1].Value;
                bestLabel = label;
            }
        }

        if (bestKey == null)
            return false;

        if (dryRun)
        {
            _log.Information("[DRY RUN] KeyboardFallback: Would send key '{Key}' for '{Label}' in {Process}",
                bestKey, bestLabel, processName);
            return true;
        }

        // Step 1: Focus the target window
        _log.Debug("KeyboardFallback: Focusing window {Process} | {Title} (handle={Handle})",
            processName, windowTitle, windowHandle);

        FocusWindow(windowHandle);

        // Step 2: Focus stabilization delay (150ms)
        Thread.Sleep(150);

        // Step 3: Pre-input delay (75ms)
        Thread.Sleep(75);

        // Step 4: Send the key via SendInput
        _log.Information("KeyboardFallback: Sending key '{Key}' for option '{Label}' in {Process} | {Window}",
            bestKey, bestLabel, processName, windowTitle);

        SendKeyViaSendInput((ushort)bestKey[0]);

        return true;
    }

    private void FocusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        // Restore if minimized
        ShowWindow(hwnd, SW_RESTORE);

        // Attach to the target window's thread to allow SetForegroundWindow
        var currentHwnd = GetForegroundWindow();
        GetWindowThreadProcessId(currentHwnd, out _);
        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = GetWindowThreadProcessId(hwnd, out _);

        bool attached = false;
        if (currentThreadId != targetThreadId)
        {
            attached = AttachThreadInput(currentThreadId, targetThreadId, true);
        }

        SetForegroundWindow(hwnd);

        if (attached)
        {
            AttachThreadInput(currentThreadId, targetThreadId, false);
        }
    }

    private static void SendKeyViaSendInput(ushort vk)
    {
        // For digits 0-9, VK codes are 0x30-0x39 (same as ASCII)
        // vk already has the correct value for digit characters

        var inputs = new INPUT[2];

        // Key down
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 }
            }
        };

        // Key up
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
            }
        };

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }
}
