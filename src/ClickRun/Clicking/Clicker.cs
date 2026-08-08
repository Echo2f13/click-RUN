using System.Runtime.InteropServices;
using System.Windows.Automation;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Clicking;

/// <summary>
/// Invokes a button via UI Automation's InvokePattern with a single-retry strategy.
/// Supports optional focus restoration to prevent target window from stealing focus.
/// </summary>
public sealed class Clicker
{
    private readonly ILogger _log;
    private readonly Random _random;

    // --- P/Invoke for focus management ---

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    public Clicker(ILogger logger, Random? random = null)
    {
        _log = logger.ForContext<Clicker>();
        _random = random ?? new Random();
    }

    /// <summary>
    /// Attempts to click the given button element using InvokePattern.
    /// When preClickDelayMs > 0, waits that many milliseconds before the first Invoke() call.
    /// When restoreFocus is true, saves the current foreground window and restores it after clicking.
    /// On failure, waits 50-100ms (randomized) and retries exactly once.
    /// Returns a <see cref="ClickResult"/> indicating success or failure.
    /// </summary>
    public ClickResult Click(
        AutomationElement button,
        ElementDescriptor descriptor,
        int preClickDelayMs = 0,
        bool restoreFocus = true,
        int focusRestoreDelayMs = 50)
    {
        InvokePattern invokePattern;
        try
        {
            invokePattern = (InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to get InvokePattern: {descriptor.ProcessName} | {descriptor.WindowTitle} | {descriptor.ButtonLabel} | {ex.Message}";
            _log.Error(msg);
            return new ClickResult(false, msg);
        }

        // Save current foreground window before clicking (if focus restoration is enabled)
        IntPtr previousForeground = IntPtr.Zero;
        if (restoreFocus)
        {
            previousForeground = GetForegroundWindow();
            _log.Debug("Focus: Saved foreground window handle {Handle}", previousForeground);
        }

        // Pre-click delay
        if (preClickDelayMs > 0)
        {
            Thread.Sleep(preClickDelayMs);
        }

        // First attempt
        bool clickSuccess = false;
        try
        {
            invokePattern.Invoke();
            clickSuccess = true;
        }
        catch
        {
            // First attempt failed — retry once after randomized delay
        }

        if (!clickSuccess)
        {
            // Wait 50-100ms before retry
            var delayMs = _random.Next(50, 101);
            Thread.Sleep(delayMs);

            // Retry attempt
            try
            {
                invokePattern.Invoke();
                clickSuccess = true;
            }
            catch (Exception ex)
            {
                var msg = $"Invoke failed after retry: {descriptor.ProcessName} | {descriptor.WindowTitle} | {descriptor.ButtonLabel} | {ex.Message}";
                _log.Error(msg);
                return new ClickResult(false, msg);
            }
        }

        // Restore focus to previous window after successful click
        if (clickSuccess && restoreFocus && previousForeground != IntPtr.Zero)
        {
            RestoreFocus(previousForeground, focusRestoreDelayMs);
        }

        return new ClickResult(true);
    }

    /// <summary>
    /// Restores focus to the specified window handle.
    /// Uses thread input attachment to ensure SetForegroundWindow succeeds.
    /// </summary>
    private void RestoreFocus(IntPtr targetWindow, int delayMs)
    {
        // Small delay to let the target app process the click before we steal focus back
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        try
        {
            // Get current foreground window's thread
            var currentForeground = GetForegroundWindow();
            if (currentForeground == targetWindow)
            {
                _log.Debug("Focus: Target window is already in foreground, no restoration needed");
                return;
            }

            var currentThreadId = GetCurrentThreadId();
            var foregroundThreadId = GetWindowThreadProcessId(currentForeground, out _);
            var targetThreadId = GetWindowThreadProcessId(targetWindow, out _);

            bool attached = false;

            // Attach to foreground thread to get permission to change focus
            if (currentThreadId != foregroundThreadId && foregroundThreadId != 0)
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            var result = SetForegroundWindow(targetWindow);

            if (attached)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (result)
            {
                _log.Debug("Focus: Restored foreground to window {Handle}", targetWindow);
            }
            else
            {
                _log.Debug("Focus: SetForegroundWindow returned false for {Handle}", targetWindow);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Focus: Failed to restore foreground window");
        }
    }
}
