using System.Windows.Automation;
using ClickRun.Clicking;
using ClickRun.Config;
using ClickRun.Detection;
using ClickRun.Filtering;
using ClickRun.Models;
using ClickRun.Tracking;
using Serilog;
using Serilog.Events;
using Xunit;

using Configuration = ClickRun.Models.Configuration;

namespace ClickRun.Tests;

/// <summary>
/// Captures log events in memory for assertion.
/// </summary>
internal sealed class LogCapture : IDisposable
{
    private readonly List<LogEvent> _events = new();
    public ILogger Logger { get; }
    public IReadOnlyList<LogEvent> Events => _events;

    public LogCapture()
    {
        Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new DelegateSink(e => _events.Add(e)))
            .CreateLogger();
    }

    public List<string> InfoMessages =>
        _events.Where(e => e.Level == LogEventLevel.Information)
               .Select(e => e.RenderMessage()).ToList();

    public List<string> DebugMessages =>
        _events.Where(e => e.Level == LogEventLevel.Debug)
               .Select(e => e.RenderMessage()).ToList();

    public List<string> AllMessages =>
        _events.Select(e => e.RenderMessage()).ToList();

    public bool HasMessage(string substring) =>
        _events.Any(e => e.RenderMessage().Contains(substring, StringComparison.OrdinalIgnoreCase));

    public void Dispose() { }
}

internal sealed class DelegateSink : Serilog.Core.ILogEventSink
{
    private readonly Action<LogEvent> _action;
    public DelegateSink(Action<LogEvent> action) => _action = action;
    public void Emit(LogEvent logEvent) => _action(logEvent);
}

/// <summary>
/// Simulates a full scan cycle: SafetyFilter → ButtonPrioritizer → TrustDialogDetector → TrustFallbackHandler.
/// Uses a MockClickExecutor so the real TrustFallbackHandler executes all safety checks
/// without requiring real AutomationElement objects.
/// </summary>
internal sealed class PipelineSimulator
{
    private readonly SafetyFilter _safetyFilter;
    private readonly TrustDialogDetector _trustDetector;
    private readonly TrustFallbackHandler _trustHandler;
    private readonly MockClickExecutor _mockExecutor;
    private readonly DebounceTracker _debounceTracker;
    private readonly Configuration _config;
    private readonly ILogger _logger;

    public int TotalClicked { get; private set; }
    public List<string> ClickedLabels { get; } = new();
    public List<string> FallbackLabels { get; } = new();

    public PipelineSimulator(Configuration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _safetyFilter = new SafetyFilter(logger);
        _debounceTracker = new DebounceTracker();
        _trustDetector = new TrustDialogDetector(logger);
        _mockExecutor = new MockClickExecutor();
        _trustHandler = new TrustFallbackHandler(
            logger, _mockExecutor, _debounceTracker,
            TimeSpan.FromMilliseconds(config.DebounceCooldownMs));
    }

    /// <summary>
    /// Simulates one scan cycle. Returns the number of clicks (0 or 1).
    /// The fallback path calls the REAL TrustFallbackHandler with all safety checks.
    /// </summary>
    public int RunCycle(ScanResult scanResult)
    {
        // Phase 1: Safety filter all buttons
        var candidates = new List<Candidate>();
        foreach (var (descriptor, element) in scanResult.Buttons)
        {
            var filterResult = _safetyFilter.Check(descriptor, _config);
            if (!filterResult.Passed) continue;

            var hash = DebounceTracker.ComputeHash(descriptor);
            if (_debounceTracker.IsInCooldown(hash, TimeSpan.FromMilliseconds(_config.DebounceCooldownMs)))
                continue;

            candidates.Add(new Candidate(descriptor, filterResult.MatchedEntry!, hash));
        }

        int clicked = 0;

        if (candidates.Count == 0)
        {
            // Phase 3: Trust fallback — calls REAL handler with all safety checks
            if (_config.TrustFallbackMode == TrustFallbackMode.Safe)
            {
                var detection = _trustDetector.Detect(scanResult, candidates);
                if (_trustHandler.TryFallback(detection, scanResult, _config, dryRun: true))
                {
                    clicked = 1;
                    FallbackLabels.Add(detection.FullCommandDescriptor!.ButtonLabel);
                }
            }
        }
        else
        {
            // Phase 2: Prioritize and "click" best candidate
            var best = ButtonPrioritizer.SelectBest(candidates, _config.Whitelist);
            if (best != null)
            {
                _logger.Information("[DRY RUN] Would click: {Label}", best.Element.ButtonLabel);
                _debounceTracker.Record(best.Hash);
                clicked = 1;
                ClickedLabels.Add(best.Element.ButtonLabel);
            }
        }

        TotalClicked += clicked;
        _debounceTracker.Prune();
        return clicked;
    }
}

// ═══════════════════════════════════════════════════════════════════
// Helper to build scan results
// ═══════════════════════════════════════════════════════════════════

internal static class ScanBuilder
{
    public static ScanResult Build(string process, string window, params string[] labels) =>
        new(process, window, IntPtr.Zero,
            labels.Select(l => (
                new ElementDescriptor(process, window, l, "", true, true, true),
                (AutomationElement)null!
            )).ToList());

    public static ScanResult Build(string process, string window,
        params (string label, bool visible, bool enabled)[] buttons) =>
        new(process, window, IntPtr.Zero,
            buttons.Select(b => (
                new ElementDescriptor(process, window, b.label, "", true, b.visible, b.enabled),
                (AutomationElement)null!
            )).ToList());
}

// ═══════════════════════════════════════════════════════════════════
// Standard config factory
// ═══════════════════════════════════════════════════════════════════

internal static class TestConfigs
{
    public static Configuration KiroDefault(TrustFallbackMode mode = TrustFallbackMode.Off) => new()
    {
        TrustFallbackMode = mode,
        DryRun = true,
        FirstSeenDelayMs = 0,
        DebounceCooldownMs = 2000,
        BlockedLabels = new List<string> { "Reject", "Cancel", "Deny" },
        Whitelist = new List<WhitelistEntry>
        {
            new()
            {
                ProcessName = "Kiro",
                WindowTitles = new List<WindowTitlePattern>
                {
                    new() { Pattern = "Kiro", MatchMode = MatchMode.Contains }
                },
                ButtonLabels = new List<string>
                {
                    "Run", "Allow", "Approve", "Continue", "Yes",
                    "Accept", "Accept command"
                }
            }
        }
    };
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 1 — BASIC FLOW VALIDATION
// ═══════════════════════════════════════════════════════════════════

public class BasicFlowValidationTests
{
    [Fact]
    public void Section1_Dialog1_NormalFlow_ClicksAcceptCommand_IgnoresTrust()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Trust command and accept", "Accept command", "Run");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked);
        Assert.Single(sim.ClickedLabels);
        Assert.Equal("Accept command", sim.ClickedLabels[0]); // highest priority execution button
        Assert.Empty(sim.FallbackLabels); // trust fallback NOT triggered
    }

    [Fact]
    public void Section1_Dialog2_TrustOnly_ModeOff_DoesNothing()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Off), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python test.py", "Base python");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(0, clicked);
        Assert.Empty(sim.ClickedLabels);
        Assert.Empty(sim.FallbackLabels);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 2 — FALLBACK MODE TEST
// ═══════════════════════════════════════════════════════════════════

public class FallbackModeTests
{
    [Fact]
    public void Section2_SafeMode_ClicksOnlyFullCommand()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python test.py", "Base python");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked);
        Assert.Empty(sim.ClickedLabels); // not via normal flow
        Assert.Single(sim.FallbackLabels);
        Assert.Equal("Full command python test.py", sim.FallbackLabels[0]);
        Assert.True(log.HasMessage("DRY RUN"));
        Assert.True(log.HasMessage("Full command"));
    }

    [Fact]
    public void Section2_SafeMode_NeverClicksBase()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        // Only Base and Partial — no Full command available
        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Base python", "Partial python -m");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(0, clicked); // No Full command → can't fallback
        Assert.Empty(sim.FallbackLabels);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 3 — MIXED DIALOG (REAL-WORLD UI NOISE)
// ═══════════════════════════════════════════════════════════════════

public class MixedDialogTests
{
    [Fact]
    public void Section3_TrustPlusCancel_StillBlocking_ClicksFullCommand()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        // Cancel is blocked by BlockedLabels, but that only affects SafetyFilter candidates.
        // For trust detection, Cancel is neither trust nor execution → blocking.
        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python script.py", "Base python", "Cancel");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked);
        Assert.Single(sim.FallbackLabels);
        Assert.Equal("Full command python script.py", sim.FallbackLabels[0]);
    }

    [Fact]
    public void Section3_TrustPlusClose_Blocking()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python", "Partial python -m", "Close");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked);
        Assert.Single(sim.FallbackLabels);
        Assert.Equal("Full command python", sim.FallbackLabels[0]);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 4 — EXECUTION PRIORITY CHECK
// ═══════════════════════════════════════════════════════════════════

public class ExecutionPriorityTests
{
    [Fact]
    public void Section4_FullCommandPlusAcceptCommand_ClicksAcceptCommand()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python", "Accept command");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked);
        Assert.Single(sim.ClickedLabels);
        Assert.Equal("Accept command", sim.ClickedLabels[0]); // normal flow wins
        Assert.Empty(sim.FallbackLabels); // fallback NOT triggered
    }

    [Fact]
    public void Section4_TrustPlusRun_ClicksRun()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python", "Base python", "Run");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked);
        Assert.Single(sim.ClickedLabels);
        Assert.Equal("Run", sim.ClickedLabels[0]);
        Assert.Empty(sim.FallbackLabels);
    }

    [Fact]
    public void Section4_MultipleExecution_HighestPriorityWins()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Run", "Accept command", "Allow");

        sim.RunCycle(scan);

        Assert.Single(sim.ClickedLabels);
        Assert.Equal("Accept command", sim.ClickedLabels[0]); // weight 100 > 80 > 40
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 5 — FALSE POSITIVE DEFENSE
// ═══════════════════════════════════════════════════════════════════

public class FalsePositiveDefenseTests
{
    [Theory]
    [InlineData("Baseball stats")]
    [InlineData("Baseline report")]
    [InlineData("Partially complete")]
    public void Section5_FalsePositiveLabels_NotTreatedAsTrust(string label)
    {
        Assert.False(TrustDialogDetector.IsTrustLabel(label));
    }

    [Fact]
    public void Section5_FalsePositiveDialog_NoFallbackTriggered()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Baseball stats", "Baseline report", "Partially complete");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(0, clicked);
        Assert.Empty(sim.FallbackLabels);
        Assert.Empty(sim.ClickedLabels);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 6 — DEBOUNCE TEST
// ═══════════════════════════════════════════════════════════════════

public class DebounceIntegrationTests
{
    [Fact]
    public void Section6_RapidRepeat_OnlyOneClick()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python test.py", "Base python");

        // First cycle: should click
        Assert.Equal(1, sim.RunCycle(scan));
        // Rapid repeats: debounce blocks
        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Equal(0, sim.RunCycle(scan));

        Assert.Equal(1, sim.TotalClicked);
        Assert.Single(sim.FallbackLabels);
    }

    [Fact]
    public void Section6_NormalFlow_DebounceBlocksRepeat()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro", "Accept command");

        Assert.Equal(1, sim.RunCycle(scan));
        Assert.Equal(0, sim.RunCycle(scan)); // debounced
        Assert.Equal(0, sim.RunCycle(scan)); // debounced

        Assert.Equal(1, sim.TotalClicked);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 7 — CONFIG VALIDATION
// ═══════════════════════════════════════════════════════════════════

public class ConfigValidationIntegrationTests
{
    [Theory]
    [InlineData("""{ "trustFallbackMode": "Safe" }""")]
    [InlineData("""{ "trustFallbackMode": 1 }""")]
    [InlineData("""{ "trustFallbackMode": "random" }""")]
    [InlineData("""{ "trustFallbackMode": "OFF" }""")]
    [InlineData("""{ "trustFallbackMode": "SAFE" }""")]
    [InlineData("""{ "trustFallbackMode": "" }""")]
    [InlineData("""{ "trustFallbackMode": true }""")]
    [InlineData("""{ "trustFallbackMode": null }""")]
    public void Section7_InvalidConfigs_ThrowInvalidOperationException(string json)
    {
        Assert.Throws<InvalidOperationException>(() => ConfigParser.Parse(json));
    }

    [Theory]
    [InlineData("""{ "trustFallbackMode": "off" }""", TrustFallbackMode.Off)]
    [InlineData("""{ "trustFallbackMode": "safe" }""", TrustFallbackMode.Safe)]
    public void Section7_ValidConfigs_ParseCorrectly(string json, TrustFallbackMode expected)
    {
        var config = ConfigParser.Parse(json);
        Assert.Equal(expected, config.TrustFallbackMode);
    }

    [Fact]
    public void Section7_MissingField_DefaultsToOff()
    {
        var config = ConfigParser.Parse("""{ "scanIntervalMs": 500 }""");
        Assert.Equal(TrustFallbackMode.Off, config.TrustFallbackMode);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 8 — PROCESS VALIDATION
// ═══════════════════════════════════════════════════════════════════

public class ProcessValidationIntegrationTests
{
    [Fact]
    public void Section8_UnknownProcess_NoClick()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("UnknownApp", "Unknown Window",
            "Full command python", "Base python");

        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Empty(sim.ClickedLabels);
        Assert.Empty(sim.FallbackLabels);
    }

    [Fact]
    public void Section8_NonWhitelistedProcess_NormalFlowAlsoBlocked()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Chrome", "Chrome Window",
            "Accept command", "Run");

        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Empty(sim.ClickedLabels);
    }

    [Fact]
    public void Section8_ProcessCaseMismatch_StillWorks()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        // "kiro" (lowercase) should match whitelist "Kiro"
        var scan = ScanBuilder.Build("kiro", "test - Kiro", "Accept command");

        Assert.Equal(1, sim.RunCycle(scan));
        Assert.Single(sim.ClickedLabels);
        Assert.Equal("Accept command", sim.ClickedLabels[0]);
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 9 — FAILURE RESILIENCE
// ═══════════════════════════════════════════════════════════════════

public class FailureResilienceTests
{
    [Fact]
    public void Section9_DisabledButton_NotClicked()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            ("Accept command", true, false)); // visible but disabled

        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Empty(sim.ClickedLabels);
    }

    [Fact]
    public void Section9_HiddenButton_NotClicked()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            ("Accept command", false, true)); // hidden

        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Empty(sim.ClickedLabels);
    }

    [Fact]
    public void Section9_DisabledFullCommand_FallbackSkips()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            ("Full command python", true, false), // disabled
            ("Base python", true, true));

        // Detector sees trust labels → blocking, but Full command is disabled → no element tracked
        Assert.Equal(0, sim.RunCycle(scan));
        Assert.Empty(sim.FallbackLabels);
    }

    [Fact]
    public void Section9_EmptyDialog_NoCrash()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = new ScanResult("Kiro", "test - Kiro", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>());

        Assert.Equal(0, sim.RunCycle(scan));
    }

    [Fact]
    public void Section9_NullLabel_NoCrash()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var buttons = new List<(ElementDescriptor, AutomationElement)>
        {
            (new ElementDescriptor("Kiro", "test - Kiro", null!, "", true, true, true), null!)
        };
        var scan = new ScanResult("Kiro", "test - Kiro", IntPtr.Zero, buttons);

        Assert.Equal(0, sim.RunCycle(scan));
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 10 — FULL PIPELINE TEST
// ═══════════════════════════════════════════════════════════════════

public class FullPipelineTests
{
    [Fact]
    public void Section10_ThreeCycles_CorrectBehavior()
    {
        using var log = new LogCapture();
        var config = TestConfigs.KiroDefault(TrustFallbackMode.Safe);
        config.DebounceCooldownMs = 100; // short cooldown for test
        var sim = new PipelineSimulator(config, log.Logger);

        // Cycle 1: Normal flow — Accept command present
        var scan1 = ScanBuilder.Build("Kiro", "test - Kiro",
            "Accept command", "Full command python", "Base python");
        Assert.Equal(1, sim.RunCycle(scan1));
        Assert.Equal("Accept command", sim.ClickedLabels.Last());
        Assert.Empty(sim.FallbackLabels);

        // Cycle 2: Blocking trust dialog — only trust labels
        var scan2 = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python -m pytest", "Base python", "Partial python");
        Assert.Equal(1, sim.RunCycle(scan2));
        Assert.Single(sim.FallbackLabels);
        Assert.Equal("Full command python -m pytest", sim.FallbackLabels[0]);

        // Cycle 3: Empty dialog — idle
        var scan3 = new ScanResult("Kiro", "test - Kiro", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>());
        Assert.Equal(0, sim.RunCycle(scan3));

        Assert.Equal(2, sim.TotalClicked); // exactly 2 clicks total
    }

    [Fact]
    public void Section10_SingleClickPerCycle_Always()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        // Dialog with both execution and trust buttons
        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Accept command", "Run", "Allow", "Full command python");

        var clicked = sim.RunCycle(scan);

        Assert.Equal(1, clicked); // exactly one
        Assert.Single(sim.ClickedLabels); // only one label recorded
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 11 — LOG VERIFICATION
// ═══════════════════════════════════════════════════════════════════

public class LogVerificationTests
{
    [Fact]
    public void Section11_NormalClick_LoggedCorrectly()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro", "Accept command");
        sim.RunCycle(scan);

        Assert.True(log.HasMessage("Accept command"));
        Assert.True(log.HasMessage("DRY RUN"));
    }

    [Fact]
    public void Section11_FallbackClick_LoggedCorrectly()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python test.py", "Base python");
        sim.RunCycle(scan);

        Assert.True(log.HasMessage("DRY RUN"));
        Assert.True(log.HasMessage("Full command"));
        Assert.True(log.HasMessage("Blocking trust dialog detected"));
    }

    [Fact]
    public void Section11_DebounceBlock_LoggedCorrectly()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python", "Base python");

        sim.RunCycle(scan); // first: clicks
        sim.RunCycle(scan); // second: debounced

        Assert.True(log.HasMessage("Debounce active"));
    }

    [Fact]
    public void Section11_NoUnexpectedLabelsClicked()
    {
        using var log = new LogCapture();
        var sim = new PipelineSimulator(TestConfigs.KiroDefault(TrustFallbackMode.Safe), log.Logger);

        var scan = ScanBuilder.Build("Kiro", "test - Kiro",
            "Full command python", "Base python", "Partial python");
        sim.RunCycle(scan);

        // Verify ONLY "Full command" appears in click logs, never Base or Partial
        var infoLogs = log.InfoMessages;
        Assert.DoesNotContain(infoLogs, m => m.Contains("Base python"));
        Assert.DoesNotContain(infoLogs, m => m.Contains("Partial python"));
    }
}

// ═══════════════════════════════════════════════════════════════════
// SECTION 12 — STRESS TEST
// ═══════════════════════════════════════════════════════════════════

public class StressTests
{
    [Fact]
    public void Section12_100Cycles_RandomDialogs_NoIncorrectClicks()
    {
        using var log = new LogCapture();
        var config = TestConfigs.KiroDefault(TrustFallbackMode.Safe);
        config.DebounceCooldownMs = 50; // short cooldown for stress test
        var sim = new PipelineSimulator(config, log.Logger);
        var rng = new Random(42); // deterministic seed

        var trustLabels = new[] { "Full command python", "Base python", "Partial python -m" };
        var execLabels = new[] { "Accept command", "Run", "Allow", "Accept" };
        var otherLabels = new[] { "Cancel", "Close", "Settings", "Help" };
        var falsePositives = new[] { "Baseball stats", "Baseline report", "Partially complete" };

        int totalClicks = 0;
        int fallbackClicks = 0;
        int normalClicks = 0;

        for (int i = 0; i < 100; i++)
        {
            // Random button combination
            var labels = new List<string>();
            var category = rng.Next(5);

            switch (category)
            {
                case 0: // Trust only → fallback should trigger
                    labels.AddRange(trustLabels.Take(rng.Next(1, 4)));
                    break;
                case 1: // Trust + execution → normal flow
                    labels.Add(trustLabels[0]);
                    labels.Add(execLabels[rng.Next(execLabels.Length)]);
                    break;
                case 2: // Execution only → normal flow
                    labels.Add(execLabels[rng.Next(execLabels.Length)]);
                    break;
                case 3: // Trust + other (Cancel, Close) → fallback
                    labels.Add(trustLabels[0]);
                    labels.Add(otherLabels[rng.Next(otherLabels.Length)]);
                    break;
                case 4: // False positives → no click
                    labels.Add(falsePositives[rng.Next(falsePositives.Length)]);
                    break;
            }

            var prevFallback = sim.FallbackLabels.Count;
            var prevNormal = sim.ClickedLabels.Count;

            // Small delay to let debounce expire between different dialogs
            Thread.Sleep(60);

            var scan = ScanBuilder.Build("Kiro", "test - Kiro", labels.ToArray());
            var clicked = sim.RunCycle(scan);

            totalClicks += clicked;
            if (sim.FallbackLabels.Count > prevFallback) fallbackClicks++;
            if (sim.ClickedLabels.Count > prevNormal) normalClicks++;

            // INVARIANT: At most 1 click per cycle
            Assert.True(clicked <= 1, $"Cycle {i}: clicked {clicked} times");
        }

        // Verify no false positive labels were ever clicked
        Assert.DoesNotContain(sim.ClickedLabels, l => l.Contains("Baseball"));
        Assert.DoesNotContain(sim.ClickedLabels, l => l.Contains("Baseline"));
        Assert.DoesNotContain(sim.ClickedLabels, l => l.Contains("Partially"));

        // Verify fallback only clicked "Full command"
        Assert.All(sim.FallbackLabels, l => Assert.StartsWith("Full command", l));

        // Verify no "Base" or "Partial" was ever clicked
        Assert.DoesNotContain(sim.ClickedLabels, l => l.StartsWith("Base "));
        Assert.DoesNotContain(sim.ClickedLabels, l => l.StartsWith("Partial "));
        Assert.DoesNotContain(sim.FallbackLabels, l => l.StartsWith("Base "));
        Assert.DoesNotContain(sim.FallbackLabels, l => l.StartsWith("Partial "));
    }

    [Fact]
    public void Section12_200Cycles_AllTrustDialogs_OnlyFullCommandClicked()
    {
        using var log = new LogCapture();
        var config = TestConfigs.KiroDefault(TrustFallbackMode.Safe);
        config.DebounceCooldownMs = 10; // very short for rapid cycling
        var sim = new PipelineSimulator(config, log.Logger);

        for (int i = 0; i < 200; i++)
        {
            Thread.Sleep(15); // let debounce expire
            var scan = ScanBuilder.Build("Kiro", $"test {i} - Kiro",
                $"Full command python -m test_{i}", "Base python", "Partial python");
            sim.RunCycle(scan);
        }

        // Every fallback click must be "Full command"
        Assert.All(sim.FallbackLabels, l => Assert.StartsWith("Full command", l));
        Assert.True(sim.FallbackLabels.Count > 0, "Expected at least some fallback clicks");
        Assert.Empty(sim.ClickedLabels); // no normal clicks
    }
}
