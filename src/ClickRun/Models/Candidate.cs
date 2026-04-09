namespace ClickRun.Models;

/// <summary>
/// A candidate button that passed the safety filter, ready for prioritization.
/// </summary>
public sealed record Candidate(
    ElementDescriptor Element,
    WhitelistEntry MatchedEntry,
    string Hash);
