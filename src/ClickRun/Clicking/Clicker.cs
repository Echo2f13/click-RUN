using System.Windows.Automation;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Clicking;

/// <summary>
/// Invokes a button via UI Automation's InvokePattern with a single-retry strategy.
/// </summary>
public sealed class Clicker
{
    private readonly ILogger _log;
    private readonly Random _random;

    public Clicker(ILogger logger, Random? random = null)
    {
        _log = logger.ForContext<Clicker>();
        _random = random ?? new Random();
    }

    /// <summary>
    /// Attempts to click the given button element using InvokePattern.
    /// When preClickDelayMs > 0, waits that many milliseconds before the first Invoke() call.
    /// On failure, waits 50-100ms (randomized) and retries exactly once.
    /// Returns a <see cref="ClickResult"/> indicating success or failure.
    /// </summary>
    public ClickResult Click(AutomationElement button, ElementDescriptor descriptor, int preClickDelayMs = 0)
    {
        InvokePattern invokePattern;
        try
        {
            invokePattern = (InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to get InvokePattern: {descriptor.ProcessName} | {descriptor.WindowTitle} | {descriptor.ButtonLabel} | {ex.Message}";
            _log.Error(msg);
            return new ClickResult(false, msg);
        }

        // Pre-click delay
        if (preClickDelayMs > 0)
        {
            Thread.Sleep(preClickDelayMs);
        }

        // First attempt
        try
        {
            invokePattern.Invoke();
            return new ClickResult(true);
        }
        catch
        {
            // First attempt failed — retry once after randomized delay
        }

        // Wait 50-100ms before retry
        var delayMs = _random.Next(50, 101);
        Thread.Sleep(delayMs);

        // Retry attempt
        try
        {
            invokePattern.Invoke();
            return new ClickResult(true);
        }
        catch (Exception ex)
        {
            var msg = $"Invoke failed after retry: {descriptor.ProcessName} | {descriptor.WindowTitle} | {descriptor.ButtonLabel} | {ex.Message}";
            _log.Error(msg);
            return new ClickResult(false, msg);
        }
    }
}
