using System.Windows.Automation;
using ClickRun.Clicking;
using ClickRun.Detection;
using ClickRun.Filtering;
using ClickRun.Models;
using ClickRun.Tracking;
using FsCheck;
using FsCheck.Xunit;
using Serilog;
using Xunit;

using Configuration = ClickRun.Models.Configuration;

namespace ClickRun.Tests;

/// <summary>
/// Property-based tests for the trust dialog fallback system.
/// These validate universal correctness properties from the design document.
/// </summary>
public class TrustFallbackPropertyTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();
    private static readonly TrustDialogDetector Detector = new(Logger);

    private static readonly string[] TrustPrefixes = { "Full command", "Base", "Partial", "Trust command and accept" };
    private static readonly string[] ExecutionLabels = { "Accept", "Accept command", "Run", "Allow", "Approve", "Continue", "Yes" };
    private static readonly string[] OtherLabels = { "Cancel", "Close", "Minimize", "Settings", "Help", "Dismiss" };

    private static ElementDescriptor MakeDesc(string label) =>
        new("Kiro", "Kiro Window", label, "", true, true, true);

    private static ScanResult MakeScan(IEnumerable<string> labels) =>
        new("Kiro", "Kiro Window", IntPtr.Zero,
            labels.Select(l => (MakeDesc(l), (AutomationElement)null!)).ToList());

    private static List<Candidate> NoCandidates() => new();

    private static Configuration SafeConfig() => new()
    {
        TrustFallbackMode = TrustFallbackMode.Safe,
        Whitelist = new List<WhitelistEntry>
        {
            new() { ProcessName = "Kiro", WindowTitles = new(), ButtonLabels = new() }
        }
    };

    // --- Property 8: Detection Completeness ---
    // IsBlockingTrustDialog == true iff passedCandidates.Count == 0 AND hasTrustLabel AND !hasExecutionLabel

    [Property(MaxTest = 200)]
    public bool DetectionCompleteness_RandomButtonCombinations(byte trustMask, byte execMask, byte otherMask)
    {
        // Build a button list from random masks
        var labels = new List<string>();

        for (int i = 0; i < TrustPrefixes.Length && i < 8; i++)
        {
            if ((trustMask & (1 << i)) != 0)
                labels.Add(TrustPrefixes[i] + " some-arg");
        }
        for (int i = 0; i < ExecutionLabels.Length && i < 8; i++)
        {
            if ((execMask & (1 << i)) != 0)
                labels.Add(ExecutionLabels[i]);
        }
        for (int i = 0; i < OtherLabels.Length && i < 8; i++)
        {
            if ((otherMask & (1 << i)) != 0)
                labels.Add(OtherLabels[i]);
        }

        if (labels.Count == 0)
        {
            var scan = MakeScan(labels);
            var result = Detector.Detect(scan, NoCandidates());
            return !result.IsBlockingTrustDialog; // Empty → not blocking
        }

        bool hasTrust = labels.Any(l => TrustDialogDetector.IsTrustLabel(l));
        bool hasExec = labels.Any(l => TrustDialogDetector.IsExecutionLabel(l));
        bool expectedBlocking = hasTrust && !hasExec;

        var scanResult = MakeScan(labels);
        var detection = Detector.Detect(scanResult, NoCandidates());

        return detection.IsBlockingTrustDialog == expectedBlocking;
    }

    [Property(MaxTest = 100)]
    public bool DetectionCompleteness_WithPassedCandidates_NeverBlocking(byte trustMask)
    {
        var labels = new List<string>();
        for (int i = 0; i < TrustPrefixes.Length && i < 8; i++)
        {
            if ((trustMask & (1 << i)) != 0)
                labels.Add(TrustPrefixes[i] + " arg");
        }
        if (labels.Count == 0) labels.Add("Full command test");

        var scan = MakeScan(labels);
        var candidates = new List<Candidate>
        {
            new(MakeDesc("Accept"), new WhitelistEntry { ProcessName = "Kiro" }, "hash")
        };

        var result = Detector.Detect(scan, candidates);
        return !result.IsBlockingTrustDialog;
    }

    // --- Property 9: Trust Label Classification ---

    [Property(MaxTest = 100)]
    public bool TrustLabelClassification_PrefixesAreRecognized(int prefixIndex, NonEmptyString suffix)
    {
        var idx = Math.Abs(prefixIndex) % TrustPrefixes.Length;
        var label = TrustPrefixes[idx] + " " + suffix.Get;
        return TrustDialogDetector.IsTrustLabel(label);
    }

    [Property(MaxTest = 100)]
    public bool TrustLabelClassification_ExecutionLabelsAreNotTrust(int labelIndex)
    {
        var idx = Math.Abs(labelIndex) % ExecutionLabels.Length;
        return !TrustDialogDetector.IsTrustLabel(ExecutionLabels[idx]);
    }

    [Fact]
    public void TrustLabelClassification_EmptyIsNotTrust()
    {
        Assert.False(TrustDialogDetector.IsTrustLabel(""));
    }

    // --- Property 3: Mode Gate ---
    // When TrustFallbackMode == Off, TryFallback always returns false

    [Property(MaxTest = 100)]
    public bool ModeGate_OffMode_AlwaysFalse(bool isBlocking, bool hasFullCommand)
    {
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Off,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "Kiro", WindowTitles = new(), ButtonLabels = new() }
            }
        };

        var desc = hasFullCommand ? MakeDesc("Full command test") : null;
        var detection = new TrustDetectionResult(isBlocking, null, desc, 2, 3);
        var scan = MakeScan(new[] { "Full command test", "Base test" });

        return !handler.TryFallback(detection, scan, config, dryRun: true);
    }

    // --- Property 4: Process Restriction ---
    // Non-whitelisted process → always false

    [Property(MaxTest = 50)]
    public bool ProcessRestriction_NonWhitelisted_AlwaysFalse(NonEmptyString processName)
    {
        var name = processName.Get;
        // Ensure it doesn't accidentally match "Kiro"
        if (name.Equals("Kiro", StringComparison.OrdinalIgnoreCase)) return true; // skip

        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var config = SafeConfig();

        var detection = new TrustDetectionResult(true, null!, MakeDesc("Full command test"), 2, 3);
        var scan = new ScanResult(name, "Window", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor(name, "Window", "Full command test", "", true, true, true), null!)
            });

        return !handler.TryFallback(detection, scan, config, dryRun: true);
    }

    // --- Property 5: Execution Button Guard ---
    // If any execution label in scan → always false

    [Property(MaxTest = 100)]
    public bool ExecutionButtonGuard_ExecutionPresent_AlwaysFalse(int execIndex)
    {
        var idx = Math.Abs(execIndex) % ExecutionLabels.Length;
        var execLabel = ExecutionLabels[idx];

        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var config = SafeConfig();

        var detection = new TrustDetectionResult(true, null!, MakeDesc("Full command test"), 2, 3);
        var scan = MakeScan(new[] { "Full command test", "Base test", execLabel });

        return !handler.TryFallback(detection, scan, config, dryRun: true);
    }
}
