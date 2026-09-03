using System;
using System.Collections.Generic;
using ClickRun.Clicking;
using ClickRun.Config;
using ClickRun.Filtering;
using ClickRun.Models;
using Serilog;
using Xunit;

namespace ClickRun.Tests;

public class VsCodeCompatibilityTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private static WhitelistEntry Entry(string processName, params string[] labels) => new()
    {
        ProcessName = processName,
        WindowTitles = new List<WindowTitlePattern>
        {
            new() { Pattern = "Visual Studio Code", MatchMode = MatchMode.Contains }
        },
        ButtonLabels = new List<string>(labels)
    };

    private static Configuration Config(string processName, params string[] labels) => new()
    {
        BlockedLabels = new List<string>(),
        Whitelist = new List<WhitelistEntry> { Entry(processName, labels) }
    };

    private static ElementDescriptor Element(string processName, string label, string context = "",
        string title = "MyProject - Visual Studio Code") => new(
        processName, title, label, "", true, true, true, context);

    [Fact]
    public void SafetyFilter_VsCode_AcceptsRunLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Code", "Run"), Config("Code", "Run"));

        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_VsCode_AcceptsYesWithSafeContext()
    {
        var filter = new SafetyFilter(Logger);
        var config = Config("Code", "Yes");
        config.ContextRequiredLabels = new List<string> { "Yes" };
        config.SafeContextKeywords = new List<string> { "Permission" };
        var result = filter.Check(Element("Code", "Yes", "Permission required to run command"), config);

        Assert.True(result.Passed);
    }

    [Fact]
    public void SafetyFilter_VsCode_RejectsYesWithoutSafeContext()
    {
        var filter = new SafetyFilter(Logger);
        var config = Config("Code", "Yes");
        config.ContextRequiredLabels = new List<string> { "Yes" };
        config.SafeContextKeywords = new List<string> { "Permission" };
        var result = filter.Check(Element("Code", "Yes", "some unrelated text"), config);

        Assert.False(result.Passed);
        Assert.Equal("missing_safe_context", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_VsCode_RejectsWrongTitle()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Code", "Run", title: "Something Else"), Config("Code", "Run"));

        Assert.False(result.Passed);
        Assert.Equal("title_mismatch", result.RejectionReason);
    }

    [Fact]
    public void SafetyFilter_CodeInsiders_AcceptsRunLabel()
    {
        var filter = new SafetyFilter(Logger);
        var result = filter.Check(Element("Code - Insiders", "Run"), Config("Code - Insiders", "Run"));

        Assert.True(result.Passed);
    }

    [Fact]
    public void DefaultConfig_ContainsCodeEntry()
    {
        Assert.Contains(DefaultConfig.Create().Whitelist,
            entry => entry.ProcessName == "Code");
    }

    [Fact]
    public void DefaultConfig_ContainsCodeInsidersEntry()
    {
        Assert.Contains(DefaultConfig.Create().Whitelist,
            entry => entry.ProcessName == "Code - Insiders");
    }

    [Fact]
    public void KeyboardFallback_RejectsWhenTitleDoesNotMatch()
    {
        var fallback = new KeyboardFallback(Logger);
        var config = Config("Code", "Allow");

        var result = fallback.TryFallback("1. Allow", "Something Else", "Code",
            IntPtr.Zero, config, dryRun: true);

        Assert.False(result);
    }

    [Fact]
    public void KeyboardFallback_AcceptsWhenTitleMatches()
    {
        var fallback = new KeyboardFallback(Logger);
        var config = Config("Code", "Allow");

        var result = fallback.TryFallback("1. Allow", "MyProject - Visual Studio Code", "Code",
            IntPtr.Zero, config, dryRun: true);

        Assert.True(result);
    }

    [Fact]
    public void KeyboardFallback_RejectsYesWithoutSafeContext()
    {
        var fallback = new KeyboardFallback(Logger);
        var config = Config("Code", "Yes");
        config.ContextRequiredLabels = new List<string> { "Yes" };
        config.SafeContextKeywords = new List<string> { "Permission" };

        var result = fallback.TryFallback("1. Yes", "MyProject - Visual Studio Code", "Code",
            IntPtr.Zero, config, dryRun: true);

        Assert.False(result);
    }

    [Fact]
    public void KeyboardFallback_AcceptsYesWithSafeContext()
    {
        var fallback = new KeyboardFallback(Logger);
        var config = Config("Code", "Yes");
        config.ContextRequiredLabels = new List<string> { "Yes" };
        config.SafeContextKeywords = new List<string> { "Permission" };

        var result = fallback.TryFallback("1. Yes\nPermission required", "MyProject - Visual Studio Code", "Code",
            IntPtr.Zero, config, dryRun: true);

        Assert.True(result);
    }
}
