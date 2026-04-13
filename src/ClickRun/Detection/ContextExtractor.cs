using System.Text;
using System.Windows.Automation;
using Serilog;

namespace ClickRun.Detection;

/// <summary>
/// Extracts visible text context from the UI tree around a button element.
/// Walks up to the nearest small container (Group/Custom), then collects
/// text from immediate children only. Capped at 500 chars for performance.
/// </summary>
public static class ContextExtractor
{
    private const int MaxTextLength = 500;
    private const int MaxDepth = 3;
    private const int MaxSiblings = 20;

    // Only Group and Custom are small enough containers.
    // Window and Pane are too large (entire app content).
    private static readonly HashSet<ControlType> SmallContainerTypes = new()
    {
        ControlType.Group,
        ControlType.Custom
    };

    /// <summary>
    /// Extracts context text surrounding a button element.
    /// Returns empty string if no meaningful container is found (avoids walking the whole window).
    /// </summary>
    public static string Extract(AutomationElement button, AutomationElement windowRoot, ILogger? logger = null)
    {
        try
        {
            var container = FindSmallContainer(button);
            if (container == null)
            {
                // No small container found — just return the button's siblings' text
                return ExtractSiblingText(button, logger);
            }

            var sb = new StringBuilder();
            AppendText(sb, container.Current.Name);

            var walker = TreeWalker.ControlViewWalker;
            CollectText(walker, container, sb, depth: 0);

            var result = sb.Length > MaxTextLength ? sb.ToString(0, MaxTextLength) : sb.ToString();
            return result;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Walks up max 3 levels looking for a Group or Custom container.
    /// Returns null if only Window/Pane found (too large).
    /// </summary>
    private static AutomationElement? FindSmallContainer(AutomationElement button)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = walker.GetParent(button);

            for (int i = 0; i < 3 && current != null; i++)
            {
                try
                {
                    var controlType = current.Current.ControlType;

                    if (SmallContainerTypes.Contains(controlType))
                        return current;

                    // Stop if we hit a Window or Pane — too large
                    if (controlType == ControlType.Window || controlType == ControlType.Pane)
                        return null;

                    current = walker.GetParent(current);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
            }
        }
        catch (ElementNotAvailableException) { }

        return null;
    }

    /// <summary>
    /// Fallback: collect text from the button's siblings (same parent level).
    /// </summary>
    private static string ExtractSiblingText(AutomationElement button, ILogger? logger)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var parent = walker.GetParent(button);
            if (parent == null) return string.Empty;

            var sb = new StringBuilder();
            var child = walker.GetFirstChild(parent);
            int count = 0;

            while (child != null && count < MaxSiblings)
            {
                try
                {
                    AppendText(sb, child.Current.Name);
                    if (sb.Length >= MaxTextLength) break;
                    child = walker.GetNextSibling(child);
                    count++;
                }
                catch (ElementNotAvailableException)
                {
                    try { child = walker.GetNextSibling(child); count++; }
                    catch { break; }
                }
            }

            return sb.Length > MaxTextLength ? sb.ToString(0, MaxTextLength) : sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void CollectText(TreeWalker walker, AutomationElement element, StringBuilder sb, int depth)
    {
        if (depth > MaxDepth || sb.Length >= MaxTextLength) return;

        try
        {
            var child = walker.GetFirstChild(element);
            int count = 0;

            while (child != null && count < MaxSiblings && sb.Length < MaxTextLength)
            {
                try
                {
                    AppendText(sb, child.Current.Name);
                    CollectText(walker, child, sb, depth + 1);
                    child = walker.GetNextSibling(child);
                    count++;
                }
                catch (ElementNotAvailableException)
                {
                    try { child = walker.GetNextSibling(child); count++; }
                    catch { break; }
                }
            }
        }
        catch (ElementNotAvailableException) { }
    }

    private static void AppendText(StringBuilder sb, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) && text.Length < 200)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }
    }
}
