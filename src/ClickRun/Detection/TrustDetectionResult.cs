using System.Windows.Automation;
using ClickRun.Models;

namespace ClickRun.Detection;

/// <summary>
/// Result of trust dialog detection analysis.
/// ScanHash is set by the detector to prove this result came from a real scan.
/// </summary>
public sealed record TrustDetectionResult(
    bool IsBlockingTrustDialog,
    AutomationElement? FullCommandElement,
    ElementDescriptor? FullCommandDescriptor,
    int TrustLabelCount,
    int TotalButtonCount,
    string? ScanHash = null);
