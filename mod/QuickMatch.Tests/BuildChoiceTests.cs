using QuickMatchLoader;
using Xunit;

namespace QuickMatch.Tests;

public class BuildChoiceTests
{
    private static readonly string[] Builds = ["/mods/QuickMatch/QuickMatch-0.111.dll", "/mods/QuickMatch/QuickMatch-0.113.dll"];

    [Theory]
    [InlineData("0.111.1", "QuickMatch-0.111.dll")]
    [InlineData("0.113.0", "QuickMatch-0.113.dll")]
    [InlineData("0.113.1", "QuickMatch-0.113.dll")]
    // A game version between two builds, or after the last, runs the build before it.
    [InlineData("0.112.0", "QuickMatch-0.111.dll")]
    [InlineData("0.115.2", "QuickMatch-0.113.dll")]
    public void AGameRunsTheNewestBuildNotNewerThanItself(string game, string expected)
    {
        Assert.Equal(expected, Path.GetFileName(BuildChoice.For(Version.Parse(game), Builds)));
    }

    [Fact]
    public void AGameOlderThanEveryBuildRunsNone()
    {
        Assert.Null(BuildChoice.For(Version.Parse("0.110.0"), Builds));
    }

    [Fact]
    public void OnlyFilesNamedForAVersionAreBuilds()
    {
        Assert.Null(BuildChoice.For(Version.Parse("0.113.1"), ["/mods/QuickMatch/QuickMatch.dll", "/mods/QuickMatch/QuickMatch-old.dll"]));
    }
}
