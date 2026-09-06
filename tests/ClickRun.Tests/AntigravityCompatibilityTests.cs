using System;
using System.Collections.Generic;
using ClickRun.Clicking;
using ClickRun.Config;
using ClickRun.Filtering;
using ClickRun.Models;
using Serilog;
using Xunit;

namespace ClickRun.Tests;

public class AntigravityCompatibilityTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private static WhitelistEntry Entry(string processName, string titlePattern, params string[] labels) => new()
    {
        ProcessName = processName,
        WindowTitles = new List<WindowTitlePattern>
        {
            new() { Pattern = titlePattern, MatchMode = MatchMode.Contains }
        },
        ButtonLabels = new List<string>(labels)
    };

    private static Configuration Config(string processName, string titlePattern, params string[] labels)
    {
        var config = DefaultConfig.Create();
        config.Whitelist = new List<WhitelistEntry> { Entry(processName, titlePattern, labels) };
        return config;
    }

    private static ElementDescriptor Element(string processName, string label, string context = "",
        string title = "src - Antigravity IDE") => new(
        processName, title, label, "", true, true, true, context);

    // --- SafetyFilter tests — Antigravity IDE process ---

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsRunLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsAllowLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Allow"), Config("Antigravity IDE", "Antigravity IDE", "Allow"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsProceedLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Proceed"), Config("Antigravity IDE", "Antigravity IDE", "Proceed"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsSubmitLabelWithSafeContext()
    {
        var filter = new SafetyFilter(Logger);
        var config = Config("Antigravity IDE", "Antigravity IDE", "Submit");
        config.ContextRequiredLabels = new List<string> { "Yes", "Submit" };
        var result = filter.Check(Element("Antigravity IDE", "Submit", "Allow running test command?"), config);
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsWrongTitle()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run", title: "Something Else"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("title_mismatch", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsYesWithoutSafeContext()
    {
        var filter = new SafetyFilter(Logger);
        var config = Config("Antigravity IDE", "Antigravity IDE", "Yes");
        config.ContextRequiredLabels = new List<string> { "Yes" };
        var result = filter.Check(Element("Antigravity IDE", "Yes", "some unrelated text"), config);
        Assert.False(result.Passed);
        Assert.Equal("missing_safe_context", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsYesWithSafeContext()
    {
        var filter = new SafetyFilter(Logger);
        var config = Config("Antigravity IDE", "Antigravity IDE", "Yes");
        config.ContextRequiredLabels = new List<string> { "Yes" };
        var result = filter.Check(Element("Antigravity IDE", "Yes", "tool execution requested"), config);
        Assert.True(result.Passed);
    }

    // --- SafetyFilter tests — Antigravity 2.0 Agent Manager process ---

    [Fact]
    public void SafetyFilter_Antigravity_AcceptsRunLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity", "Run", title: "Antigravity 2.0"), Config("Antigravity", "Antigravity", "Run"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_Antigravity_AcceptsProceedLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity", "Proceed", title: "Antigravity 2.0"), Config("Antigravity", "Antigravity", "Proceed"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_Antigravity_RejectsWrongTitle()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity", "Run", title: "Something Else"), Config("Antigravity", "Antigravity", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("title_mismatch", result.RejectionReason);
    }

    // --- False positive guard tests (blocklist) ---

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsRunAndDebugLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run and Debug"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsRunTaskLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run Task"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsRunWithoutDebuggingLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run Without Debugging"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsAcceptAllChangesLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Accept All Changes"), Config("Antigravity IDE", "Antigravity IDE", "Accept"));
        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsDiscardLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Discard"), Config("Antigravity IDE", "Antigravity IDE", "Discard"));
        Assert.False(result.Passed);
        Assert.Equal("blocked_label", result.RejectionReason);
    }

    // --- Dynamic suffix tests (prefixMatchLabels) ---

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsRunWithShortcutSuffix()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run (Ctrl+Enter)"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_AcceptsAllowWithShortcutSuffix()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Allow (Ctrl+Enter)"), Config("Antigravity IDE", "Antigravity IDE", "Allow"));
        Assert.True(result.Passed);
    }

    // --- Dangerous context tests ---

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsRunWhenContextContainsRmRf()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run", "execute rm -rf /"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("dangerous_context", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsRunWhenContextContainsDelCommand()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run", "execute del /f /s /q temp"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("dangerous_context", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_AntigravityIDE_RejectsRunWhenContextContainsFormatCommand()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Antigravity IDE", "Run", "execute format c: /q"), Config("Antigravity IDE", "Antigravity IDE", "Run"));
        Assert.False(result.Passed);
        Assert.Equal("dangerous_context", result.RejectionReason);
    }

    // --- Keyboard fallback tests ---

    [Fact]
    public void KeyboardFallback_AntigravityIDE_AcceptsWhenTitleMatches()
    {
        var fallback = new KeyboardFallback(Logger);
        var config = Config("Antigravity IDE", "Antigravity IDE", "Allow");
        var result = fallback.TryFallback("1. Allow", "src - Antigravity IDE", "Antigravity IDE", IntPtr.Zero, config, dryRun: true);
        Assert.True(result);
    }

    [Fact]
    public void KeyboardFallback_AntigravityIDE_RejectsWhenTitleDoesNotMatch()
    {
        var fallback = new KeyboardFallback(Logger);
        var config = Config("Antigravity IDE", "Antigravity IDE", "Allow");
        var result = fallback.TryFallback("1. Allow", "Something Else", "Antigravity IDE", IntPtr.Zero, config, dryRun: true);
        Assert.False(result);
    }

    // --- DefaultConfig presence tests ---

    [Fact]
    public void DefaultConfig_ContainsAntigravityIDEEntry()
    {
        Assert.Contains(DefaultConfig.Create().Whitelist, entry => entry.ProcessName == "Antigravity IDE");
    }

    [Fact]
    public void DefaultConfig_ContainsAntigravityEntry()
    {
        Assert.Contains(DefaultConfig.Create().Whitelist, entry => entry.ProcessName == "Antigravity");
    }

    [Fact]
    public void DefaultConfig_ContainsAntigravityIDEInsidersEntry()
    {
        Assert.Contains(DefaultConfig.Create().Whitelist, entry => entry.ProcessName == "Antigravity IDE - Insiders");
    }

    [Fact]
    public void DefaultConfig_BlockedLabels_ContainsRunAndDebug()
    {
        Assert.Contains(DefaultConfig.Create().BlockedLabels, label => label == "Run and Debug");
    }

    [Fact]
    public void DefaultConfig_BlockedLabels_ContainsDiscard()
    {
        Assert.Contains(DefaultConfig.Create().BlockedLabels, label => label == "Discard");
    }
}
