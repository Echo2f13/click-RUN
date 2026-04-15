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
        Assert.Null(ButtonPrioritizer.SelectBest(new List<Candidate>(), new List<WhitelistEntry>()));
    }

    [Fact]
    public void SelectBest_NullList_ReturnsNull()
    {
        Assert.Null(ButtonPrioritizer.SelectBest(null!, new List<WhitelistEntry>()));
    }

    [Fact]
    public void SelectBest_SingleCandidate_ReturnsThatCandidate()
    {
        var entry = MakeEntry("Run");
        var candidate = MakeCandidate("Run", entry);
        Assert.Same(candidate, ButtonPrioritizer.SelectBest(new List<Candidate> { candidate }, new List<WhitelistEntry> { entry }));
    }

    // --- EXECUTION OVER TRUST ---

    [Fact]
    public void AcceptCommand_BeatsTrustCommandAndAccept()
    {
        var entry = MakeEntry("Accept command", "Trust command and accept");
        var accept = MakeCandidate("Accept command", entry, "h1");
        var trust = MakeCandidate("Trust command and accept", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { trust, accept },
            new List<WhitelistEntry> { entry });

        Assert.Same(accept, result);
    }

    [Fact]
    public void AcceptCommand_BeatsRun()
    {
        var entry = MakeEntry("Run", "Accept command");
        var run = MakeCandidate("Run", entry, "h1");
        var accept = MakeCandidate("Accept command", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { run, accept },
            new List<WhitelistEntry> { entry });

        Assert.Same(accept, result);
    }

    [Fact]
    public void AcceptCommand_BeatsFullCommand()
    {
        var entry = MakeEntry("Accept command");
        var accept = MakeCandidate("Accept command", entry, "h1");
        var full = MakeCandidate("Full command python test.py", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { full, accept },
            new List<WhitelistEntry> { entry });

        Assert.Same(accept, result);
    }

    [Fact]
    public void Run_BeatsTrustCommandAndAccept()
    {
        var entry = MakeEntry("Run", "Trust command and accept");
        var run = MakeCandidate("Run", entry, "h1");
        var trust = MakeCandidate("Trust command and accept", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { trust, run },
            new List<WhitelistEntry> { entry });

        Assert.Same(run, result);
    }

    [Fact]
    public void Run_BeatsFullCommand()
    {
        var entry = MakeEntry("Run");
        var run = MakeCandidate("Run", entry, "h1");
        var full = MakeCandidate("Full command python test.py", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { full, run },
            new List<WhitelistEntry> { entry });

        Assert.Same(run, result);
    }

    [Fact]
    public void AllThreePresent_AcceptCommandWins()
    {
        var entry = MakeEntry("Run", "Accept command", "Trust command and accept");
        var run = MakeCandidate("Run", entry, "h1");
        var accept = MakeCandidate("Accept command", entry, "h2");
        var trust = MakeCandidate("Trust command and accept", entry, "h3");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { run, trust, accept },
            new List<WhitelistEntry> { entry });

        Assert.Same(accept, result);
    }

    [Fact]
    public void TrustOnly_TrustWins()
    {
        // Trust labels are no longer in default whitelist, but if user adds them they still work
        var entry = MakeEntry("Trust command and accept");
        var trust = MakeCandidate("Trust command and accept", entry);

        Assert.Same(trust, ButtonPrioritizer.SelectBest(
            new List<Candidate> { trust },
            new List<WhitelistEntry> { entry }));
    }

    [Fact]
    public void FullCommandOnly_FullCommandWins()
    {
        // Full command labels are no longer in default prefix list, but if present they still work
        var entry = MakeEntry("Run");
        var full = MakeCandidate("Full command python test.py", entry);

        Assert.Same(full, ButtonPrioritizer.SelectBest(
            new List<Candidate> { full },
            new List<WhitelistEntry> { entry }));
    }

    [Fact]
    public void Allow_BeatsRun()
    {
        var entry = MakeEntry("Run", "Allow");
        var run = MakeCandidate("Run", entry, "h1");
        var allow = MakeCandidate("Allow", entry, "h2");

        var result = ButtonPrioritizer.SelectBest(
            new List<Candidate> { run, allow },
            new List<WhitelistEntry> { entry });

        Assert.Same(allow, result);
    }

    [Fact]
    public void CaseInsensitive()
    {
        var entry = MakeEntry("run");
        var candidate = MakeCandidate("RUN", entry);

        Assert.Same(candidate, ButtonPrioritizer.SelectBest(
            new List<Candidate> { candidate },
            new List<WhitelistEntry> { entry }));
    }
}
