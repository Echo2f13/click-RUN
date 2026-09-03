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
        "Accept", "Accept command"
    };

    private static readonly List<string> DefaultBlockedLabels = new()
    {
        "Reject", "Cancel", "Deny", "Proceed without executing"
    };

    // Labels that may appear with suffixes like "(Ctrl+Enter)" in VS Code.
    // PrefixMatchLabels enables "Allow (Ctrl+Enter)" to match "Allow".
    private static readonly List<string> DefaultPrefixMatchLabels = new()
    {
        "Allow", "Run", "Accept", "Approve", "Continue", "Yes", "Trust"
    };

    private static readonly List<string> DefaultSafeContextKeywords = new()
    {
        "Allow write", "Allow access", "Permission", "Grant", "Allow edit",
        "Allow all", "Make this edit", "apply edit", "run command", "execute",
        // VS Code Copilot agent confirmation markers
        "Chat Confirmation Dialog", "confirmation pending"
    };

    // "Delete" and "Remove" removed: VS Code Copilot wraps normal confirmations
    // in text describing what will happen (e.g. "Deletes the file"), which would
    // cause every shell command confirmation to be rejected as dangerous.
    private static readonly List<string> DefaultDangerousContextKeywords = new()
    {
        "Overwrite", "Reset", "Drop", "Erase", "Destroy"
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
            PrefixMatchLabels = new List<string>(DefaultPrefixMatchLabels),
            SafeContextKeywords = new List<string>(DefaultSafeContextKeywords),
            DangerousContextKeywords = new List<string>(DefaultDangerousContextKeywords),
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
                    ProcessName = "Code - Insiders",
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
