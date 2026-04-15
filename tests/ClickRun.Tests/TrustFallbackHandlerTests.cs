using System.Windows.Automation;
using ClickRun.Clicking;
using ClickRun.Detection;
using ClickRun.Filtering;
using ClickRun.Models;
using ClickRun.Tracking;
using Serilog;
using Xunit;

namespace ClickRun.Tests;

/// <summary>
/// Mock click executor for testing. Records clicks and returns configurable results.
/// </summary>
internal sealed class MockClickExecutor : IClickExecutor
{
    public List<string> ClickedLabels { get; } = new();
    public bool ShouldSucceed { get; set; } = true;

    public ClickResult Click(ElementDescriptor descriptor, int preClickDelayMs = 0)
    {
        ClickedLabels.Add(descriptor.ButtonLabel);
        return ShouldSucceed
            ? new ClickResult(true)
            : new ClickResult(false, "Mock click failure");
    }
}

public class TrustFallbackHandlerTests
{
    private readonly ILogger _logger;
    private readonly MockClickExecutor _mockExecutor;
    private readonly DebounceTracker _debounceTracker;
    private readonly TrustFallbackHandler _handler;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    public TrustFallbackHandlerTests()
    {
        _logger = new LoggerConfiguration().CreateLogger();
        _mockExecutor = new MockClickExecutor();
        _debounceTracker = new DebounceTracker();
        _handler = new TrustFallbackHandler(_logger, _mockExecutor, _debounceTracker, Cooldown);
    }

    private static ElementDescriptor FullCommandDescriptor() =>
        new("Kiro", "Kiro Window", "Full command python -m pytest", "", true, true, true);

    private static ElementDescriptor MakeDescriptor(string label) =>
        new("Kiro", "Kiro Window", label, "", true, true, true);

    /// <summary>
    /// Creates a blocking detection and scan that share the same descriptor reference,
    /// as would happen in real runtime when TrustDialogDetector.Detect stores the
    /// descriptor from the scan result.
    /// </summary>
    private static (TrustDetectionResult Detection, ScanResult Scan) MakeBlockingScanWithFullCommand(
        params string[] extraLabels)
    {
        var fullDesc = FullCommandDescriptor();
        var buttons = new List<(ElementDescriptor, AutomationElement)>
        {
            (fullDesc, null!)
        };
        foreach (var label in extraLabels)
            buttons.Add((MakeDescriptor(label), null!));

        var scan = new ScanResult("Kiro", "Kiro Window", IntPtr.Zero, buttons);
        var detector = new ClickRun.Detection.TrustDialogDetector(
            new LoggerConfiguration().CreateLogger());
        var detection = detector.Detect(scan, new List<Candidate>());
        return (detection, scan);
    }

    private static TrustDetectionResult BlockingWithFullCommand() =>
        new(true, null!, FullCommandDescriptor(), 3, 3);

    private static TrustDetectionResult BlockingWithoutFullCommand() =>
        new(true, null, null, 2, 2);

    private static TrustDetectionResult NotBlocking() =>
        new(false, null, null, 0, 3);

    private static ScanResult MakeScan(params string[] labels)
    {
        var buttons = labels.Select(l =>
            (MakeDescriptor(l), (AutomationElement)null!)).ToList();
        return new ScanResult("Kiro", "Kiro Window", IntPtr.Zero, buttons);
    }

    private static Configuration SafeConfig() => new()
    {
        TrustFallbackMode = TrustFallbackMode.Safe,
        Whitelist = new List<WhitelistEntry>
        {
            new()
            {
                ProcessName = "Kiro",
                WindowTitles = new List<WindowTitlePattern>
                {
                    new() { Pattern = "Kiro", MatchMode = MatchMode.Contains }
                },
                ButtonLabels = new List<string> { "Run", "Accept" }
            }
        }
    };

    private static Configuration OffConfig() => new()
    {
        TrustFallbackMode = TrustFallbackMode.Off,
        Whitelist = new List<WhitelistEntry>
        {
            new()
            {
                ProcessName = "Kiro",
                WindowTitles = new List<WindowTitlePattern>(),
                ButtonLabels = new List<string>()
            }
        }
    };

    // --- Safety Check 1: Mode must be Safe ---

    [Fact]
    public void TryFallback_ModeOff_ReturnsFalse()
    {
        var scan = MakeScan("Full command python", "Base python");
        var result = _handler.TryFallback(BlockingWithFullCommand(), scan, OffConfig(), dryRun: false);
        Assert.False(result);
        Assert.Empty(_mockExecutor.ClickedLabels);
    }

    [Fact]
    public void TryFallback_ModeOff_WithBlockingDialog_ReturnsFalse()
    {
        var scan = MakeScan("Full command python", "Base python", "Partial python");
        var result = _handler.TryFallback(BlockingWithFullCommand(), scan, OffConfig(), dryRun: true);
        Assert.False(result);
    }

    // --- Safety Check 2: Must be blocking ---

    [Fact]
    public void TryFallback_NotBlocking_ReturnsFalse()
    {
        var scan = MakeScan("Full command python", "Accept command");
        var result = _handler.TryFallback(NotBlocking(), scan, SafeConfig(), dryRun: false);
        Assert.False(result);
    }

    // --- Safety Check 3: Must have FullCommandDescriptor ---

    [Fact]
    public void TryFallback_NoFullCommandElement_ReturnsFalse()
    {
        var scan = MakeScan("Base python", "Partial python");
        var result = _handler.TryFallback(BlockingWithoutFullCommand(), scan, SafeConfig(), dryRun: false);
        Assert.False(result);
    }

    [Fact]
    public void TryFallback_NullElement_WithDescriptor_ReturnsFalse()
    {
        // Descriptor is null → check 3 catches it
        var detection = new TrustDetectionResult(true, null, null, 3, 3);
        var scan = MakeScan("Full command python", "Base python");
        var result = _handler.TryFallback(detection, scan, SafeConfig(), dryRun: true);
        Assert.False(result);
    }

    // --- Safety Check 4: Process must be whitelisted ---

    [Fact]
    public void TryFallback_NonWhitelistedProcess_ReturnsFalse()
    {
        var buttons = new List<(ElementDescriptor, AutomationElement)>
        {
            (new ElementDescriptor("UnknownApp", "Unknown", "Full command python", "", true, true, true), null!)
        };
        var scan = new ScanResult("UnknownApp", "Unknown Window", IntPtr.Zero, buttons);
        var result = _handler.TryFallback(BlockingWithFullCommand(), scan, SafeConfig(), dryRun: false);
        Assert.False(result);
    }

    [Fact]
    public void TryFallback_EmptyWhitelist_ReturnsFalse()
    {
        var config = SafeConfig();
        config.Whitelist = new List<WhitelistEntry>();
        var scan = MakeScan("Full command python", "Base python");
        var result = _handler.TryFallback(BlockingWithFullCommand(), scan, config, dryRun: true);
        Assert.False(result);
    }

    // --- Safety Check 5: No execution labels in scan ---

    [Theory]
    [InlineData("Accept")]
    [InlineData("Accept command")]
    [InlineData("Run")]
    [InlineData("Allow")]
    [InlineData("Approve")]
    [InlineData("Continue")]
    [InlineData("Yes")]
    public void TryFallback_ExecutionLabelPresent_ReturnsFalse(string execLabel)
    {
        var scan = MakeScan("Full command python", "Base python", execLabel);
        var result = _handler.TryFallback(BlockingWithFullCommand(), scan, SafeConfig(), dryRun: true);
        Assert.False(result);
    }

    // --- Safety Check 6: Debounce ---

    [Fact]
    public void TryFallback_DebounceActive_ReturnsFalse()
    {
        var scan = MakeScan("Full command python -m pytest", "Base python");
        var hash = DebounceTracker.ComputeHash(FullCommandDescriptor());
        _debounceTracker.Record(hash);
        var result = _handler.TryFallback(BlockingWithFullCommand(), scan, SafeConfig(), dryRun: true);
        Assert.False(result);
    }

    // --- Non-execution labels don't trigger check 5 ---

    [Theory]
    [InlineData("Cancel")]
    [InlineData("Close")]
    [InlineData("Minimize")]
    [InlineData("Settings")]
    [InlineData("Help")]
    public void TryFallback_NonExecutionLabel_DoesNotTriggerCheck5(string label)
    {
        var (detection, scan) = MakeBlockingScanWithFullCommand(label);
        var result = _handler.TryFallback(detection, scan, SafeConfig(), dryRun: true);
        Assert.True(result);
    }

    // --- SUCCESS PATH: DryRun ---

    [Fact]
    public void TryFallback_DryRun_AllChecksPassed_ReturnsTrue()
    {
        var (detection, scan) = MakeBlockingScanWithFullCommand("Base python", "Partial python -m");
        var result = _handler.TryFallback(detection, scan, SafeConfig(), dryRun: true);
        Assert.True(result);
        Assert.Empty(_mockExecutor.ClickedLabels);
    }

    [Fact]
    public void TryFallback_DryRun_RecordsDebounce()
    {
        var (detection, scan) = MakeBlockingScanWithFullCommand("Base python");
        _handler.TryFallback(detection, scan, SafeConfig(), dryRun: true);
        var hash = DebounceTracker.ComputeHash(FullCommandDescriptor());
        Assert.True(_debounceTracker.IsInCooldown(hash, Cooldown));
    }

    [Fact]
    public void TryFallback_DryRun_SecondCallBlocked()
    {
        var (detection, scan) = MakeBlockingScanWithFullCommand("Base python");
        Assert.True(_handler.TryFallback(detection, scan, SafeConfig(), dryRun: true));
        Assert.False(_handler.TryFallback(detection, scan, SafeConfig(), dryRun: true));
    }

    // --- SUCCESS PATH: Real click via mock executor ---

    [Fact]
    public void TryFallback_RealClick_Success_ReturnsTrue()
    {
        var (detection, scan) = MakeBlockingScanWithFullCommand("Base python");
        var result = _handler.TryFallback(detection, scan, SafeConfig(), dryRun: false);
        Assert.True(result);
        Assert.Single(_mockExecutor.ClickedLabels);
        Assert.Equal("Full command python -m pytest", _mockExecutor.ClickedLabels[0]);
    }

    [Fact]
    public void TryFallback_RealClick_Failure_ReturnsFalse()
    {
        _mockExecutor.ShouldSucceed = false;
        var (detection, scan) = MakeBlockingScanWithFullCommand("Base python");
        var result = _handler.TryFallback(detection, scan, SafeConfig(), dryRun: false);
        Assert.False(result);
        Assert.Single(_mockExecutor.ClickedLabels);
    }

    [Fact]
    public void TryFallback_RealClick_Failure_NoDebounceRecorded()
    {
        _mockExecutor.ShouldSucceed = false;
        var (detection, scan) = MakeBlockingScanWithFullCommand("Base python");
        _handler.TryFallback(detection, scan, SafeConfig(), dryRun: false);
        var hash = DebounceTracker.ComputeHash(FullCommandDescriptor());
        Assert.False(_debounceTracker.IsInCooldown(hash, Cooldown));
    }

    // --- Default config safety ---

    [Fact]
    public void DefaultConfiguration_TrustFallbackModeIsOff()
    {
        var config = new Configuration();
        Assert.Equal(TrustFallbackMode.Off, config.TrustFallbackMode);
    }

    // --- Config serialization ---

    [Fact]
    public void ConfigParsing_TrustFallbackMode_Safe()
    {
        var json = """{ "trustFallbackMode": "safe" }""";
        var config = ClickRun.Config.ConfigParser.Parse(json);
        Assert.Equal(TrustFallbackMode.Safe, config.TrustFallbackMode);
    }

    [Fact]
    public void ConfigParsing_TrustFallbackMode_Off()
    {
        var json = """{ "trustFallbackMode": "off" }""";
        var config = ClickRun.Config.ConfigParser.Parse(json);
        Assert.Equal(TrustFallbackMode.Off, config.TrustFallbackMode);
    }

    [Fact]
    public void ConfigParsing_TrustFallbackMode_Missing_DefaultsToOff()
    {
        var json = """{ "scanIntervalMs": 500 }""";
        var config = ClickRun.Config.ConfigParser.Parse(json);
        Assert.Equal(TrustFallbackMode.Off, config.TrustFallbackMode);
    }

    [Fact]
    public void ConfigParsing_TrustFallbackMode_Invalid_Throws()
    {
        var json = """{ "trustFallbackMode": "aggressive" }""";
        Assert.Throws<InvalidOperationException>(() => ClickRun.Config.ConfigParser.Parse(json));
    }
}
