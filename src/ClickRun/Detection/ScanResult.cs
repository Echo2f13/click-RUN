using System.Windows.Automation;
using ClickRun.Models;

namespace ClickRun.Detection;

/// <summary>
/// Result of a single foreground window scan cycle.
/// </summary>
public sealed record ScanResult(
    string ProcessName,
    string WindowTitle,
    List<(ElementDescriptor Descriptor, AutomationElement Element)> Buttons);
