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
    public List<string> BlockedLabels { get; set; } = new() { "Reject", "Cancel", "Deny" };
    public bool MultiWindowMode { get; set; } = false;
    public List<WhitelistEntry> Whitelist { get; set; } = new();
}
