using System.Windows.Automation;
using ClickRun.Detection;
using ClickRun.Models;
using Serilog;
using Xunit;

namespace ClickRun.Tests;

public class TrustDialogDetectorTests
{
    private readonly TrustDialogDetector _detector;

    public TrustDialogDetectorTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _detector = new TrustDialogDetector(logger);
    }

    private static ElementDescriptor MakeDescriptor(
        string label,
        bool isButton = true,
        bool isVisible = true,
        bool isEnabled = true) =>
        new("Kiro", "Kiro Window", label, "", isButton, isVisible, isEnabled);

    private static ScanResult MakeScan(params string[] labels)
    {
        var buttons = labels.Select(l =>
            (MakeDescriptor(l), (AutomationElement)null!)).ToList();
        return new ScanResult("Kiro", "Kiro Window", IntPtr.Zero, buttons);
    }

    private static ScanResult MakeEmptyScan() =>
        new("Kiro", "Kiro Window", IntPtr.Zero,
            new List<(ElementDescriptor, AutomationElement)>());

    private static List<Candidate> NoCandidates() => new();

    private static List<Candidate> SomeCandidates()
    {
        var desc = MakeDescriptor("Accept command");
        var entry = new WhitelistEntry { ProcessName = "Kiro" };
        return new List<Candidate> { new(desc, entry, "hash123") };
    }

    // --- Detection: Core edge cases ---

    [Fact]
    public void Detect_PassedCandidatesPresent_NotBlocking()
    {
        var scan = MakeScan("Full command python -m pytest", "Base python");
        var result = _detector.Detect(scan, SomeCandidates());

        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_EmptyButtons_NotBlocking()
    {
        var scan = MakeEmptyScan();
        var result = _detector.Detect(scan, NoCandidates());

        Assert.False(result.IsBlockingTrustDialog);
        Assert.Equal(0, result.TotalButtonCount);
    }

    [Fact]
    public void Detect_AllTrustLabels_Blocking()
    {
        // Case A: [Full command, Base, Partial] → blocking
        var scan = MakeScan(
            "Full command python -m pytest",
            "Base python",
            "Partial python -m");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
        Assert.Equal(3, result.TrustLabelCount);
        Assert.Equal(3, result.TotalButtonCount);
    }

    [Fact]
    public void Detect_TrustPlusCancel_Blocking()
    {
        // Case B: [Full command, Base, Cancel] → blocking
        // Cancel is NOT an execution label, trust labels exist, no execution labels
        var scan = MakeScan(
            "Full command python -m pytest",
            "Base python",
            "Cancel");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
        Assert.Equal(2, result.TrustLabelCount); // Cancel is not a trust label
        Assert.Equal(3, result.TotalButtonCount);
    }

    [Fact]
    public void Detect_TrustPlusAcceptCommand_NotBlocking()
    {
        // Case C: [Full command, Accept command] → NOT blocking
        // "Accept command" IS an execution label
        var scan = MakeScan(
            "Full command python -m pytest",
            "Accept command");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_CancelOnly_NotBlocking()
    {
        // Case D: [Cancel only] → NOT blocking (no trust labels)
        var scan = MakeScan("Cancel");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_TrustPlusRun_NotBlocking()
    {
        // "Run" is an execution label
        var scan = MakeScan("Full command python", "Run");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_TrustPlusAllow_NotBlocking()
    {
        var scan = MakeScan("Base python", "Allow");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.False(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_TrustPlusClose_Blocking()
    {
        // "Close" is not an execution label, trust labels present → blocking
        var scan = MakeScan("Full command python", "Partial python -m", "Close");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
    }

    [Fact]
    public void Detect_OnlyNonTrustNonExecution_NotBlocking()
    {
        // [Close, Minimize] → no trust labels → not blocking
        var scan = MakeScan("Close", "Minimize");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.False(result.IsBlockingTrustDialog);
    }

    // --- FullCommandElement extraction ---

    [Fact]
    public void Detect_FullCommandPresent_DescriptorPopulated()
    {
        var scan = MakeScan("Full command python -m pytest", "Base python", "Partial python -m");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.NotNull(result.FullCommandDescriptor);
        Assert.Equal("Full command python -m pytest", result.FullCommandDescriptor!.ButtonLabel);
    }

    [Fact]
    public void Detect_NoFullCommand_DescriptorNull()
    {
        // [Base, Partial] only → blocking but no Full command element
        var scan = MakeScan("Base python", "Partial python -m");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
        Assert.Null(result.FullCommandDescriptor);
    }

    [Fact]
    public void Detect_FullCommandNotVisible_DescriptorNull()
    {
        var buttons = new List<(ElementDescriptor, AutomationElement)>
        {
            (MakeDescriptor("Full command python", isVisible: false), null!),
            (MakeDescriptor("Base python"), null!)
        };
        var scan = new ScanResult("Kiro", "Kiro Window", IntPtr.Zero, buttons);
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
        Assert.Null(result.FullCommandDescriptor); // Not visible → not tracked
    }

    [Fact]
    public void Detect_FullCommandNotEnabled_DescriptorNull()
    {
        var buttons = new List<(ElementDescriptor, AutomationElement)>
        {
            (MakeDescriptor("Full command python", isEnabled: false), null!),
            (MakeDescriptor("Base python"), null!)
        };
        var scan = new ScanResult("Kiro", "Kiro Window", IntPtr.Zero, buttons);
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
        Assert.Null(result.FullCommandDescriptor); // Not enabled → not tracked
    }

    // --- TrustLabelCount accuracy ---

    [Fact]
    public void Detect_TrustLabelCount_OnlyCountsTrustLabels()
    {
        var scan = MakeScan("Full command python", "Base python", "Cancel", "Close");
        var result = _detector.Detect(scan, NoCandidates());

        Assert.True(result.IsBlockingTrustDialog);
        Assert.Equal(2, result.TrustLabelCount); // Full command + Base
        Assert.Equal(4, result.TotalButtonCount);
    }
}

public class TrustLabelClassificationTests
{
    // --- IsTrustLabel ---

    [Theory]
    [InlineData("Full command python -m pytest", true)]
    [InlineData("Full command", true)]
    [InlineData("full command something", true)]
    [InlineData("FULL COMMAND test", true)]
    [InlineData("Base python", true)]
    [InlineData("base", true)]
    [InlineData("BASE something", true)]
    [InlineData("Partial python -m", true)]
    [InlineData("partial", true)]
    [InlineData("PARTIAL test", true)]
    [InlineData("Trust command and accept", true)]
    [InlineData("trust command and accept", true)]
    [InlineData("TRUST COMMAND AND ACCEPT", true)]
    // Non-trust labels
    [InlineData("Accept", false)]
    [InlineData("Accept command", false)]
    [InlineData("Run", false)]
    [InlineData("Allow", false)]
    [InlineData("Cancel", false)]
    [InlineData("Close", false)]
    [InlineData("Settings", false)]
    [InlineData("", false)]
    public void IsTrustLabel_ClassifiesCorrectly(string label, bool expected)
    {
        Assert.Equal(expected, TrustDialogDetector.IsTrustLabel(label));
    }

    [Fact]
    public void IsTrustLabel_NullInput_ReturnsFalse()
    {
        Assert.False(TrustDialogDetector.IsTrustLabel(null!));
    }

    // --- IsExecutionLabel ---

    [Theory]
    [InlineData("Accept", true)]
    [InlineData("accept", true)]
    [InlineData("ACCEPT", true)]
    [InlineData("Accept command", true)]
    [InlineData("accept command", true)]
    [InlineData("Run", true)]
    [InlineData("run", true)]
    [InlineData("Allow", true)]
    [InlineData("allow", true)]
    [InlineData("Approve", true)]
    [InlineData("approve", true)]
    [InlineData("Continue", true)]
    [InlineData("continue", true)]
    [InlineData("Yes", true)]
    [InlineData("yes", true)]
    // Non-execution labels
    [InlineData("Full command python", false)]
    [InlineData("Base python", false)]
    [InlineData("Partial python", false)]
    [InlineData("Cancel", false)]
    [InlineData("Close", false)]
    [InlineData("Reject", false)]
    [InlineData("Accept all", false)] // Not exact match
    [InlineData("Run anyway", false)] // Not exact match
    [InlineData("", false)]
    public void IsExecutionLabel_ClassifiesCorrectly(string label, bool expected)
    {
        Assert.Equal(expected, TrustDialogDetector.IsExecutionLabel(label));
    }

    [Fact]
    public void IsExecutionLabel_NullInput_ReturnsFalse()
    {
        Assert.False(TrustDialogDetector.IsExecutionLabel(null!));
    }
}
