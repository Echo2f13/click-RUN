using ClickRun.Models;

namespace ClickRun.Filtering;

/// <summary>
/// Selects the single best candidate button using intent-based priority.
/// 
/// Strategy: PREFER EXECUTION over PERMANENT TRUST.
/// "Accept command" wins over "Trust command and accept" so commands
/// execute without being permanently added to the trust list.
/// 
/// Priority (highest to lowest):
///   1. "accept command"                  — approve execution without permanent trust (weight 100)
///   2. "accept"                          — accept action (weight 90)
///   3. "allow"                           — allow action (weight 80)
///   4. "approve"                         — approve action (weight 70)
///   5. "yes, allow all edits this session" — session permission (weight 65)
///   6. "yes"                             — confirmation (weight 60)
///   7. "continue"                        — continue action (weight 50)
///   8. "run"                             — temporary execution (weight 40)
///   9. "trust command and accept"        — permanent trust (weight 30, demoted)
///  10. "trust"                           — trust action (weight 20, demoted)
///  11. "full command ..."                — trust variation (weight 10, demoted)
///  12. everything else                   — fallback (weight 1)
/// </summary>
public static class ButtonPrioritizer
{
    private static readonly Dictionary<string, int> IntentWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        // Execution actions — these run commands without permanent trust
        ["accept command"] = 100,
        ["accept"] = 90,
        ["allow"] = 80,
        ["approve"] = 70,
        ["yes, allow all edits this session"] = 65,
        ["yes"] = 60,
        ["continue"] = 50,
        ["run"] = 40,

        // Trust actions — NOT included by default.
        // If user explicitly adds them to whitelist, they get lowest priority.
    };

    /// <summary>
    /// Selects the single highest-priority candidate from the list.
    /// Prefers execution actions over permanent trust actions.
    /// </summary>
    public static Candidate? SelectBest(List<Candidate> candidates, List<WhitelistEntry> whitelist)
    {
        if (candidates is null || candidates.Count == 0)
            return null;

        Candidate? best = null;
        int bestWeight = -1;

        foreach (var candidate in candidates)
        {
            var weight = ComputeWeight(candidate);
            if (weight > bestWeight)
            {
                best = candidate;
                bestWeight = weight;
            }
        }

        return best;
    }

    private static int ComputeWeight(Candidate candidate)
    {
        var label = SafetyFilter.NormalizeLabel(candidate.Element.ButtonLabel).ToLowerInvariant();

        // Exact match against intent weights
        if (IntentWeights.TryGetValue(label, out var exactWeight))
            return exactWeight;

        return 1;
    }
}
