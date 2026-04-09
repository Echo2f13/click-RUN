using ClickRun.Models;

namespace ClickRun.Filtering;

/// <summary>
/// Result of a safety filter check, indicating pass/fail and the matched whitelist entry.
/// </summary>
public sealed record SafetyFilterResult(bool Passed, WhitelistEntry? MatchedEntry, string? RejectionReason);
