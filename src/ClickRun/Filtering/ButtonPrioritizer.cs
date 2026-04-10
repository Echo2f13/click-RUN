using ClickRun.Models;

namespace ClickRun.Filtering;

/// <summary>
/// Ranks candidate buttons by label match quality and selects the single best candidate.
/// 
/// Ranking strategy (strict keyword priority):
///   1. Whitelist label index is the PRIMARY ranking — earlier labels in the list are higher priority.
///      Config order defines keyword priority: Run(0) > Allow(1) > Accept(5) > Trust(7) etc.
///   2. Match type is the SECONDARY ranking — exact match beats substring match at the same index.
///
/// Examples with config order [Run, Allow, Approve, Continue, Yes, Accept, ..., Trust, ...]:
///   - "Run" (exact, index 0) beats "Accept command" (exact, index 6)
///   - "Trust command and accept" resolves to "Accept" (substring, index 5) not "Trust command and accept" (exact, index 8)
///   - "Run anyway" resolves to "Run" (substring, index 0)
/// </summary>
public static class ButtonPrioritizer
{
    /// <summary>
    /// Selects the single highest-priority candidate from the list.
    /// Primary: earliest whitelist label index wins (keyword priority order).
    /// Secondary: exact match beats substring match at the same label index.
    /// </summary>
    public static Candidate? SelectBest(List<Candidate> candidates, List<WhitelistEntry> whitelist)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        Candidate? best = null;
        int bestLabelIndex = int.MaxValue;
        int bestMatchType = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var (labelIndex, matchType) = ComputeRank(candidate);

            // Primary: lower label index wins (keyword priority)
            // Secondary: lower match type wins (0=exact, 1=substring)
            if (labelIndex < bestLabelIndex
                || (labelIndex == bestLabelIndex && matchType < bestMatchType))
            {
                best = candidate;
                bestLabelIndex = labelIndex;
                bestMatchType = matchType;
            }
        }

        return best;
    }

    /// <summary>
    /// Computes the rank for a candidate.
    /// Returns (bestLabelIndex, matchType) where:
    ///   - bestLabelIndex = earliest whitelist label that matches (exact or substring)
    ///   - matchType = 0 for exact, 1 for substring
    /// Scans all labels to find the earliest match of any type.
    /// </summary>
    private static (int LabelIndex, int MatchType) ComputeRank(Candidate candidate)
    {
        var buttonLabel = candidate.Element.ButtonLabel;
        var labels = candidate.MatchedEntry.ButtonLabels;

        int bestIndex = int.MaxValue;
        int bestType = int.MaxValue;

        for (int i = 0; i < labels.Count; i++)
        {
            // Stop early — can't beat an earlier index
            if (i >= bestIndex)
                break;

            if (string.Equals(buttonLabel, labels[i], StringComparison.OrdinalIgnoreCase))
            {
                // Exact match at this index — best possible for this index
                bestIndex = i;
                bestType = 0;
            }
            else if (buttonLabel.Contains(labels[i], StringComparison.OrdinalIgnoreCase))
            {
                // Substring match at this index
                bestIndex = i;
                bestType = 1;
            }
        }

        return (bestIndex, bestType);
    }
}
