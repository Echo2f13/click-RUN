using ClickRun.Clicking;
using ClickRun.Detection;
using ClickRun.Models;
using ClickRun.Tracking;
using Serilog;

namespace ClickRun.Filtering;

/// <summary>
/// Handles the safe fallback for blocking trust dialogs.
/// Only clicks "Full command ..." (exact-match trust) after
/// validating all safety preconditions.
/// </summary>
public sealed class TrustFallbackHandler
{
    private readonly ILogger _logger;
    private readonly IClickExecutor _clickExecutor;
    private readonly DebounceTracker _debounceTracker;
    private readonly TimeSpan _debounceCooldown;

    /// <summary>
    /// Known execution labels as a HashSet for O(1) exact lookup.
    /// Only exact normalized matches are treated as execution labels.
    /// </summary>
    private static readonly HashSet<string> ExecutionLabelSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept",
        "accept command",
        "run",
        "allow",
        "approve",
        "continue",
        "yes"
    };

    public TrustFallbackHandler(
        ILogger logger,
        IClickExecutor clickExecutor,
        DebounceTracker debounceTracker,
        TimeSpan debounceCooldown)
    {
        _logger = logger.ForContext<TrustFallbackHandler>();
        _clickExecutor = clickExecutor;
        _debounceTracker = debounceTracker;
        _debounceCooldown = debounceCooldown;
    }

    /// <summary>
    /// Attempts to safely resolve a blocking trust dialog by clicking
    /// the "Full command ..." button. Returns true if the fallback was applied.
    /// </summary>
    public bool TryFallback(
        TrustDetectionResult detection,
        ScanResult scanResult,
        Configuration config,
        bool dryRun)
    {
        // Null whitelist guard
        if (config?.Whitelist == null)
            throw new NullReferenceException("Configuration whitelist cannot be null.");

        // Safety check 1: Mode must be "safe"
        if (config.TrustFallbackMode != TrustFallbackMode.Safe)
        {
            _logger.Debug("TrustFallback: Mode is '{Mode}', skipping", config.TrustFallbackMode);
            return false;
        }

        // Safety check 2: Must be a blocking trust dialog
        if (!detection.IsBlockingTrustDialog)
            return false;

        // Safety check 3: Must have a valid "Full command ..." descriptor
        if (detection.FullCommandDescriptor == null
            || !detection.FullCommandDescriptor.IsVisible
            || !detection.FullCommandDescriptor.IsEnabled)
        {
            _logger.Warning("TrustFallback: Blocking trust dialog but no valid 'Full command' button found, skipping");
            return false;
        }

        // Verify descriptor exists in scan result (identity match on ProcessName, ButtonLabel, AutomationId)
        bool descriptorInScan = false;
        foreach (var (descriptor, _) in scanResult.Buttons)
        {
            if (string.Equals(descriptor.ProcessName, detection.FullCommandDescriptor.ProcessName, StringComparison.Ordinal)
                && string.Equals(descriptor.ButtonLabel, detection.FullCommandDescriptor.ButtonLabel, StringComparison.Ordinal)
                && string.Equals(descriptor.AutomationId, detection.FullCommandDescriptor.AutomationId, StringComparison.Ordinal))
            {
                descriptorInScan = true;
                break;
            }
        }
        if (!descriptorInScan)
        {
            _logger.Warning("TrustFallback: FullCommandDescriptor not found in scan result, skipping");
            return false;
        }

        // Verify detection was produced by the detector for this scan (ScanHash validation)
        var expectedHash = TrustDialogDetector.ComputeScanHash(scanResult);
        if (!string.Equals(detection.ScanHash, expectedHash, StringComparison.Ordinal))
        {
            _logger.Warning("TrustFallback: Detection ScanHash mismatch, skipping");
            return false;
        }

        // Safety check 4: Source process must be whitelisted (case-sensitive, no wildcards)
        if (!IsWhitelistedProcess(scanResult.ProcessName, config.Whitelist))
        {
            _logger.Warning("TrustFallback: Process '{Process}' is not whitelisted, skipping",
                scanResult.ProcessName);
            return false;
        }

        // Safety check 5: Verify no execution buttons exist (exact match only)
        foreach (var (descriptor, _) in scanResult.Buttons)
        {
            var normalized = SafetyFilter.NormalizeLabel(descriptor.ButtonLabel);
            if (IsExecutionLabel(normalized))
            {
                _logger.Warning(
                    "TrustFallback: Execution button '{Label}' found, aborting fallback",
                    descriptor.ButtonLabel);
                return false;
            }
        }

        // Safety check 6: Debounce
        var hash = DebounceTracker.ComputeHash(detection.FullCommandDescriptor);
        if (_debounceTracker.IsInCooldown(hash, _debounceCooldown))
        {
            _logger.Debug("TrustFallback: Debounce active for 'Full command', skipping");
            return false;
        }

        // All safety checks passed — execute fallback
        if (dryRun)
        {
            _logger.Information(
                "[DRY RUN] TrustFallback: Would click 'Full command' in {Process} | {Window}",
                scanResult.ProcessName, scanResult.WindowTitle);
            _debounceTracker.Record(hash);
            return true;
        }

        var clickResult = _clickExecutor.Click(detection.FullCommandDescriptor, config.PreClickDelayMs);

        if (clickResult.Success)
        {
            _debounceTracker.Record(hash);
            _logger.Information(
                "TrustFallback: Clicked 'Full command' in {Process} | {Window} | {Label}",
                scanResult.ProcessName, scanResult.WindowTitle,
                detection.FullCommandDescriptor.ButtonLabel);
            return true;
        }

        _logger.Error("TrustFallback: Click failed for 'Full command' — {Error}", clickResult.ErrorMessage);
        return false;
    }

    /// <summary>
    /// Case-sensitive process whitelist check. Wildcards excluded.
    /// </summary>
    private static bool IsWhitelistedProcess(string processName, List<WhitelistEntry> whitelist)
    {
        foreach (var entry in whitelist)
        {
            if (string.Equals(entry.ProcessName, "*", StringComparison.Ordinal))
                continue;

            if (string.Equals(entry.ProcessName, processName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Exact match only. Normalize + HashSet lookup. No prefix, no contains, no fuzzy.
    /// </summary>
    private static bool IsExecutionLabel(string normalizedLabel)
    {
        if (string.IsNullOrEmpty(normalizedLabel))
            return false;

        return ExecutionLabelSet.Contains(normalizedLabel);
    }
}
