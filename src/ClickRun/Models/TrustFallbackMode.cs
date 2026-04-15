namespace ClickRun.Models;

/// <summary>
/// Controls trust dialog fallback behavior.
/// </summary>
public enum TrustFallbackMode
{
    /// <summary>Default. Trust dialogs are ignored entirely.</summary>
    Off,
    /// <summary>Allow clicking only "Full command ..." to unblock.</summary>
    Safe
}
