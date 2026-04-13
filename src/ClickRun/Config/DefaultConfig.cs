using ClickRun.Models;

namespace ClickRun.Config;

/// <summary>
/// Provides the default configuration when no config file exists.
/// </summary>
public static class DefaultConfig
{
    private static readonly List<string> DefaultButtonLabels = new()
    {
        "Run", "Allow", "Approve", "Continue", "Yes", "Yes, allow all edits this session",
        "Accept", "Accept command", "Trust", "Trust command and accept"
    };

    private static readonly List<string> DefaultBlockedLabels = new()
    {
        "Reject", "Cancel", "Deny"
    };

    /// <summary>
    /// Creates the default Configuration with Kiro, Code, and Claude entries using "contains" match mode.
    /// </summary>
    public static Configuration Create()
    {
        return new Configuration
        {
            ScanIntervalMs = 500,
            DebounceCooldownMs = 2000,
            KillSwitchHotkey = "Ctrl+Alt+R",
            LogLevel = "info",
            EnableWildcardProcess = false,
            EnableDebugInstrumentation = false,
            DryRun = false,
            PreClickDelayMs = 0,
            BlockedLabels = new List<string>(DefaultBlockedLabels),
            MultiWindowMode = false,
            Whitelist = new List<WhitelistEntry>
            {
                new()
                {
                    ProcessName = "Kiro",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Kiro", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string>(DefaultButtonLabels)
                },
                new()
                {
                    ProcessName = "Code",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Visual Studio Code", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string>(DefaultButtonLabels)
                },
                new()
                {
                    ProcessName = "Claude",
                    WindowTitles = new List<WindowTitlePattern>
                    {
                        new() { Pattern = "Claude", MatchMode = MatchMode.Contains }
                    },
                    ButtonLabels = new List<string>(DefaultButtonLabels)
                }
            }
        };
    }

    /// <summary>
    /// Returns the default config file path: ~/.clickrun/config.json
    /// </summary>
    public static string GetDefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".clickrun", "config.json");
    }

    /// <summary>
    /// Loads config from the default path, creating the default file if it doesn't exist.
    /// </summary>
    public static Configuration LoadOrCreateDefault(Serilog.ILogger? logger = null)
    {
        var configPath = GetDefaultConfigPath();

        var config = ConfigParser.LoadFromFile(configPath, logger);
        if (config != null)
            return config;

        logger?.Information("Configuration file not found at {Path}. Creating default configuration.", configPath);
        var defaultConfig = Create();
        ConfigSerializer.SaveToFile(defaultConfig, configPath);
        return defaultConfig;
    }
}
