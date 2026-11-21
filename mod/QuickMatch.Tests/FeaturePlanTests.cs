using QuickMatch.Patches;
using Xunit;

namespace QuickMatch.Tests;

public class FeaturePlanTests
{
    private sealed class Run
    {
        public readonly List<GameFeature> Applied = [];
        public readonly List<GameFeature> Undone = [];
        public readonly List<string> Log = [];

        public FeaturePlan.Outcome Apply(params GameFeature[] failing) =>
            FeaturePlan.Apply(
                feature =>
                {
                    if (failing.Contains(feature))
                    {
                        throw new Exception("Patching exception in method null", new ArgumentException($"{feature} target not found"));
                    }
                    Applied.Add(feature);
                },
                Undone.Add,
                Log.Add);
    }

    [Fact]
    public void EveryFeatureIsOnWhenNothingFails()
    {
        var run = new Run();
        FeaturePlan.Outcome outcome = run.Apply();

        Assert.True(outcome.ModOn);
        Assert.Empty(outcome.Off);
        Assert.Equal(Enum.GetValues<GameFeature>().Order(), run.Applied.Order());
        Assert.Empty(run.Log);
    }

    [Fact]
    public void AFailedOptionalFeatureIsUndoneAndOffWithALogLineAndTheRestStayOn()
    {
        var run = new Run();
        FeaturePlan.Outcome outcome = run.Apply(GameFeature.Blitz);

        Assert.True(outcome.ModOn);
        Assert.Equal([GameFeature.Blitz], outcome.Off);
        Assert.Equal([GameFeature.Blitz], run.Undone);
        Assert.Contains(GameFeature.Voice, run.Applied);
        Assert.Contains(run.Log, line => line.StartsWith("Blitz is off") && line.Contains("target not found"));
    }

    [Fact]
    public void AFeatureIsOffWhenOneItNeedsIsOffAndIsNeverPatched()
    {
        var run = new Run();
        FeaturePlan.Outcome outcome = run.Apply(GameFeature.RunTracking);

        Assert.True(outcome.ModOn);
        Assert.Equal(new HashSet<GameFeature> { GameFeature.RunTracking, GameFeature.Regroup, GameFeature.StandIn, GameFeature.Rejoin }, outcome.Off);
        Assert.DoesNotContain(GameFeature.Regroup, run.Applied);
        Assert.DoesNotContain(GameFeature.Rejoin, run.Applied);
        Assert.Contains(run.Log, line => line == "Rejoin is off: it needs Regroup, StandIn");
    }

    // Muting is in the player window, so voice without it would leave a player unable to silence anyone.
    [Fact]
    public void VoiceIsOffWhenThePlayerWindowIsOff()
    {
        FeaturePlan.Outcome outcome = new Run().Apply(GameFeature.PlayerWindow);

        Assert.Contains(GameFeature.Voice, outcome.Off);
    }

    [Fact]
    public void AFailedCoreLeavesTheModOffWithNothingPatched()
    {
        var run = new Run();
        FeaturePlan.Outcome outcome = run.Apply(GameFeature.Core);

        Assert.False(outcome.ModOn);
        Assert.Equal(Enum.GetValues<GameFeature>().ToHashSet(), outcome.Off);
        Assert.Empty(run.Applied);
        Assert.Equal([GameFeature.Core], run.Undone);
        Assert.Single(run.Log, line => line.StartsWith("QuickMatch is off"));
    }

    [Fact]
    public void EveryFeatureIsPlannedOnceAfterTheFeaturesItNeeds()
    {
        var seen = new HashSet<GameFeature>();
        foreach (var (feature, needs) in FeaturePlan.All)
        {
            Assert.All(needs, need => Assert.Contains(need, seen));
            Assert.True(seen.Add(feature), $"{feature} planned twice");
        }
        Assert.Equal(Enum.GetValues<GameFeature>().ToHashSet(), seen);
    }
}
