using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Detection;

/// <summary>
/// Scans the foreground window for enabled, visible Button elements
/// using the Windows UI Automation API.
/// </summary>
public sealed class Detector
{
    private readonly ILogger _log;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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

            var rootElement = AutomationElement.FromHandle(hwnd);

            // Extract process name from the window's ProcessId property
            var pid = (int)rootElement.GetCurrentPropertyValue(AutomationElement.ProcessIdProperty);
            string processName;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
            }
            catch (ArgumentException)
            {
                _log.Debug("Process {Pid} no longer exists, skipping cycle", pid);
                return null;
            }

            // Extract window title from the Name property
            var windowTitle = rootElement.Current.Name ?? string.Empty;

            // Find all visible, enabled buttons
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
                    // Element disappeared between FindAll and property access; skip it
                }
            }

            return new ScanResult(processName, windowTitle, buttons);
        }
        catch (ElementNotAvailableException ex)
        {
            _log.Error(ex, "UI Automation element not available, skipping cycle");
            return null;
        }
        catch (COMException ex)
        {
            _log.Error(ex, "UI Automation COM error, skipping cycle");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error during scan, skipping cycle");
            return null;
        }
    }
}
