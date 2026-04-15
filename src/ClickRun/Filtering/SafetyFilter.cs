using ClickRun.Matching;
using ClickRun.Models;
using Serilog;

namespace ClickRun.Filtering;

/// <summary>
/// Validates candidate UI elements against the whitelist and safety rules.
/// </summary>
public class SafetyFilter
{
    private readonly ILogger _logger;

    public SafetyFilter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks whether an element passes all safety criteria.
    /// Returns a result indicating pass/fail, the matched whitelist entry, and any rejection reason.
    /// </summary>
    public SafetyFilterResult Check(ElementDescriptor element, Configuration config)
    {
        // 1. Control type must be Button
        if (!element.IsButton)
        {
            return Reject(element, "not_button");
        }

        // 2. Element must be visible
        if (!element.IsVisible)
        {
            return Reject(element, "not_visible");
        }

        // 3. Element must be enabled
        if (!element.IsEnabled)
        {
            return Reject(element, "not_enabled");
        }

        // 4. Blocklist check — hard reject if label contains any blocked word
        if (IsBlocked(element.ButtonLabel, config.BlockedLabels))
        {
            return Reject(element, "blocked_label");
        }

        // 5. Find a matching whitelist entry (process name + window title + button label)
        bool processMatched = false;
        bool titleMatched = false;

        foreach (var entry in config.Whitelist)
        {
            // Wildcard process guard
            if (string.Equals(entry.ProcessName, "*", StringComparison.Ordinal))
            {
                if (!config.EnableWildcardProcess)
                {
                    _logger.Debug(
                        "Skipping wildcard process entry for {AppName} | {Label}",
                        element.ProcessName,
                        element.ButtonLabel);
                    continue; // Skip wildcard entry, check remaining non-wildcard entries
                }
            }
            else if (!string.Equals(entry.ProcessName, element.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            processMatched = true;

            // Window title must match at least one pattern
            if (!TitleMatcher.MatchAny(element.WindowTitle, entry.WindowTitles))
            {
                titleMatched = false;
                continue;
            }

            titleMatched = true;

            // Button label: exact match OR prefix match against whitelist labels
            if (!MatchesButtonLabel(element.ButtonLabel, entry.ButtonLabels, config.PrefixMatchLabels))
            {
                continue;
            }

            // Context-based safety check for labels that require safe context (e.g., "Yes")
            if (RequiresContextValidation(element.ButtonLabel, config.ContextRequiredLabels))
            {
                // Build full context: window title + extracted UI context text
                var fullContext = string.IsNullOrEmpty(element.ContextText)
                    ? element.WindowTitle
                    : $"{element.WindowTitle} {element.ContextText}";

                _logger.Debug(
                    "Context check for '{Label}': {ContextLength} chars | Preview: {Preview}",
                    element.ButtonLabel,
                    fullContext.Length,
                    fullContext.Length > 120 ? fullContext[..120] + "..." : fullContext);

                // Dangerous context check first — hard reject
                if (ContainsAny(fullContext, config.DangerousContextKeywords))
                {
                    return Reject(element, "dangerous_context");
                }

                // Safe context check — must contain at least one safe keyword
                if (!ContainsAny(fullContext, config.SafeContextKeywords))
                {
                    return Reject(element, "missing_safe_context");
                }
            }

            // All checks passed
            return new SafetyFilterResult(true, entry, null);
        }

        // Determine the most specific rejection reason
        if (!processMatched)
        {
            return Reject(element, "process_mismatch");
        }

        if (!titleMatched)
        {
            return Reject(element, "title_mismatch");
        }

        return Reject(element, "label_mismatch");
    }

    private static bool MatchesButtonLabel(string buttonLabel, List<string> allowedLabels, List<string> prefixMatchLabels)
    {
        var normalized = NormalizeLabel(buttonLabel);

        // 1. Exact match (normalized) — "Run" = "Run", "Accept command" = "Accept command"
        foreach (var label in allowedLabels)
        {
            if (string.Equals(normalized, NormalizeLabel(label), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Prefix match ONLY for explicitly configured prefixes
        //    "Full command python -m ..." starts with "Full command " → match
        //    "Run Code (Ctrl+Alt+N)" does NOT match because "Run" is not in prefixMatchLabels
        foreach (var prefix in prefixMatchLabels)
        {
            var normalizedPrefix = NormalizeLabel(prefix);
            if (StartsWithWord(normalized, normalizedPrefix))
            {
                return true;
            }
        }

        // NO contains fallback. NO word-boundary fallback.
        // All valid labels must be in the whitelist as exact entries or in prefixMatchLabels.
        return false;
    }

    /// <summary>
    /// Returns true if 'text' starts with 'prefix' at a word boundary.
    /// The prefix must be at position 0 and followed by a space, end-of-string, or nothing.
    /// "Trust command and accept".StartsWithWord("Trust") → true
    /// "Do not trust this".StartsWithWord("Trust") → false
    /// "Trustworthy".StartsWithWord("Trust") → false (no word boundary)
    /// </summary>
    private static bool StartsWithWord(string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        // Must be followed by space, end-of-string, or nothing
        if (text.Length == prefix.Length)
            return true;

        return text[prefix.Length] == ' ';
    }

    /// <summary>
    /// Normalizes a UI label by removing non-ASCII/corrupted unicode, trimming whitespace,
    /// and collapsing multiple spaces. Keeps the label readable for matching.
    /// </summary>
    internal static string NormalizeLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
            return string.Empty;

        // Remove non-ASCII characters (corrupted unicode like î©°, îª´, etc.)
        var chars = new char[label.Length];
        int pos = 0;
        for (int i = 0; i < label.Length; i++)
        {
            char c = label[i];
            if (c >= 0x20 && c < 0x7F) // printable ASCII only
            {
                chars[pos++] = c;
            }
            else if (c == '\t' || c == '\n' || c == '\r')
            {
                chars[pos++] = ' ';
            }
        }

        // Trim and collapse multiple spaces
        var result = new string(chars, 0, pos).Trim();
        while (result.Contains("  "))
            result = result.Replace("  ", " ");

        return result;
    }

    private static bool IsBlocked(string buttonLabel, List<string> blockedLabels)
    {
        var normalized = NormalizeLabel(buttonLabel);
        foreach (var blocked in blockedLabels)
        {
            if (normalized.Contains(NormalizeLabel(blocked), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresContextValidation(string buttonLabel, List<string> contextRequiredLabels)
    {
        foreach (var label in contextRequiredLabels)
        {
            if (buttonLabel.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, List<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private SafetyFilterResult Reject(ElementDescriptor element, string reason)
    {
        _logger.Debug(
            "Rejected: {Reason} | App={AppName} | Label={Label}",
            reason,
            element.ProcessName,
            element.ButtonLabel);

        return new SafetyFilterResult(false, null, reason);
    }
}
