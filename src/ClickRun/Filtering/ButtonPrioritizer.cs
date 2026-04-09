using ClickRun.Models;

namespace ClickRun.Filtering;

/// <summary>
/// Ranks candidate buttons by label match quality and selects the single best candidate.
/// </summary>
public static class ButtonPrioritizer
{
    /// <summary>
    /// Selects the single highest-priority candidate from the list.
    /// Priority 0 (highest): button label exactly matches a whitelist label (case-insensitive).
    /// Priority 1 (lower): button label contains a whitelist label as substring (case-insensitive).
    /// Tie-breaking: prefer the candidate matching the earliest button label in the whitelist order.
    /// </summary>
    /// <param name="candidates">Candidates that passed the safety filter.</param>
    /// <param name="whitelist">The full whitelist for reference (unused; ordering comes from MatchedEntry).</param>
    /// <returns>The single best candidate, or null if the list is empty.</returns>
    public static Candidate? SelectBest(List<Candidate> candidates, List<WhitelistEntry> whitelist)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        Candidate? best = null;
        int bestPriority = int.MaxValue;
        int bestLabelIndex = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var (priority, labelIndex) = ComputeRank(candidate);

            if (priority < bestPriority
                || (priority == bestPriority && labelIndex < bestLabelIndex))
            {
                best = candidate;
                bestPriority = priority;
                bestLabelIndex = labelIndex;
            }
        }

        return best;
    }

    private static (int Priority, int LabelIndex) ComputeRank(Candidate candidate)
    {
        var buttonLabel = candidate.Element.ButtonLabel;
        var labels = candidate.MatchedEntry.ButtonLabels;

        for (int i = 0; i < labels.Count; i++)
        {
            if (string.Equals(buttonLabel, labels[i], StringComparison.OrdinalIgnoreCase))
            {
                return (0, i); // Exact match — highest priority
            }
        }

        for (int i = 0; i < labels.Count; i++)
        {
            if (buttonLabel.Contains(labels[i], StringComparison.OrdinalIgnoreCase))
            {
                return (1, i); // Substring match — lower priority
            }
        }

        // No match found (shouldn't happen if safety filter ran correctly)
        return (int.MaxValue, int.MaxValue);
    }
}
