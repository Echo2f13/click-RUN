using System.Security.Cryptography;
using System.Text;
using ClickRun.Models;

namespace ClickRun.Tracking;

/// <summary>
/// Tracks recently clicked elements to prevent duplicate clicks within a cooldown period.
/// </summary>
public sealed class DebounceTracker
{
    private readonly Dictionary<string, DateTime> _recentClicks = new();

    /// <summary>
    /// Computes a SHA256 hash of the element descriptor, truncated to 16 bytes (32 hex chars).
    /// Uses length-prefixed encoding to prevent delimiter collision attacks.
    /// Format: {len}:{processName}{len}:{windowTitle}{len}:{buttonLabel}[{len}:{automationId}]
    /// </summary>
    public static string ComputeHash(ElementDescriptor element)
    {
        var sb = new StringBuilder();
        AppendLengthPrefixed(sb, element.ProcessName ?? "");
        AppendLengthPrefixed(sb, element.WindowTitle ?? "");
        AppendLengthPrefixed(sb, element.ButtonLabel ?? "");
        if (!string.IsNullOrEmpty(element.AutomationId))
            AppendLengthPrefixed(sb, element.AutomationId);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes, 0, 16).ToLowerInvariant();
    }

    private static void AppendLengthPrefixed(StringBuilder sb, string value)
    {
        sb.Append(value.Length);
        sb.Append(':');
        sb.Append(value);
    }

    /// <summary>
    /// Returns true if the given hash was recorded and the elapsed time since recording
    /// is less than the specified cooldown.
    /// </summary>
    public bool IsInCooldown(string hash, TimeSpan cooldown)
    {
        if (_recentClicks.TryGetValue(hash, out var timestamp))
        {
            return (DateTime.UtcNow - timestamp) < cooldown;
        }

        return false;
    }

    /// <summary>
    /// Records a click for the given hash at the current UTC time.
    /// </summary>
    public void Record(string hash)
    {
        _recentClicks[hash] = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes all entries older than 10 seconds.
    /// </summary>
    public void Prune()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(10);
        var staleKeys = _recentClicks
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _recentClicks.Remove(key);
        }
    }
}
