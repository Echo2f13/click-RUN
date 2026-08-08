using ClickRun.Models;

namespace ClickRun.Clicking;

/// <summary>
/// Abstraction for clicking UI elements. Allows production code to use
/// real UI Automation and tests to use a mock implementation.
/// </summary>
public interface IClickExecutor
{
    /// <summary>
    /// Attempts to click the button described by the given descriptor.
    /// Returns a ClickResult indicating success or failure.
    /// </summary>
    /// <param name="descriptor">The element to click.</param>
    /// <param name="preClickDelayMs">Delay before clicking.</param>
    /// <param name="restoreFocus">Whether to restore focus to the previous window after clicking.</param>
    /// <param name="focusRestoreDelayMs">Delay before restoring focus.</param>
    ClickResult Click(
        ElementDescriptor descriptor,
        int preClickDelayMs = 0,
        bool restoreFocus = true,
        int focusRestoreDelayMs = 50);
}
