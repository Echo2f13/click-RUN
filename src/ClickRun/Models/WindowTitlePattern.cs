namespace ClickRun.Models;

/// <summary>
/// A window title pattern with its match mode.
/// </summary>
public class WindowTitlePattern
{
    public string Pattern { get; set; } = string.Empty;
    public MatchMode MatchMode { get; set; } = MatchMode.Exact;
}
