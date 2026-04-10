using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Detection;

/// <summary>
/// Scans windows for enabled, visible Button elements
/// using the Windows UI Automation API.
/// </summary>
public sealed class Detector
{
    private readonly ILogger _log;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public Detector(ILogger logger)
    {
        _log = logger.ForContext<Detector>();
    }

    /// <summary>
    /// Scans the current foreground window and returns all visible, enabled buttons.
    /// Returns null when no foreground window is available or an error occurs.
    /// </summary>
    public ScanResult? Scan()
    {
        try
        {
            var hwnd = GetForegroundWindow();

            if (hwnd == IntPtr.Zero)
            {
                _log.Debug("No foreground window detected, skipping cycle");
                return null;
            }

            return ScanWindow(hwnd);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error during foreground scan, skipping cycle");
            return null;
        }
    }

    /// <summary>
    /// Scans all visible windows belonging to whitelisted processes.
    /// Returns a list of ScanResults, one per matching window.
    /// </summary>
    public List<ScanResult> ScanAll(HashSet<string> whitelistedProcessNames)
    {
        var results = new List<ScanResult>();
        var windowHandles = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (IsWindowVisible(hWnd))
                windowHandles.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        _log.Debug("MultiWindow: EnumWindows found {Count} visible windows", windowHandles.Count);

        int matchedWindows = 0;

        foreach (var hwnd in windowHandles)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) continue;

                string processName;
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    processName = process.ProcessName;
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!whitelistedProcessNames.Contains(processName))
                {
                    // Case-insensitive fallback check
                    bool found = false;
                    foreach (var wp in whitelistedProcessNames)
                    {
                        if (string.Equals(wp, processName, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) continue;
                }

                // Get window title to filter out empty/helper windows
                string windowTitle;
                try
                {
                    var rootElement = AutomationElement.FromHandle(hwnd);
                    windowTitle = rootElement.Current.Name ?? string.Empty;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(windowTitle))
                {
                    _log.Debug("MultiWindow: Skipping {Process} window with empty title (handle={Handle})", processName, hwnd);
                    continue;
                }

                _log.Debug("MultiWindow: Found window — Process={Process} | Title={Title} | Handle={Handle}", processName, windowTitle, hwnd);
                matchedWindows++;

                var result = ScanWindow(hwnd);
                if (result != null && result.Buttons.Count > 0)
                {
                    _log.Debug("MultiWindow: Scanned {ButtonCount} buttons in {Process} | {Title}", result.Buttons.Count, processName, windowTitle);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Error scanning window {Handle}, skipping", hwnd);
            }
        }

        _log.Debug("MultiWindow: {Matched} whitelisted windows found, {Results} with buttons", matchedWindows, results.Count);

        return results;
    }

    private ScanResult? ScanWindow(IntPtr hwnd)
    {
        try
        {
            var rootElement = AutomationElement.FromHandle(hwnd);

            var pid = (int)rootElement.GetCurrentPropertyValue(AutomationElement.ProcessIdProperty);
            string processName;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
            }
            catch (ArgumentException)
            {
                _log.Debug("Process {Pid} no longer exists, skipping window", pid);
                return null;
            }

            var windowTitle = rootElement.Current.Name ?? string.Empty;

            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.IsEnabledProperty, true),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false));

            var elements = rootElement.FindAll(TreeScope.Descendants, condition);

            var buttons = new List<(ElementDescriptor, AutomationElement)>();

            foreach (AutomationElement element in elements)
            {
                try
                {
                    var descriptor = new ElementDescriptor(
                        ProcessName: processName,
                        WindowTitle: windowTitle,
                        ButtonLabel: element.Current.Name ?? string.Empty,
                        AutomationId: element.Current.AutomationId ?? string.Empty,
                        IsButton: true,
                        IsVisible: !element.Current.IsOffscreen,
                        IsEnabled: element.Current.IsEnabled);

                    buttons.Add((descriptor, element));
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            return new ScanResult(processName, windowTitle, buttons);
        }
        catch (ElementNotAvailableException ex)
        {
            _log.Error(ex, "UI Automation element not available, skipping window");
            return null;
        }
        catch (COMException ex)
        {
            _log.Error(ex, "UI Automation COM error, skipping window");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error during window scan, skipping");
            return null;
        }
    }
}
