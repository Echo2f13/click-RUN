using System.Windows.Automation;
using ClickRun.Clicking;
using ClickRun.Config;
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

// ═══════════════════════════════════════════════════════════════════
// SECTION 1 — DETECTION BREAK TESTS
// ═══════════════════════════════════════════════════════════════════

public class DetectionBreakTests
{
    private readonly TrustDialogDetector _detector;

    public DetectionBreakTests()
    {
        _detector = new TrustDialogDetector(new LoggerConfiguration().CreateLogger());
    }

    private static ElementDescriptor Desc(string label, bool visible = true, bool enabled = true) =>
        new("Kiro", "Kiro Window", label, "", true, visible, enabled);

    private static ScanResult Scan(params (string label, bool visible, bool enabled)[] buttons)
    {
        var list = buttons.Select(b =>
            (Desc(b.label, b.visible, b.enabled), (AutomationElement)null!)).ToList();
        return new ScanResult("Kiro", "Kiro Window", IntPtr.Zero, list);
    }

    private static ScanResult Scan(params string[] labels) =>
        new("Kiro", "Kiro Window", IntPtr.Zero,
            labels.Select(l => (Desc(l), (AutomationElement)null!)).ToList());

    private static List<Candidate> None() => new();

    // --- 1.1 Trust button variations ---

    [Theory]
    [InlineData("Trust")]           // bare "Trust" — NOT a trust prefix
    [InlineData("Trust App")]       // NOT a known prefix
    [InlineData("Always Trust")]    // NOT a known prefix
    [InlineData("Trust this app")]  // NOT a known prefix
    public void Detect_GenericTrustLabels_AreNotTrustPrefixes(string label)
    {
        // These should NOT be classified as trust labels.
        // "Trust" alone doesn't start with any of: "full command", "base", "partial", "trust command and accept"
        Assert.False(TrustDialogDetector.IsTrustLabel(label));
    }

    [Fact]
    public void Detect_GenericTrustOnly_NotBlocking()
    {
        // Dialog with only "Trust" button — not a trust prefix, so not blocking
        var scan = Scan("Trust");
        var result = _detector.Detect(scan, None());
        Assert.False(result.IsBlockingTrustDialog);
    }

    [Theory]
    [InlineData("FULL COMMAND test")]
    [InlineData("full command test")]
    [InlineData("Full Command Test")]
    [InlineData("fUlL cOmMaNd test")]
    public void Detect_CaseMixing_StillDetectedAsTrust(string label)
    {
        var scan = Scan(label);
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
    }

    [Theory]
    [InlineData("  Full command test  ")]   // leading/trailing spaces
    [InlineData("\tFull command test")]      // tab prefix
    [InlineData("Full  command  test")]      // double spaces
    public void Detect_WhitespaceVariations_NormalizedCorrectly(string label)
    {
        // NormalizeLabel strips non-ASCII, trims, collapses spaces
        var normalized = SafetyFilter.NormalizeLabel(label);
        Assert.True(TrustDialogDetector.IsTrustLabel(normalized));
    }

    [Fact]
    public void Detect_UnicodeInLabel_StrippedByNormalization()
    {
        // Unicode chars get stripped, leaving "Full command test"
        var label = "Full\u00A0command\u00A0test"; // non-breaking spaces (0xA0 > 0x7F → stripped)
        var normalized = SafetyFilter.NormalizeLabel(label);
        // After stripping non-ASCII: "Fullcommandtest" (no spaces between words)
        Assert.Equal("Fullcommandtest", normalized);
        // This means "Full command" with non-breaking spaces is NOT detected as trust
        Assert.False(TrustDialogDetector.IsTrustLabel(normalized));
    }

    // --- 1.2 Layout edge cases ---

    [Fact]
    public void Detect_FullCommandNotFirstButton_StillDetected()
    {
        var scan = Scan("Base python", "Partial python", "Full command python -m test");
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
        Assert.NotNull(result.FullCommandDescriptor);
        Assert.Equal("Full command python -m test", result.FullCommandDescriptor!.ButtonLabel);
    }

    [Fact]
    public void Detect_FullCommandHidden_NotTrackedAsElement()
    {
        var scan = Scan(
            ("Full command python", false, true),  // hidden
            ("Base python", true, true));
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
        Assert.Null(result.FullCommandDescriptor); // hidden → not tracked
    }

    [Fact]
    public void Detect_FullCommandDisabled_NotTrackedAsElement()
    {
        var scan = Scan(
            ("Full command python", true, false),  // disabled
            ("Base python", true, true));
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
        Assert.Null(result.FullCommandDescriptor); // disabled → not tracked
    }

    [Fact]
    public void Detect_MultipleFullCommands_LastOneWins()
    {
        // Two "Full command" buttons — the last visible+enabled one should be tracked
        var scan = Scan("Full command first", "Full command second");
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
        Assert.Equal("Full command second", result.FullCommandDescriptor!.ButtonLabel);
    }

    // --- 1.3 Multi-button ambiguity ---

    [Fact]
    public void Detect_TrustPlusCancelPlusAccept_NotBlocking()
    {
        // Accept is an execution label → not blocking
        var scan = Scan("Full command python", "Cancel", "Accept");
        var result = _detector.Detect(scan, None());
        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_TrustPlusClosePlusIgnore_Blocking()
    {
        // Close and Ignore are neither trust nor execution → blocking
        var scan = Scan("Full command python", "Close", "Ignore");
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_MultipleTrustLikeButtons_AllCounted()
    {
        var scan = Scan("Full command a", "Full command b", "Base c", "Partial d");
        var result = _detector.Detect(scan, None());
        Assert.True(result.IsBlockingTrustDialog);
        Assert.Equal(4, result.TrustLabelCount);
    }

    // --- 1.4 Missing fields ---

    [Fact]
    public void Detect_EmptyButtonLabel_NotTrust()
    {
        var scan = Scan("");
        var result = _detector.Detect(scan, None());
        Assert.False(result.IsBlockingTrustDialog); // empty label → not trust
    }

    [Fact]
    public void Detect_NullButtonLabel_HandledGracefully()
    {
        var buttons = new List<(ElementDescriptor, AutomationElement)>
        {
            (new ElementDescriptor("Kiro", "Win", null!, "", true, true, true), null!)
        };
        var scan = new ScanResult("Kiro", "Win", IntPtr.Zero, buttons);
        // NormalizeLabel handles null → returns empty → not trust
        var result = _detector.Detect(scan, None());
        Assert.False(result.IsBlockingTrustDialog);
    }

    // --- 1.5 False positives ---

    [Theory]
    [InlineData("Do not trust")]
    [InlineData("Untrusted")]
    [InlineData("Trust settings")]
    [InlineData("Trusted publisher")]
    [InlineData("Trustworthy")]
    public void Detect_FalsePositiveTrustLabels_NotClassifiedAsTrust(string label)
    {
        // None of these start with a known trust prefix
        Assert.False(TrustDialogDetector.IsTrustLabel(label));
    }

    [Fact]
    public void Detect_BaseballLabel_FalsePositive_ShouldNotBeTrust()
    {
        // BUG: "Baseball" starts with "base" → incorrectly classified as trust.
        // Spec requires trust prefix matching at word boundaries.
        // "Baseball" is not a trust dialog label.
        Assert.False(TrustDialogDetector.IsTrustLabel("Baseball"));
    }

    [Fact]
    public void Detect_PartiallyComplete_FalsePositive_ShouldNotBeTrust()
    {
        // BUG: "Partially complete" starts with "partial" → incorrectly classified as trust.
        // Spec requires trust prefix matching at word boundaries.
        Assert.False(TrustDialogDetector.IsTrustLabel("Partially complete"));
    }

    [Fact]
    public void Detect_BaselineReport_FalsePositive_ShouldNotBeTrust()
    {
        // BUG: "Baseline report" starts with "base" → incorrectly classified as trust.
        // Spec requires trust prefix matching at word boundaries.
        Assert.False(TrustDialogDetector.IsTrustLabel("Baseline report"));
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 2 — CLASSIFICATION ATTACKS
// ═══════════════════════════════════════════════════════════════════

public class ClassificationAttackTests
{
    // --- 2.1 Semantic traps ---

    [Theory]
    [InlineData("Trust and continue", false)]       // doesn't start with known prefix
    [InlineData("Continue (Trusted)", false)]        // "Continue" is execution label
    [InlineData("Allow & Trust", false)]             // "Allow" is not a prefix match for trust
    public void IsTrustLabel_SemanticTraps(string label, bool expectedTrust)
    {
        Assert.Equal(expectedTrust, TrustDialogDetector.IsTrustLabel(label));
    }

    [Theory]
    [InlineData("Trust and continue", false)]       // not exact match for any execution label
    [InlineData("Continue (Trusted)", false)]        // not exact match — has suffix
    [InlineData("Allow & Trust", false)]             // not exact match — has suffix
    public void IsExecutionLabel_SemanticTraps(string label, bool expectedExec)
    {
        Assert.Equal(expectedExec, TrustDialogDetector.IsExecutionLabel(label));
    }

    [Fact]
    public void Detect_TrustAndContinue_NeitherTrustNorExecution()
    {
        // "Trust and continue" is not a trust prefix and not an exact execution label
        var scan = new ScanResult("Kiro", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor("Kiro", "Win", "Trust and continue", "", true, true, true), null!)
            });
        var result = new TrustDialogDetector(new LoggerConfiguration().CreateLogger())
            .Detect(scan, new List<Candidate>());
        // No trust labels, no execution labels → not blocking
        Assert.False(result.IsBlockingTrustDialog);
    }

    // --- 2.2 Adversarial labels ---

    [Theory]
    [InlineData("Trust?")]                      // not a known prefix
    [InlineData("Trust (not recommended)")]     // not a known prefix
    [InlineData("Trust disabled")]              // not a known prefix
    public void IsTrustLabel_AdversarialLabels_NotTrust(string label)
    {
        Assert.False(TrustDialogDetector.IsTrustLabel(label));
    }

    [Fact]
    public void Detect_TrustCommandAndAcceptWithSuffix_IsTrust()
    {
        // "Trust command and accept all" starts with "trust command and accept" → IS trust
        Assert.True(TrustDialogDetector.IsTrustLabel("Trust command and accept all"));
    }

    // --- 2.3 Mixed intent ---

    [Fact]
    public void Detect_ContinueIsExecution_EvenWithTrustContext()
    {
        // "Continue" alone is an execution label — even if dialog has trust buttons
        var scan = new ScanResult("Kiro", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor("Kiro", "Win", "Full command python", "", true, true, true), null!),
                (new ElementDescriptor("Kiro", "Win", "Continue", "", true, true, true), null!)
            });
        var result = new TrustDialogDetector(new LoggerConfiguration().CreateLogger())
            .Detect(scan, new List<Candidate>());
        // "Continue" is execution → not blocking
        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_YesIsExecution_BlocksFallback()
    {
        var scan = new ScanResult("Kiro", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor("Kiro", "Win", "Base python", "", true, true, true), null!),
                (new ElementDescriptor("Kiro", "Win", "Yes", "", true, true, true), null!)
            });
        var result = new TrustDialogDetector(new LoggerConfiguration().CreateLogger())
            .Detect(scan, new List<Candidate>());
        Assert.False(result.IsBlockingTrustDialog);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 3 — HANDLER SAFETY BYPASS TESTS
// ═══════════════════════════════════════════════════════════════════

public class HandlerSafetyBypassTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private static ElementDescriptor Desc(string label) =>
        new("Kiro", "Kiro Window", label, "", true, true, true);

    private static TrustFallbackHandler MakeHandler(DebounceTracker? tracker = null) =>
        new(Logger, new MockClickExecutor(), tracker ?? new DebounceTracker(), TimeSpan.FromSeconds(2));

    private static ScanResult Scan(string process, params string[] labels) =>
        new(process, "Window", IntPtr.Zero,
            labels.Select(l => (Desc(l), (AutomationElement)null!)).ToList());

    private static Configuration SafeConfig(params string[] whitelistedProcesses)
    {
        var config = new Configuration { TrustFallbackMode = TrustFallbackMode.Safe };
        config.Whitelist = whitelistedProcesses.Select(p =>
            new WhitelistEntry { ProcessName = p, WindowTitles = new(), ButtonLabels = new() }).ToList();
        return config;
    }

    // --- 3.1 Mode gate ---

    [Fact]
    public void Handler_DefaultConfig_NeverExecutes()
    {
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null!, Desc("Full command test"), 2, 2);
        var scan = Scan("Kiro", "Full command test", "Base test");
        var config = new Configuration(); // default: Off

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Fact]
    public void Handler_ExplicitOff_NeverExecutes()
    {
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null!, Desc("Full command test"), 2, 2);
        var scan = Scan("Kiro", "Full command test");
        var config = new Configuration { TrustFallbackMode = TrustFallbackMode.Off };

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    // --- 3.2 Process restriction ---

    [Theory]
    [InlineData("Kiro", "kiro")]        // case mismatch
    [InlineData("Kiro", "KIRO")]        // all caps
    [InlineData("Kiro", "KiRo")]        // mixed case
    public void Handler_ProcessCaseMismatch_StillWhitelisted(string whitelisted, string actual)
    {
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null, Desc("Full command test"), 2, 2);
        var scan = Scan(actual, "Full command test", "Base test");
        var config = SafeConfig(whitelisted);

        // Process matching MUST be case-sensitive for trust fallback safety
        var result = handler.TryFallback(detection, scan, config, dryRun: true);
        Assert.False(result);
    }

    [Theory]
    [InlineData("chrome_fake.exe")]
    [InlineData("Kiro.exe")]           // "Kiro.exe" != "Kiro"
    [InlineData("Kiro ")]              // trailing space
    [InlineData(" Kiro")]              // leading space
    [InlineData("NotKiro")]
    public void Handler_PartialProcessMatch_Rejected(string attackProcess)
    {
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null!, Desc("Full command test"), 2, 2);
        var scan = Scan(attackProcess, "Full command test");
        var config = SafeConfig("Kiro");

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    // --- 3.3 Execution guard ---

    [Theory]
    [InlineData("accept")]
    [InlineData("ACCEPT")]
    [InlineData("Accept")]
    [InlineData("Accept Command")]
    [InlineData("RUN")]
    [InlineData("run")]
    [InlineData("YES")]
    public void Handler_ExecutionLabelCaseVariations_AllBlocked(string execLabel)
    {
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null!, Desc("Full command test"), 2, 3);
        var scan = Scan("Kiro", "Full command test", "Base test", execLabel);
        var config = SafeConfig("Kiro");

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Theory]
    [InlineData("Accept all")]      // not exact match
    [InlineData("Run anyway")]      // not exact match
    [InlineData("Yes please")]      // not exact match
    [InlineData("Approve all")]     // not exact match
    public void Handler_NonExactExecutionLabels_NotBlocked(string label)
    {
        // Non-exact execution labels MUST block the fallback.
        // "Run anyway" contains "Run" — the handler must reject to prevent
        // trust escalation when execution-like buttons are present.
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null, Desc("Full command test"), 2, 3);
        var scan = Scan("Kiro", "Full command test", label);
        var config = SafeConfig("Kiro");

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    // --- 3.4 Null element bypass ---

    [Fact]
    public void Handler_NullElement_NullDescriptor_Blocked()
    {
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null, null, 2, 2);
        var scan = Scan("Kiro", "Full command test");
        var config = SafeConfig("Kiro");

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Fact]
    public void Handler_NullElement_WithDescriptor_Blocked()
    {
        // Descriptor alone is NOT sufficient. The handler must verify
        // the descriptor is tied to a real, clickable UI element.
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null, Desc("Full command test"), 2, 2);
        var scan = Scan("Kiro", "Full command test");
        var config = SafeConfig("Kiro");

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    // --- 3.5 Config edge cases ---

    [Fact]
    public void Handler_NullWhitelist_Throws()
    {
        // BUG: Null whitelist should throw NullReferenceException when handler
        // reaches check 4 (whitelist iteration). Currently masked because check 3
        // (null element) catches it first. If element were non-null, this would crash.
        var handler = MakeHandler();
        var detection = new TrustDetectionResult(true, null!, Desc("Full command test"), 2, 2);
        var scan = Scan("Kiro", "Full command test");
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = null!
        };

        Assert.Throws<NullReferenceException>(() =>
            handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Fact]
    public void ConfigParsing_CaseSensitive_SafeUppercase_Throws()
    {
        // BUG: Config spec says valid values are "off" and "safe" (lowercase).
        // "Safe" (PascalCase) should be rejected as invalid input.
        // JsonStringEnumConverter is too permissive — accepts case-insensitive values.
        var json = """{ "trustFallbackMode": "Safe" }""";
        Assert.Throws<InvalidOperationException>(() => ConfigParser.Parse(json));
    }

    [Fact]
    public void ConfigParsing_NumericValue_Throws()
    {
        // BUG: Config spec says valid values are "off" and "safe" (strings only).
        // Numeric value 1 should be rejected. JsonStringEnumConverter silently
        // maps 1 → TrustFallbackMode.Safe, bypassing string-only validation.
        var json = """{ "trustFallbackMode": 1 }""";
        Assert.Throws<InvalidOperationException>(() => ConfigParser.Parse(json));
    }

    [Fact]
    public void ConfigParsing_EmptyString_Throws()
    {
        var json = """{ "trustFallbackMode": "" }""";
        Assert.Throws<InvalidOperationException>(() => ConfigParser.Parse(json));
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 4 — DEBOUNCE BREAK TESTS
// ═══════════════════════════════════════════════════════════════════

public class DebounceBreakTests
{
    // --- 4.1 Rapid repeated calls ---

    [Fact]
    public void Debounce_SameHashRecordedTwice_StillInCooldown()
    {
        var tracker = new DebounceTracker();
        var desc = new ElementDescriptor("Kiro", "Win", "Full command test", "", true, true, true);
        var hash = DebounceTracker.ComputeHash(desc);

        tracker.Record(hash);
        tracker.Record(hash); // overwrite

        Assert.True(tracker.IsInCooldown(hash, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Debounce_DifferentLabels_DifferentHashes()
    {
        var desc1 = new ElementDescriptor("Kiro", "Win", "Full command test1", "", true, true, true);
        var desc2 = new ElementDescriptor("Kiro", "Win", "Full command test2", "", true, true, true);

        var hash1 = DebounceTracker.ComputeHash(desc1);
        var hash2 = DebounceTracker.ComputeHash(desc2);

        Assert.NotEqual(hash1, hash2);
    }

    // --- 4.2 Slight variations ---

    [Fact]
    public void Debounce_SameDialogDifferentAutomationId_DifferentHash()
    {
        var desc1 = new ElementDescriptor("Kiro", "Win", "Full command test", "id1", true, true, true);
        var desc2 = new ElementDescriptor("Kiro", "Win", "Full command test", "id2", true, true, true);

        Assert.NotEqual(
            DebounceTracker.ComputeHash(desc1),
            DebounceTracker.ComputeHash(desc2));
    }

    [Fact]
    public void Debounce_SameDialogNoAutomationId_SameHash()
    {
        var desc1 = new ElementDescriptor("Kiro", "Win", "Full command test", "", true, true, true);
        var desc2 = new ElementDescriptor("Kiro", "Win", "Full command test", "", true, true, true);

        Assert.Equal(
            DebounceTracker.ComputeHash(desc1),
            DebounceTracker.ComputeHash(desc2));
    }

    [Fact]
    public void Debounce_SameProcessDifferentWindow_DifferentHash()
    {
        var desc1 = new ElementDescriptor("Kiro", "Window A", "Full command test", "", true, true, true);
        var desc2 = new ElementDescriptor("Kiro", "Window B", "Full command test", "", true, true, true);

        Assert.NotEqual(
            DebounceTracker.ComputeHash(desc1),
            DebounceTracker.ComputeHash(desc2));
    }

    // --- 4.3 Edge timing ---

    [Fact]
    public void Debounce_ZeroCooldown_NeverInCooldown()
    {
        var tracker = new DebounceTracker();
        tracker.Record("hash");

        // Zero cooldown means nothing is ever in cooldown
        Assert.False(tracker.IsInCooldown("hash", TimeSpan.Zero));
    }

    [Fact]
    public void Debounce_NegativeCooldown_NeverInCooldown()
    {
        var tracker = new DebounceTracker();
        tracker.Record("hash");

        Assert.False(tracker.IsInCooldown("hash", TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Debounce_VeryLargeCooldown_AlwaysInCooldown()
    {
        var tracker = new DebounceTracker();
        tracker.Record("hash");

        Assert.True(tracker.IsInCooldown("hash", TimeSpan.FromDays(365)));
    }

    // --- 4.4 Prune behavior ---

    [Fact]
    public void Debounce_PruneDoesNotRemoveFreshEntries()
    {
        var tracker = new DebounceTracker();
        tracker.Record("fresh");
        tracker.Prune();

        Assert.True(tracker.IsInCooldown("fresh", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Debounce_UnrecordedHash_NotInCooldown()
    {
        var tracker = new DebounceTracker();
        Assert.False(tracker.IsInCooldown("never_recorded", TimeSpan.FromSeconds(10)));
    }

    // --- 4.5 Hash collision resistance ---

    [Fact]
    public void Debounce_PipeInLabel_ShouldNotCauseCollision()
    {
        // BUG: Hash uses "|" as delimiter but doesn't escape "|" in field values.
        // desc1: process="Kiro", title="Win", label="Full|command" → "Kiro|Win|Full|command"
        // desc2: process="Kiro", title="Win|Full", label="command" → "Kiro|Win|Full|command"
        // These are different elements but produce the same hash → collision.
        var desc1 = new ElementDescriptor("Kiro", "Win", "Full|command", "", true, true, true);
        var desc2 = new ElementDescriptor("Kiro", "Win|Full", "command", "", true, true, true);

        var hash1 = DebounceTracker.ComputeHash(desc1);
        var hash2 = DebounceTracker.ComputeHash(desc2);

        // Different elements MUST produce different hashes
        Assert.NotEqual(hash1, hash2);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 5 — PROPERTY-BASED FUZZING
// ═══════════════════════════════════════════════════════════════════

public class AdversarialPropertyTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();
    private static readonly TrustDialogDetector Detector = new(Logger);

    private static ElementDescriptor Desc(string label) =>
        new("Kiro", "Kiro Window", label, "", true, true, true);

    private static ScanResult Scan(IEnumerable<string> labels) =>
        new("Kiro", "Kiro Window", IntPtr.Zero,
            labels.Select(l => (Desc(l), (AutomationElement)null!)).ToList());

    // --- Invariant: No execution when no trust button exists ---

    [Property(MaxTest = 200)]
    public bool NoTrustLabels_NeverBlocking(NonEmptyString label1, NonEmptyString label2)
    {
        var l1 = label1.Get;
        var l2 = label2.Get;

        // Skip if any label happens to be a trust prefix
        if (TrustDialogDetector.IsTrustLabel(SafetyFilter.NormalizeLabel(l1))) return true;
        if (TrustDialogDetector.IsTrustLabel(SafetyFilter.NormalizeLabel(l2))) return true;

        var scan = Scan(new[] { l1, l2 });
        var result = Detector.Detect(scan, new List<Candidate>());
        return !result.IsBlockingTrustDialog;
    }

    // --- Invariant: Execution label always prevents blocking ---

    [Property(MaxTest = 200)]
    public bool ExecutionLabelPresent_NeverBlocking(int execIdx)
    {
        var execLabels = new[] { "Accept", "Accept command", "Run", "Allow", "Approve", "Continue", "Yes" };
        var idx = Math.Abs(execIdx) % execLabels.Length;

        var scan = Scan(new[] { "Full command test", "Base test", execLabels[idx] });
        var result = Detector.Detect(scan, new List<Candidate>());
        return !result.IsBlockingTrustDialog;
    }

    // --- Invariant: Mode Off → handler always returns false ---

    [Property(MaxTest = 100)]
    public bool ModeOff_HandlerAlwaysFalse(bool isBlocking, bool hasDesc)
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

        var desc = hasDesc ? Desc("Full command test") : null;
        var detection = new TrustDetectionResult(isBlocking, null, desc, 2, 3);
        var scan = Scan(new[] { "Full command test" });

        return !handler.TryFallback(detection, scan, config, dryRun: true);
    }

    // --- Invariant: Empty scan → never blocking ---

    [Property(MaxTest = 50)]
    public bool EmptyScan_NeverBlocking(byte candidateCount)
    {
        var scan = new ScanResult("Kiro", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>());
        var candidates = new List<Candidate>();
        for (int i = 0; i < candidateCount % 5; i++)
        {
            candidates.Add(new Candidate(Desc("Accept"), new WhitelistEntry { ProcessName = "Kiro" }, $"hash{i}"));
        }

        var result = Detector.Detect(scan, candidates);
        return !result.IsBlockingTrustDialog;
    }

    // --- Fuzz: Random garbage labels ---

    [Property(MaxTest = 300)]
    public bool RandomGarbageLabels_InvariantsHold(NonEmptyString s1, NonEmptyString s2, NonEmptyString s3)
    {
        var labels = new[] { s1.Get, s2.Get, s3.Get };
        var scan = Scan(labels);
        var result = Detector.Detect(scan, new List<Candidate>());

        bool hasTrust = labels.Any(l => TrustDialogDetector.IsTrustLabel(SafetyFilter.NormalizeLabel(l)));
        bool hasExec = labels.Any(l => TrustDialogDetector.IsExecutionLabel(SafetyFilter.NormalizeLabel(l)));

        if (!hasTrust) return !result.IsBlockingTrustDialog;
        if (hasExec) return !result.IsBlockingTrustDialog;
        return result.IsBlockingTrustDialog;
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 6 — INTEGRATION FAILURE CHAINS
// ═══════════════════════════════════════════════════════════════════

public class IntegrationFailureChainTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private static ElementDescriptor Desc(string label) =>
        new("Kiro", "Kiro Window", label, "", true, true, true);

    private static ScanResult Scan(params string[] labels) =>
        new("Kiro", "Kiro Window", IntPtr.Zero,
            labels.Select(l => (Desc(l), (AutomationElement)null!)).ToList());

    // --- 6.1 Detector passes incorrect classification → handler must still block ---

    [Fact]
    public void Chain_DetectorSaysBlocking_HandlerStillChecksElement()
    {
        // Even if we manually construct a "blocking" detection, handler checks element
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var detection = new TrustDetectionResult(true, null, null, 2, 2); // no element
        var scan = Scan("Full command test", "Base test");
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "Kiro", WindowTitles = new(), ButtonLabels = new() }
            }
        };

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Fact]
    public void Chain_DetectorSaysBlocking_HandlerStillChecksProcess()
    {
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var detection = new TrustDetectionResult(true, null!, Desc("Full command test"), 2, 2);
        var scan = new ScanResult("MaliciousApp", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor("MaliciousApp", "Win", "Full command test", "", true, true, true), null!)
            });
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "Kiro", WindowTitles = new(), ButtonLabels = new() }
            }
        };

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    // --- 6.2 Full pipeline: Detect → Handler with various scenarios ---

    [Fact]
    public void Pipeline_NormalFlow_AcceptPresent_NoFallback()
    {
        var detector = new TrustDialogDetector(Logger);
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));

        var scan = Scan("Full command python", "Accept command");
        var detection = detector.Detect(scan, new List<Candidate>());

        // Detector says not blocking (Accept command is execution)
        Assert.False(detection.IsBlockingTrustDialog);
        // Handler also rejects
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "Kiro", WindowTitles = new(), ButtonLabels = new() }
            }
        };
        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Fact]
    public void Pipeline_BlockingFlow_OnlyTrustLabels_DetectsCorrectly()
    {
        var detector = new TrustDialogDetector(Logger);
        var scan = Scan("Full command python -m pytest", "Base python", "Partial python -m");
        var detection = detector.Detect(scan, new List<Candidate>());

        Assert.True(detection.IsBlockingTrustDialog);
        Assert.NotNull(detection.FullCommandDescriptor);
        Assert.Equal(3, detection.TrustLabelCount);
    }

    [Fact]
    public void Pipeline_BlockingFlow_TrustPlusCancel_DetectsCorrectly()
    {
        var detector = new TrustDialogDetector(Logger);
        var scan = Scan("Full command python", "Base python", "Cancel");
        var detection = detector.Detect(scan, new List<Candidate>());

        Assert.True(detection.IsBlockingTrustDialog);
        Assert.Equal(2, detection.TrustLabelCount);
    }

    // --- 6.3 Debounce integration ---

    [Fact]
    public void Pipeline_DebounceBlocksSecondAttempt()
    {
        var tracker = new DebounceTracker();
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), tracker, TimeSpan.FromSeconds(2));

        var desc = Desc("Full command python");
        var hash = DebounceTracker.ComputeHash(desc);
        tracker.Record(hash); // simulate previous click

        var detection = new TrustDetectionResult(true, null!, desc, 2, 2);
        var scan = Scan("Full command python", "Base python");
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "Kiro", WindowTitles = new(), ButtonLabels = new() }
            }
        };

        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    // --- 6.4 Wildcard process in whitelist ---

    [Fact]
    public void Pipeline_WildcardProcess_MatchesAnything()
    {
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var detection = new TrustDetectionResult(true, null, Desc("Full command test"), 2, 2);
        var scan = new ScanResult("AnyProcess", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor("AnyProcess", "Win", "Full command test", "", true, true, true), null!)
            });
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "*", WindowTitles = new(), ButtonLabels = new() }
            }
        };

        // VULNERABILITY: Wildcard "*" matches via string.Equals("AnyProcess", "*") → false
        // So wildcard does NOT bypass the process check. This is actually SAFE behavior.
        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }

    [Fact]
    public void Pipeline_WildcardProcess_OnlyMatchesLiteralStar()
    {
        var handler = new TrustFallbackHandler(Logger, new MockClickExecutor(), new DebounceTracker(), TimeSpan.FromSeconds(2));
        var detection = new TrustDetectionResult(true, null, Desc("Full command test"), 2, 2);
        var scan = new ScanResult("*", "Win", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>
            {
                (new ElementDescriptor("*", "Win", "Full command test", "", true, true, true), null!)
            });
        var config = new Configuration
        {
            TrustFallbackMode = TrustFallbackMode.Safe,
            Whitelist = new List<WhitelistEntry>
            {
                new() { ProcessName = "*", WindowTitles = new(), ButtonLabels = new() }
            }
        };

        // Process name "*" matches whitelist entry "*" literally.
        // But descriptor validation blocks execution — descriptor must be verified.
        Assert.False(handler.TryFallback(detection, scan, config, dryRun: true));
    }
}
