namespace ClickRun.Models;

/// <summary>
/// A whitelist entry specifying a target application, its window title patterns, and allowed button labels.
/// </summary>
public class WhitelistEntry
{
    public string ProcessName { get; set; } = string.Empty;
    public List<WindowTitlePattern> WindowTitles { get; set; } = new();
    public List<string> ButtonLabels { get; set; } = new();
}
