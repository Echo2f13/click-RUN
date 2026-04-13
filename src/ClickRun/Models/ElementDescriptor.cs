namespace ClickRun.Models;

/// <summary>
/// Describes a UI element candidate for safety filtering.
/// </summary>
public sealed record ElementDescriptor(
    string ProcessName,
    string WindowTitle,
    string ButtonLabel,
    string AutomationId,
    bool IsButton,
    bool IsVisible,
    bool IsEnabled,
    string ContextText = "");
