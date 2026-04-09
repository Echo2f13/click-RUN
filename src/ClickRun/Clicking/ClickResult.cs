namespace ClickRun.Clicking;

/// <summary>
/// Result of a click attempt on a UI element.
/// </summary>
public sealed record ClickResult(bool Success, string? ErrorMessage = null);
