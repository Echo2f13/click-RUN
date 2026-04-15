using System.Windows.Automation;
using ClickRun.Filtering;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Detection;

/// <summary>
/// Detects blocking trust dialogs where no whitelisted execution buttons
/// are available and trust-related labels are present without execution labels.
/// </summary>
public sealed class TrustDialogDetector
{
    private readonly ILogger _logger;

    /// <summary>
    /// Known trust-related label prefixes. A button whose normalized label
    /// starts with any of these (case-insensitive) is classified as a trust label.
    /// </summary>
    private static readonly string[] TrustLabelPrefixes =
    [
        "full command",
        "base",
        "partial",
        "trust command and accept"
    ];

    /// <summary>
    /// Known execution labels. A button whose normalized lowercase label
    /// matches any of these exactly is classified as an execution label.
    /// </summary>
    private static readonly string[] ExecutionLabels =
    [
        "accept",
        "accept command",
        "run",
        "allow",
        "approve",
        "continue",
        "yes"
    ];

    public TrustDialogDetector(ILogger logger)
    {
        _logger = logger.ForContext<TrustDialogDetector>();
    }

    /// <summary>
    /// Analyzes a scan result and returns a detection result indicating
    /// whether this is a blocking trust dialog.
    ///
    /// A dialog is blocking when:
    ///   passedCandidates.Count == 0 AND hasTrustLabel AND !hasExecutionLabel
    /// </summary>
    public TrustDetectionResult Detect(
        ScanResult scanResult,
        List<Candidate> passedCandidates)
    {
        // If any candidates passed the safety filter, this is NOT a blocking dialog
        if (passedCandidates.Count > 0)
        {
            _logger.Debug("TrustDetect: {Count} candidates passed safety filter, not blocking",
                passedCandidates.Count);
            return NotBlocking(scanResult.Buttons.Count);
        }

        // If no buttons at all, nothing to detect
        if (scanResult.Buttons.Count == 0)
        {
            _logger.Debug("TrustDetect: No buttons found, not blocking");
            return NotBlocking(0);
        }

        // Classify every button and track flags
        AutomationElement? fullCommandElement = null;
        ElementDescriptor? fullCommandDescriptor = null;
        bool hasTrustLabel = false;
        bool hasExecutionLabel = false;
        int trustCount = 0;

        foreach (var (descriptor, element) in scanResult.Buttons)
        {
            var normalized = SafetyFilter.NormalizeLabel(descriptor.ButtonLabel);

            if (IsTrustLabel(normalized))
            {
                hasTrustLabel = true;
                trustCount++;

                // Track the "Full command ..." element specifically (must be visible and enabled)
                if (normalized.StartsWith("full command", StringComparison.OrdinalIgnoreCase)
                    && descriptor.IsVisible && descriptor.IsEnabled)
                {
                    fullCommandElement = element;
                    fullCommandDescriptor = descriptor;
                }
            }

            if (IsExecutionLabel(normalized))
            {
                hasExecutionLabel = true;
                _logger.Debug("TrustDetect: Execution label found: '{Label}', not a blocking trust dialog",
                    descriptor.ButtonLabel);
            }
        }

        // CORRECTED LOGIC: blocking = trust labels present AND no execution labels
        bool isBlocking = hasTrustLabel && !hasExecutionLabel;

        if (isBlocking)
        {
            _logger.Information(
                "TrustDetect: Blocking trust dialog detected — {TrustCount} trust labels, {TotalCount} total buttons, FullCommand={HasFull}",
                trustCount, scanResult.Buttons.Count, fullCommandElement != null);
        }
        else
        {
            _logger.Debug(
                "TrustDetect: Not blocking — hasTrustLabel={HasTrust}, hasExecutionLabel={HasExec}, {TotalCount} total buttons",
                hasTrustLabel, hasExecutionLabel, scanResult.Buttons.Count);
        }

        // Compute scan hash to prove this detection came from a real scan
        var scanHash = ComputeScanHash(scanResult);

        return new TrustDetectionResult(
            IsBlockingTrustDialog: isBlocking,
            FullCommandElement: fullCommandElement,
            FullCommandDescriptor: fullCommandDescriptor,
            TrustLabelCount: trustCount,
            TotalButtonCount: scanResult.Buttons.Count,
            ScanHash: scanHash);
    }

    /// <summary>
    /// Computes a hash of the scan result to embed in the detection result.
    /// The handler verifies this hash matches the scan it receives.
    /// </summary>
    internal static string ComputeScanHash(ScanResult scanResult)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(scanResult.ProcessName);
        sb.Append('|');
        sb.Append(scanResult.WindowTitle);
        sb.Append('|');
        sb.Append(scanResult.Buttons.Count);
        foreach (var (desc, _) in scanResult.Buttons)
        {
            sb.Append('|');
            sb.Append(desc.ButtonLabel);
        }
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// Returns true if the normalized label starts with any known trust prefix
    /// at a word boundary (case-insensitive). The prefix must be followed by
    /// a space or end-of-string to prevent false positives like "Baseball" matching "base".
    /// </summary>
    internal static bool IsTrustLabel(string normalizedLabel)
    {
        if (string.IsNullOrEmpty(normalizedLabel))
            return false;

        foreach (var prefix in TrustLabelPrefixes)
        {
            if (normalizedLabel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Word boundary check: must be followed by space or end-of-string
                if (normalizedLabel.Length == prefix.Length || normalizedLabel[prefix.Length] == ' ')
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the normalized lowercase label matches any known execution label exactly.
    /// </summary>
    internal static bool IsExecutionLabel(string normalizedLabel)
    {
        if (string.IsNullOrEmpty(normalizedLabel))
            return false;

        var lower = normalizedLabel.ToLowerInvariant();

        foreach (var label in ExecutionLabels)
        {
            if (string.Equals(lower, label, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static TrustDetectionResult NotBlocking(int totalButtons) =>
        new(false, null, null, 0, totalButtons, null);
}
