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
    ClickResult Click(ElementDescriptor descriptor, int preClickDelayMs = 0);
}
