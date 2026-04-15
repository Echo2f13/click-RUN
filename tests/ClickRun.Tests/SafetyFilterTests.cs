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

public class ContextBasedYesFilterTests
{
    private readonly SafetyFilter _filter;

    public ContextBasedYesFilterTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _filter = new SafetyFilter(logger);
    }

    private static Configuration MakeConfig() => new()
    {
        ContextRequiredLabels = new List<string> { "Yes" },
        SafeContextKeywords = new List<string> { "Allow write", "Allow access", "Permission", "Grant" },
        DangerousContextKeywords = new List<string> { "Delete", "Remove", "Overwrite", "Reset", "Drop" },
        Whitelist = new List<WhitelistEntry>
        {
            new()
            {
                ProcessName = "Code",
                WindowTitles = new List<WindowTitlePattern>
                {
                    new() { Pattern = "Visual Studio Code", MatchMode = MatchMode.Contains }
                },
                ButtonLabels = new List<string> { "Run", "Allow", "Yes", "Yes, allow all edits this session" }
            }
        }
    };

    private static ElementDescriptor MakeElement(string label, string windowTitle) => new(
        ProcessName: "Code",
        WindowTitle: windowTitle,
        ButtonLabel: label,
        AutomationId: "btn",
        IsButton: true,
        IsVisible: true,
        IsEnabled: true);

    [Fact]
    public void Yes_WithSafeContext_Passes()
    {
        var config = MakeConfig();
        var element = MakeElement("Yes", "Allow write to file.ts - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Yes_WithGrantContext_Passes()
    {
        var config = MakeConfig();
        var element = MakeElement("Yes", "Grant Permission - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Yes_WithDangerousContext_Rejected()
    {
        var config = MakeConfig();
        var element = MakeElement("Yes", "Delete file? - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.False(result.Passed);
        Assert.Equal("dangerous_context", result.RejectionReason);
    }

    [Fact]
    public void Yes_WithRemoveContext_Rejected()
    {
        var config = MakeConfig();
        var element = MakeElement("Yes", "Remove all changes? - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.False(result.Passed);
        Assert.Equal("dangerous_context", result.RejectionReason);
    }

    [Fact]
    public void Yes_WithNoSafeContext_Rejected()
    {
        var config = MakeConfig();
        var element = MakeElement("Yes", "Some random dialog - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.False(result.Passed);
        Assert.Equal("missing_safe_context", result.RejectionReason);
    }

    [Fact]
    public void YesAllowAllEdits_WithSafeContext_Passes()
    {
        var config = MakeConfig();
        var element = MakeElement("Yes, allow all edits this session", "Allow access - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Run_DoesNotRequireContext()
    {
        var config = MakeConfig();
        var element = MakeElement("Run", "Whatever title - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Allow_DoesNotRequireContext()
    {
        var config = MakeConfig();
        var element = MakeElement("Allow", "Whatever title - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void DangerousContext_TakesPriorityOverSafe()
    {
        var config = MakeConfig();
        // Window title contains both "Allow write" (safe) and "Delete" (dangerous)
        var element = MakeElement("Yes", "Allow write and Delete backup - Visual Studio Code");
        var result = _filter.Check(element, config);

        Assert.False(result.Passed);
        Assert.Equal("dangerous_context", result.RejectionReason);
    }
}

public class LabelNormalizationTests
{
    [Theory]
    [InlineData("Run", "Run")]
    [InlineData("  Run  ", "Run")]
    [InlineData("Run\t", "Run")]
    public void NormalizeLabel_TrimsWhitespace(string input, string expected)
    {
        Assert.Equal(expected, SafetyFilter.NormalizeLabel(input));
    }

    [Fact]
    public void NormalizeLabel_RemovesNonAscii()
    {
        var result = SafetyFilter.NormalizeLabel("Follow \u00EE\u00A9\u00B0");
        Assert.Equal("Follow", result);
    }

    [Fact]
    public void NormalizeLabel_CollapsesSpaces()
    {
        Assert.Equal("Trust command", SafetyFilter.NormalizeLabel("Trust  command"));
    }

    [Fact]
    public void NormalizeLabel_EmptyString()
    {
        Assert.Equal(string.Empty, SafetyFilter.NormalizeLabel(""));
        Assert.Equal(string.Empty, SafetyFilter.NormalizeLabel(null!));
    }

    [Fact]
    public void NormalizeLabel_PreservesAscii()
    {
        Assert.Equal("Full command python -m src.test", SafetyFilter.NormalizeLabel("Full command python -m src.test"));
    }
}

public class WordBoundaryMatchingTests
{
    private readonly SafetyFilter _filter;

    public WordBoundaryMatchingTests()
    {
        _filter = new SafetyFilter(new LoggerConfiguration().CreateLogger());
    }

    private static Configuration MakeConfig(params string[] labels) => new()
    {
        PrefixMatchLabels = new List<string> { "Full command" },
        Whitelist = new List<WhitelistEntry>
        {
            new()
            {
                ProcessName = "Kiro",
                WindowTitles = new List<WindowTitlePattern>
                {
                    new() { Pattern = "Kiro", MatchMode = MatchMode.Contains }
                },
                ButtonLabels = labels.ToList()
            }
        }
    };

    private static ElementDescriptor El(string label) => new(
        "Kiro", "test - Kiro", label, "", true, true, true);

    // --- SHOULD MATCH ---

    [Fact]
    public void ExactMatch_Run()
    {
        var result = _filter.Check(El("Run"), MakeConfig("Run", "Trust", "Accept"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void ExactMatch_Trust()
    {
        var result = _filter.Check(El("Trust"), MakeConfig("Run", "Trust", "Accept"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void ExactMatch_TrustCommandAndAccept()
    {
        var result = _filter.Check(El("Trust command and accept"), MakeConfig("Run", "Trust", "Accept", "Trust command and accept"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void ExactMatch_AcceptCommand()
    {
        var result = _filter.Check(El("Accept command"), MakeConfig("Run", "Trust", "Accept", "Accept command"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void PrefixMatch_FullCommandDynamic()
    {
        var result = _filter.Check(El("Full command python -m src.test"), MakeConfig("Run"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void ExactMatch_WithUnicodeNormalized()
    {
        var result = _filter.Check(El("Run\u00EE"), MakeConfig("Run"));
        Assert.True(result.Passed);
    }

    // --- SHOULD NOT MATCH ---

    [Fact]
    public void NoMatch_RunCodeExtension()
    {
        // CRITICAL: "Run Code (Ctrl+Alt+N)" must NOT match "Run"
        var result = _filter.Check(El("Run Code (Ctrl+Alt+N)"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
        Assert.Equal("label_mismatch", result.RejectionReason);
    }

    [Fact]
    public void NoMatch_DoNotAcceptThis()
    {
        var result = _filter.Check(El("Do not accept this"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
    }

    [Fact]
    public void NoMatch_Trustworthy()
    {
        var result = _filter.Check(El("Trustworthy"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
    }

    [Fact]
    public void NoMatch_Running()
    {
        var result = _filter.Check(El("Running"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
    }

    [Fact]
    public void NoMatch_Acceptable()
    {
        var result = _filter.Check(El("Acceptable"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
    }

    [Fact]
    public void NoMatch_RandomLabel()
    {
        var result = _filter.Check(El("Settings"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
    }

    [Fact]
    public void NoMatch_TrustCommandWithoutExactEntry()
    {
        // "Trust command and accept" should NOT match if only "Trust" is in whitelist (no word-boundary fallback)
        var result = _filter.Check(El("Trust command and accept"), MakeConfig("Run", "Trust", "Accept"));
        Assert.False(result.Passed);
    }
}
