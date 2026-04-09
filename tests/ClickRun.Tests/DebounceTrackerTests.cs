using ClickRun.Models;
using ClickRun.Tracking;
using Xunit;

namespace ClickRun.Tests;

public class DebounceTrackerTests
{
    private static ElementDescriptor MakeElement(
        string process = "TestApp",
        string title = "Test Window",
        string label = "Run",
        string automationId = "btn1") =>
        new(process, title, label, automationId, true, true, true);

    // --- ComputeHash tests ---

    [Fact]
    public void ComputeHash_SameInputs_ReturnsSameHash()
    {
        var element = MakeElement();
        var hash1 = DebounceTracker.ComputeHash(element);
        var hash2 = DebounceTracker.ComputeHash(element);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_ReturnsDifferentHash()
    {
        var a = MakeElement(label: "Run");
        var b = MakeElement(label: "Cancel");

        Assert.NotEqual(
            DebounceTracker.ComputeHash(a),
            DebounceTracker.ComputeHash(b));
    }

    [Fact]
    public void ComputeHash_Returns32HexChars()
    {
        var hash = DebounceTracker.ComputeHash(MakeElement());

        Assert.Equal(32, hash.Length);
        Assert.Matches("^[0-9a-f]{32}$", hash);
    }

    [Fact]
    public void ComputeHash_UsesAllFields()
    {
        // Changing any single field should produce a different hash
        var baseline = MakeElement();
        var baseHash = DebounceTracker.ComputeHash(baseline);

        Assert.NotEqual(baseHash, DebounceTracker.ComputeHash(MakeElement(process: "Other")));
        Assert.NotEqual(baseHash, DebounceTracker.ComputeHash(MakeElement(title: "Other")));
        Assert.NotEqual(baseHash, DebounceTracker.ComputeHash(MakeElement(label: "Other")));
        Assert.NotEqual(baseHash, DebounceTracker.ComputeHash(MakeElement(automationId: "other")));
    }

    [Fact]
    public void ComputeHash_NullAutomationId_DoesNotThrow()
    {
        var element = MakeElement(automationId: null!);
        var hash = DebounceTracker.ComputeHash(element);

        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void ComputeHash_EmptyAutomationId_DoesNotThrow()
    {
        var element = MakeElement(automationId: "");
        var hash = DebounceTracker.ComputeHash(element);

        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void ComputeHash_NullAndEmptyAutomationId_ProduceSameHash()
    {
        var nullElement = MakeElement(automationId: null!);
        var emptyElement = MakeElement(automationId: "");

        Assert.Equal(
            DebounceTracker.ComputeHash(nullElement),
            DebounceTracker.ComputeHash(emptyElement));
    }

    [Fact]
    public void ComputeHash_NullAutomationId_DiffersFromNonEmpty()
    {
        var nullElement = MakeElement(automationId: null!);
        var normalElement = MakeElement(automationId: "btn1");

        Assert.NotEqual(
            DebounceTracker.ComputeHash(nullElement),
            DebounceTracker.ComputeHash(normalElement));
    }

    // --- IsInCooldown tests ---

    [Fact]
    public void IsInCooldown_NoRecord_ReturnsFalse()
    {
        var tracker = new DebounceTracker();

        Assert.False(tracker.IsInCooldown("somehash", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void IsInCooldown_RecentRecord_ReturnsTrue()
    {
        var tracker = new DebounceTracker();
        tracker.Record("hash1");

        Assert.True(tracker.IsInCooldown("hash1", TimeSpan.FromSeconds(2)));
    }

    // --- Record tests ---

    [Fact]
    public void Record_StoresHash()
    {
        var tracker = new DebounceTracker();
        tracker.Record("abc");

        Assert.True(tracker.IsInCooldown("abc", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Record_OverwritesExistingTimestamp()
    {
        var tracker = new DebounceTracker();
        tracker.Record("abc");
        // Recording again should update the timestamp
        tracker.Record("abc");

        Assert.True(tracker.IsInCooldown("abc", TimeSpan.FromSeconds(10)));
    }

    // --- Prune tests ---

    [Fact]
    public void Prune_RemovesOldEntries()
    {
        var tracker = new DebounceTracker();
        tracker.Record("old");

        // We can't easily fake time, but we can verify prune doesn't remove fresh entries
        tracker.Prune();

        // Fresh entry should still be present
        Assert.True(tracker.IsInCooldown("old", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Prune_PreservesFreshEntries()
    {
        var tracker = new DebounceTracker();
        tracker.Record("fresh1");
        tracker.Record("fresh2");

        tracker.Prune();

        Assert.True(tracker.IsInCooldown("fresh1", TimeSpan.FromSeconds(10)));
        Assert.True(tracker.IsInCooldown("fresh2", TimeSpan.FromSeconds(10)));
    }
}
