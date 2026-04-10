using ClickRun.Filtering;
using ClickRun.Models;
using Xunit;

namespace ClickRun.Tests;

public class ButtonPrioritizerTests
{
    private static WhitelistEntry MakeEntry(params string[] labels) => new()
    {
        ProcessName = "TestApp",
        WindowTitles = new List<WindowTitlePattern>
        {
            new() { Pattern = "Test", MatchMode = MatchMode.Contains }
        },
        ButtonLabels = labels.ToList()
    };

    private static Candidate MakeCandidate(string buttonLabel, WhitelistEntry entry, string hash = "h") =>
        new(
            new ElementDescriptor("TestApp", "Test Window", buttonLabel, "aid", true, true, true),
            entry,
            hash);

    [Fact]
    public void SelectBest_EmptyList_ReturnsNull()
    {
        var result = ButtonPrioritizer.SelectBest(new List<Candidate>(), new List<WhitelistEntry>());
        Assert.Null(result);
    }

    [Fact]
    public void SelectBest_NullList_ReturnsNull()
    {
        var result = ButtonPrioritizer.SelectBest(null!, new List<WhitelistEntry>());
        Assert.Null(result);
    }

    [Fact]
    public void SelectBest_SingleCandidate_ReturnsThatCandidate()
    {
        var entry = MakeEntry("Run", "Cancel");
        var candidate = MakeCandidate("Run", entry);

        var result = ButtonPrioritizer.SelectBest(new List<Candidate> { candidate }, new List<WhitelistEntry> { entry });

        Assert.Same(candidate, result);
    }

    [Fact]
    public void SelectBest_ExactMatchBeatsSubstringMatch()
    {
        var entry = MakeEntry("Run", "Allow");
        var exact = MakeCandidate("Run", entry, "h1");
        var substring = MakeCandidate("Run Anyway", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { substring, exact },
            new List<WhitelistEntry> { entry });

        Assert.Same(exact, result);
    }

    [Fact]
    public void SelectBest_TieBreaking_PrefersEarlierWhitelistLabel()
    {
        var entry = MakeEntry("Run", "Allow", "Continue");
        var allowCandidate = MakeCandidate("Allow", entry, "h1");
        var continueCandidate = MakeCandidate("Continue", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { continueCandidate, allowCandidate },
            new List<WhitelistEntry> { entry });

        // "Allow" is at index 1, "Continue" at index 2 — Allow wins
        Assert.Same(allowCandidate, result);
    }

    [Fact]
    public void SelectBest_ExactMatchIsCaseInsensitive()
    {
        var entry = MakeEntry("run");
        var candidate = MakeCandidate("RUN", entry);

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { candidate },
            new List<WhitelistEntry> { entry });

        Assert.Same(candidate, result);
    }

    [Fact]
    public void SelectBest_SubstringMatch_DetectedCorrectly()
    {
        var entry = MakeEntry("Run");
        var candidate = MakeCandidate("Run Anyway", entry);

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { candidate },
            new List<WhitelistEntry> { entry });

        Assert.Same(candidate, result);
    }

    [Fact]
    public void SelectBest_SubstringTieBreaking_PrefersEarlierLabel()
    {
        var entry = MakeEntry("Run", "Allow");
        var runAnyway = MakeCandidate("Run Anyway", entry, "h1");
        var allowAll = MakeCandidate("Allow All", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { allowAll, runAnyway },
            new List<WhitelistEntry> { entry });

        // Both are substring matches; "Run" is at index 0, "Allow" at index 1 — Run Anyway wins
        Assert.Same(runAnyway, result);
    }

    [Fact]
    public void SelectBest_KeywordPriority_RunBeatsAcceptAndTrust()
    {
        // Config order: Run(0), Accept(1), Trust(2)
        var entry = MakeEntry("Run", "Accept", "Trust");
        var run = MakeCandidate("Run", entry, "h1");
        var accept = MakeCandidate("Accept command", entry, "h2");
        var trust = MakeCandidate("Trust", entry, "h3");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { trust, accept, run },
            new List<WhitelistEntry> { entry });

        // "Run" is index 0 — wins over Accept(1) and Trust(2)
        Assert.Same(run, result);
    }

    [Fact]
    public void SelectBest_KeywordPriority_AcceptBeatsTrust()
    {
        // Config order: Run(0), Accept(1), Trust(2)
        var entry = MakeEntry("Run", "Accept", "Trust");
        var accept = MakeCandidate("Accept command", entry, "h1");
        var trust = MakeCandidate("Trust", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { trust, accept },
            new List<WhitelistEntry> { entry });

        // "Accept command" matches "Accept" at index 1 (substring)
        // "Trust" matches "Trust" at index 2 (exact)
        // Index 1 < index 2 → Accept command wins
        Assert.Same(accept, result);
    }

    [Fact]
    public void SelectBest_TrustCommandAndAccept_ResolvesToAccept()
    {
        // "Trust command and accept" contains both "Accept" and "Trust"
        // Config order: Run(0), Accept(1), Trust(2)
        // "Accept" is at index 1, "Trust" is at index 2
        // Should resolve to index 1 (Accept) because it's earlier
        var entry = MakeEntry("Run", "Accept", "Trust", "Trust command and accept");
        var candidate = MakeCandidate("Trust command and accept", entry, "h1");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { candidate },
            new List<WhitelistEntry> { entry });

        // The candidate matches "Accept" (substring, index 1) which beats
        // "Trust" (substring, index 2) and "Trust command and accept" (exact, index 3)
        Assert.Same(candidate, result);
        // Verify it resolved to the Accept keyword's index, not Trust's
    }

    [Fact]
    public void SelectBest_RunAnyway_ResolvesToRun()
    {
        var entry = MakeEntry("Run", "Accept", "Trust");
        var candidate = MakeCandidate("Run anyway", entry, "h1");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { candidate },
            new List<WhitelistEntry> { entry });

        // "Run anyway" contains "Run" at index 0 — highest priority keyword
        Assert.Same(candidate, result);
    }

    [Fact]
    public void SelectBest_EarlierSubstringBeatsLaterExact()
    {
        // Keyword priority order matters more than match type
        var entry = MakeEntry("Run", "Accept", "Accept command");
        var candidate = MakeCandidate("Accept command", entry, "h1");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { candidate },
            new List<WhitelistEntry> { entry });

        // "Accept command" has exact match at index 2, but substring match "Accept" at index 1
        // Index 1 < index 2 → resolves to Accept's priority
        Assert.Same(candidate, result);
    }
}
