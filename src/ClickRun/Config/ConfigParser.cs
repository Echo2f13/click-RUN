using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Config;

/// <summary>
/// Loads and validates configuration from ~/.clickrun/config.json.
/// </summary>
public static class ConfigParser
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>
    /// Parses a JSON string into a Configuration object, validates regex patterns,
    /// and clamps ScanIntervalMs to [300, 800].
    /// </summary>
    public static Configuration Parse(string json, ILogger? logger = null)
    {
        Configuration config;
        try
        {
            config = JsonSerializer.Deserialize<Configuration>(json, Options)
                     ?? throw new InvalidOperationException("Deserialized configuration is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid JSON in configuration file (line {ex.LineNumber}, byte {ex.BytePositionInLine}): {ex.Message}", ex);
        }

        ValidateRegexPatterns(config);
        ClampScanInterval(config, logger);
        ClampPreClickDelay(config, logger);

        return config;
    }

    /// <summary>
    /// Loads configuration from the given file path. If the file does not exist, returns null.
    /// </summary>
    public static Configuration? LoadFromFile(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return Parse(json, logger);
    }

    private static void ValidateRegexPatterns(Configuration config)
    {
        foreach (var entry in config.Whitelist)
        {
            foreach (var titlePattern in entry.WindowTitles)
            {
                if (titlePattern.MatchMode == MatchMode.Regex)
                {
                    try
                    {
                        _ = new Regex(titlePattern.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException(
                            $"Invalid regex pattern '{titlePattern.Pattern}' in whitelist entry for process '{entry.ProcessName}': {ex.Message}", ex);
                    }
                }
            }
        }
    }

    private static void ClampScanInterval(Configuration config, ILogger? logger)
    {
        var original = config.ScanIntervalMs;
        config.ScanIntervalMs = Math.Clamp(config.ScanIntervalMs, 300, 800);

        if (original != config.ScanIntervalMs)
        {
            logger?.Warning(
                "ScanIntervalMs value {Original} is outside valid range [300, 800]. Clamped to {Clamped}.",
                original, config.ScanIntervalMs);
        }
    }

    private static void ClampPreClickDelay(Configuration config, ILogger? logger)
    {
        var original = config.PreClickDelayMs;
        config.PreClickDelayMs = Math.Clamp(config.PreClickDelayMs, 0, 200);

        if (original != config.PreClickDelayMs)
        {
            logger?.Warning(
                "PreClickDelayMs value {Original} is outside valid range [0, 200]. Clamped to {Clamped}.",
                original, config.PreClickDelayMs);
        }
    }

    /// <summary>
    /// Returns the shared JsonSerializerOptions used for config parsing/serialization.
    /// </summary>
    public static JsonSerializerOptions GetSerializerOptions() => Options;
}
