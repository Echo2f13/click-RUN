using ClickRun.Filtering;
using ClickRun.Models;
using Serilog;
using Xunit;

namespace ClickRun.Tests;

public class SafetyFilterTests
{
    private readonly SafetyFilter _filter;
    private readonly Configuration _config;

    public SafetyFilterTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _filter = new SafetyFilter(logger);

        _config = new Configuration
        {
            EnableWildcardProcess = false,
            Whitelist = new List<WhitelistEntry>
            {
                new()
                {
                    ProcessName = "Code",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Visual Studio Code", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string> { "Run", "Allow", "Continue" }
                }
            }
        };
    }

    private static ElementDescriptor ValidElement() => new(
        ProcessName: "Code",
        WindowTitle: "MyProject - Visual Studio Code",
        ButtonLabel: "Run",
        AutomationId: "btn1",
        IsButton: true,
        IsVisible: true,
        IsEnabled: true);

    [Fact]
    public void Check_ValidElement_Passes()
    {
        var result = _filter.Check(ValidElement(), _config);

        Assert.True(result.Passed);
        Assert.NotNull(result.MatchedEntry);
        Assert.Equal("Code", result.MatchedEntry!.ProcessName);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void Check_NotButton_Rejected()
    {
        var element = ValidElement() with { IsButton = false };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("not_button", result.RejectionReason);
    }

    [Fact]
    public void Check_NotVisible_Rejected()
    {
        var element = ValidElement() with { IsVisible = false };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("not_visible", result.RejectionReason);
    }

    [Fact]
    public void Check_NotEnabled_Rejected()
    {
        var element = ValidElement() with { IsEnabled = false };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("not_enabled", result.RejectionReason);
    }

    [Fact]
    public void Check_WrongProcessName_Rejected()
    {
        var element = ValidElement() with { ProcessName = "Notepad" };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("process_mismatch", result.RejectionReason);
    }

    [Fact]
    public void Check_WrongWindowTitle_Rejected()
    {
        var element = ValidElement() with { WindowTitle = "Notepad" };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("title_mismatch", result.RejectionReason);
    }

    [Fact]
    public void Check_WrongButtonLabel_Rejected()
    {
        var element = ValidElement() with { ButtonLabel = "Settings" };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("label_mismatch", result.RejectionReason);
    }

    [Fact]
    public void Check_BlockedLabel_Rejected()
    {
        var element = ValidElement() with { ButtonLabel = "Cancel" };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    [Fact]
    public void Check_BlockedLabel_ContainsMatch_Rejected()
    {
        var element = ValidElement() with { ButtonLabel = "Reject changes" };
        var result = _filter.Check(element, _config);

        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    [Fact]
    public void Check_ButtonLabelMatchIsCaseInsensitive()
    {
        var element = ValidElement() with { ButtonLabel = "rUn" };
        var result = _filter.Check(element, _config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Check_WildcardProcess_RejectedWhenDisabled()
    {
        var config = new Configuration
        {
            EnableWildcardProcess = false,
            Whitelist = new List<WhitelistEntry>
            {
                new()
                {
                    ProcessName = "*",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Any", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string> { "Run" }
                }
            }
        };

        var element = new ElementDescriptor("SomeApp", "Any Window", "Run", "btn", true, true, true);
        var result = _filter.Check(element, config);

        Assert.False(result.Passed);
        // Wildcard entry is skipped when disabled, no other entries match → process_mismatch
        Assert.Equal("process_mismatch", result.RejectionReason);
    }

    [Fact]
    public void Check_WildcardProcess_AcceptedWhenEnabled()
    {
        var config = new Configuration
        {
            EnableWildcardProcess = true,
            Whitelist = new List<WhitelistEntry>
            {
                new()
                {
                    ProcessName = "*",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Any", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string> { "Run" }
                }
            }
        };

        var element = new ElementDescriptor("SomeApp", "Any Window", "Run", "btn", true, true, true);
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
        Assert.Equal("*", result.MatchedEntry!.ProcessName);
    }

    [Fact]
    public void Check_WildcardDisabled_FallsThroughToNonWildcardEntry()
    {
        var config = new Configuration
        {
            EnableWildcardProcess = false,
            Whitelist = new List<WhitelistEntry>
            {
                new()
                {
                    ProcessName = "*",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "VS Code", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string> { "Run" }
                },
                new()
                {
                    ProcessName = "Code",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "VS Code", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string> { "Run" }
                }
            }
        };

        var element = new ElementDescriptor("Code", "VS Code Window", "Run", "btn", true, true, true);
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
        Assert.Equal("Code", result.MatchedEntry!.ProcessName);
    }

    [Fact]
    public void Check_ProcessNameMatchIsCaseInsensitive()
    {
        var element = ValidElement() with { ProcessName = "code" };
        var result = _filter.Check(element, _config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Check_ExactTitleMode_RequiresFullMatch()
    {
        var config = new Configuration
        {
            Whitelist = new List<WhitelistEntry>
            {
                new()
                {
                    ProcessName = "Code",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Visual Studio Code", MatchMode = MatchMode.Exact }
                    },
                    ButtonLabels = new List<string> { "Run" }
                }
            }
        };

        // Partial title should fail with exact mode
        var element = ValidElement() with { WindowTitle = "MyProject - Visual Studio Code" };
        var result = _filter.Check(element, config);

        Assert.False(result.Passed);
    }
}
