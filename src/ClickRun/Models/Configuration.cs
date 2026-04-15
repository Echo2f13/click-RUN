namespace ClickRun.Models;

/// <summary>
/// Root configuration model for ClickRun.
/// </summary>
public class Configuration
{
    public int ScanIntervalMs { get; set; } = 500;
    public int DebounceCooldownMs { get; set; } = 2000;
    public string KillSwitchHotkey { get; set; } = "Ctrl+Alt+R";
    public string LogLevel { get; set; } = "info";
    public bool EnableWildcardProcess { get; set; } = false;
    public bool EnableDebugInstrumentation { get; set; } = false;
    public bool DryRun { get; set; } = false;
    public int PreClickDelayMs { get; set; } = 0;
    public int FirstSeenDelayMs { get; set; } = 500;
    public List<string> BlockedLabels { get; set; } = new() { "Reject", "Cancel", "Deny" };
    public List<string> ContextRequiredLabels { get; set; } = new() { "Yes" };
    public List<string> SafeContextKeywords { get; set; } = new() { "Allow write", "Allow access", "Permission", "Grant", "Allow edit", "Allow all", "Make this edit", "apply edit", "run command", "execute" };
    public List<string> DangerousContextKeywords { get; set; } = new() { "Delete", "Remove", "Overwrite", "Reset", "Drop", "Erase", "Destroy" };
    public bool MultiWindowMode { get; set; } = false;
    public bool EnableKeyboardFallback { get; set; } = false;
    public List<string> PrefixMatchLabels { get; set; } = new();
    public List<WhitelistEntry> Whitelist { get; set; } = new();
    public TrustFallbackMode TrustFallbackMode { get; set; } = TrustFallbackMode.Off;
}
