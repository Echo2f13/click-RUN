using Serilog;
using Serilog.Events;

namespace ClickRun.Logging;

/// <summary>
/// Configures Serilog with file sink, rolling file rotation, and log level mapping.
/// </summary>
public static class LoggerSetup
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clickrun");

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "clickrun.log");

    /// <summary>
    /// Output template: [ISO8601_TIMESTAMP] [LEVEL] [COMPONENT] message
    /// </summary>
    private const string OutputTemplate =
        "[{Timestamp:o}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Creates a configured Serilog ILogger with file sink and rolling file rotation.
    /// </summary>
    /// <param name="logLevel">Log level string from config: "debug", "info", "warn", or "error".</param>
    /// <returns>A configured Serilog ILogger instance.</returns>
    public static ILogger CreateLogger(string logLevel)
    {
        var level = ParseLogLevel(logLevel);

        return new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                path: LogFilePath,
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                retainedFileCountLimit: 3,
                rollOnFileSizeLimit: true)
            .CreateLogger();
    }

    /// <summary>
    /// Maps a config log level string to a Serilog LogEventLevel.
    /// Defaults to Information for unrecognized values.
    /// </summary>
    internal static LogEventLevel ParseLogLevel(string logLevel)
    {
        return (logLevel?.ToLowerInvariant()) switch
        {
            "debug" => LogEventLevel.Debug,
            "info" => LogEventLevel.Information,
            "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            _ => LogEventLevel.Information,
        };
    }

    // Note: Throttled debug logging (per-cycle summary of elements scanned, passed, rejected)
    // is implemented in the main scan loop (Task 10), not here. The logger setup only configures
    // the Serilog pipeline; the caller is responsible for emitting summary lines instead of
    // per-element debug entries.
}
