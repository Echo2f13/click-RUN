using ClickRun.Logging;
using Serilog.Events;
using Xunit;

namespace ClickRun.Tests;

public class LoggerSetupTests
{
    [Theory]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("info", LogEventLevel.Information)]
    [InlineData("warn", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    public void ParseLogLevel_MapsKnownLevels(string input, LogEventLevel expected)
    {
        var result = LoggerSetup.ParseLogLevel(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("DEBUG")]
    [InlineData("Info")]
    [InlineData("WARN")]
    [InlineData("Error")]
    public void ParseLogLevel_IsCaseInsensitive(string input)
    {
        // Should not throw and should map to a valid level
        var result = LoggerSetup.ParseLogLevel(input);
        Assert.True(Enum.IsDefined(typeof(LogEventLevel), result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("verbose")]
    [InlineData("trace")]
    [InlineData("unknown")]
    public void ParseLogLevel_DefaultsToInformation_ForUnknownValues(string input)
    {
        var result = LoggerSetup.ParseLogLevel(input);
        Assert.Equal(LogEventLevel.Information, result);
    }

    [Fact]
    public void ParseLogLevel_DefaultsToInformation_ForNull()
    {
        var result = LoggerSetup.ParseLogLevel(null!);
        Assert.Equal(LogEventLevel.Information, result);
    }

    [Fact]
    public void CreateLogger_ReturnsNonNullLogger()
    {
        var logger = LoggerSetup.CreateLogger("info");
        Assert.NotNull(logger);
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("error")]
    public void CreateLogger_AcceptsAllValidLevels(string level)
    {
        var logger = LoggerSetup.CreateLogger(level);
        Assert.NotNull(logger);
    }
}
