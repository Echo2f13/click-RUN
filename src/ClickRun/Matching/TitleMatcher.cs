using System.Text.RegularExpressions;
using ClickRun.Models;

namespace ClickRun.Matching;

/// <summary>
/// Matches window titles against configured patterns using the specified match mode.
/// </summary>
public static class TitleMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Matches a window title against a pattern using the specified mode.
    /// </summary>
    /// <param name="windowTitle">The actual window title to check.</param>
    /// <param name="pattern">The pattern to match against.</param>
    /// <param name="mode">The matching strategy (Exact, Contains, or Regex).</param>
    /// <returns>True if the window title matches the pattern; otherwise false.</returns>
    public static bool Match(string windowTitle, string pattern, MatchMode mode)
    {
        return mode switch
        {
            MatchMode.Exact => windowTitle.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            MatchMode.Contains => windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            MatchMode.Regex => Regex.IsMatch(windowTitle, pattern, RegexOptions.IgnoreCase, RegexTimeout),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported match mode.")
        };
    }

    /// <summary>
    /// Returns true if the window title matches any of the given patterns.
    /// </summary>
    /// <param name="windowTitle">The actual window title to check.</param>
    /// <param name="patterns">The list of patterns to match against.</param>
    /// <returns>True if any pattern matches; otherwise false.</returns>
    public static bool MatchAny(string windowTitle, List<WindowTitlePattern> patterns)
    {
        foreach (var entry in patterns)
        {
            if (Match(windowTitle, entry.Pattern, entry.MatchMode))
                return true;
        }

        return false;
    }
}
