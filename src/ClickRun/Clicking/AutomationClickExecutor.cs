using System.Windows.Automation;
using ClickRun.Models;

namespace ClickRun.Clicking;

/// <summary>
/// Production IClickExecutor that wraps a Clicker and an AutomationElement lookup.
/// Used by ClickRunEngine to provide real UI Automation clicking through the IClickExecutor interface.
/// </summary>
public sealed class AutomationClickExecutor : IClickExecutor
{
    private readonly Clicker _clicker;
    private readonly Func<ElementDescriptor, AutomationElement?> _elementLookup;

    public AutomationClickExecutor(Clicker clicker, Func<ElementDescriptor, AutomationElement?> elementLookup)
    {
        _clicker = clicker;
        _elementLookup = elementLookup;
    }

    public ClickResult Click(ElementDescriptor descriptor, int preClickDelayMs = 0)
    {
        var element = _elementLookup(descriptor);
        if (element == null)
            return new ClickResult(false, $"AutomationElement not found for: {descriptor.ButtonLabel}");

        return _clicker.Click(element, descriptor, preClickDelayMs);
    }
}
