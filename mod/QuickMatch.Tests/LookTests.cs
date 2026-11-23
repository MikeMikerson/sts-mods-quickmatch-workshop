using System.Linq;
using QuickMatch.Matchmaking;
using QuickMatch.Ui;
using Xunit;

namespace QuickMatch.Tests;

public class LookTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 0)]
    [InlineData(-1, 0)]
    public void TheSizePaginatorOpensOnTheSavedSize(int size, int page)
    {
        Assert.Equal(page, LookLayout.SizePage(size));
        Assert.Equal(size is 2 or 3 or 4 ? size : 0, LookLayout.SizeAt(page));
    }

    [Fact]
    public void ASizePageOutsideTheListIsTheNearestEnd()
    {
        Assert.Equal(0, LookLayout.SizeAt(-3));
        Assert.Equal(4, LookLayout.SizeAt(9));
    }

    [Fact]
    public void TheAscensionPaginatorShowsTheChosenLevelsOnlyForChoose()
    {
        Assert.Equal(LookLayout.ChosenPage, LookLayout.AscensionPage(AscensionChoice.Of(2, 5)));
        Assert.Equal(LookLayout.AnyPage, LookLayout.AscensionPage(AscensionChoice.Random));
        // A saved Climb is Any here, as Play Now treats it.
        Assert.Equal(LookLayout.AnyPage, LookLayout.AscensionPage(AscensionChoice.Progress));
        Assert.Equal(LookLayout.AnyPage, LookLayout.AscensionPage(new AscensionChoice(AscensionMode.Choose, [])));
    }

    [Fact]
    public void TheChosenLevelsAreTheUnlockedOnesElseTheMaximum()
    {
        Assert.Equal([2, 5], LookLayout.ChosenLevels(AscensionChoice.Of(5, 2, 5), 7));
        // Levels locked again (another profile) are dropped; with none left, the maximum alone.
        Assert.Equal([2], LookLayout.ChosenLevels(AscensionChoice.Of(2, 9), 4));
        Assert.Equal([4], LookLayout.ChosenLevels(AscensionChoice.Of(8, 9), 4));
        // A player who never chose starts on their maximum.
        Assert.Equal([6], LookLayout.ChosenLevels(AscensionChoice.Random, 6));
        Assert.Equal([10], LookLayout.ChosenLevels(AscensionChoice.Random, 14));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(6, 6)]
    [InlineData(7, 4)]
    [InlineData(11, 6)]
    public void TheChoosePanelKeepsToTwoRows(int levels, int columns) => Assert.Equal(columns, LookLayout.ChooserColumns(levels));

    [Fact]
    public void AStripStartsBelowTheLowestThingItMustNotCover()
    {
        Assert.Equal(130, LookLayout.StripTop(130, 12));
        // The Quick Match screen's title ends at 181 in the game's view: the line starts below it.
        Assert.Equal(193, LookLayout.StripTop(130, 12, 181, 0));
        // Under the vote strip when that is lower; things not shown (0) do not count.
        Assert.Equal(312, LookLayout.StripTop(130, 12, 181, 300, 0));
        Assert.Equal(130, LookLayout.StripTop(130, 12, 0, 0, 0));
    }

    [Fact]
    public void AWindowLargerThanTheViewShrinksToFit()
    {
        Assert.Equal(1f, LookLayout.FitScale(980, 560, 1920, 1080, 24));
        Assert.Equal((1280f - 48) / 1300, LookLayout.FitScale(1300, 500, 1280, 720, 24), 3);
        Assert.Equal((720f - 48) / 900, LookLayout.FitScale(980, 900, 1280, 720, 24), 3);
        // Before its first layout (no size yet) it is drawn at its own size.
        Assert.Equal(1f, LookLayout.FitScale(0, 0, 1280, 720, 24));
    }

    [Fact]
    public void AButtonFitsItsTextUpToAThirdWiderThanItsPlate()
    {
        // The settings button's plate is 552 x 134, so 263.6 wide at the button's 64.
        const float natural = 64f * 552 / 134;
        // A short text keeps the least width asked for.
        Assert.Equal(240, LookLayout.ButtonWidth(240, 80, natural));
        // "Play Any Ascension" (about 270 at 28) widens it with room for the pointed ends.
        Assert.Equal(334, LookLayout.ButtonWidth(260, 270, natural));
        // Never more than a third wider than the plate's own shape: a longer text shrinks instead.
        Assert.Equal(natural * 4 / 3, LookLayout.ButtonWidth(240, 600, natural), 3);
    }

    [Fact]
    public void AButtonIsNeverSqueezedMoreThanAQuarterNarrowerThanItsPlate()
    {
        const float natural = 64f * 552 / 134;
        // The plate fills the button's height, so a narrow button would draw its pointed ends squeezed.
        Assert.Equal(natural * 3 / 4, LookLayout.ButtonWidth(160, 60, natural), 3);
        // Block at 200 is already wide enough and keeps its width.
        Assert.Equal(200, LookLayout.ButtonWidth(200, 80, natural));
    }

    [Theory]
    // Not in a run with them: Block, then Cancel on the right.
    [InlineData(true, false, new[] { "Block", "Cancel" })]
    // In a run with them: stay until it ends or leave now.
    [InlineData(true, true, new[] { "Stay", "Leave", "Cancel" })]
    // Blocked already (or not blockable again yet): nothing to choose, the window only says why, and closes.
    [InlineData(false, false, new[] { "Close" })]
    [InlineData(false, true, new[] { "Close" })]
    public void TheBlockQuestionPutsTheSafeAnswerOnTheRight(bool blockable, bool inRun, string[] answers)
    {
        Assert.Equal(answers, LookLayout.BlockAnswers(blockable, inRun).Select(a => a.ToString()));
    }

    // Vote to Remove is its own button in the player's window.
    [Fact]
    public void TheBlockQuestionNeverOffersTheVote()
    {
        foreach (bool blockable in new[] { true, false })
        {
            foreach (bool inRun in new[] { true, false })
            {
                Assert.DoesNotContain("Vote", LookLayout.BlockAnswers(blockable, inRun).Select(a => a.ToString()));
            }
        }
    }

    [Theory]
    // A voice match: Mute and the volume first, then Block, Vote to Remove, and Close on the right.
    [InlineData(true, false, true, true, new[] { "Mute", "Volume", "Block", "Vote", "Close" })]
    // A No Voice match (or voice off for us): Block and Vote to Remove only.
    [InlineData(false, false, true, true, new[] { "Block", "Vote", "Close" })]
    // In a run with them: Hide Emotes under the voice rows, with voice or without.
    [InlineData(true, true, true, true, new[] { "Mute", "Volume", "Emotes", "Block", "Vote", "Close" })]
    [InlineData(false, true, true, true, new[] { "Emotes", "Block", "Vote", "Close" })]
    // Blocked already but still here: the vote stays, Block goes.
    [InlineData(false, false, false, true, new[] { "Vote", "Close" })]
    [InlineData(true, false, false, true, new[] { "Mute", "Volume", "Vote", "Close" })]
    // No vote to start (outside the vote's limits): Block alone.
    [InlineData(true, false, true, false, new[] { "Mute", "Volume", "Block", "Close" })]
    // Nothing to do: the window only says who they are, and closes.
    [InlineData(false, false, false, false, new[] { "Close" })]
    public void APlayersWindowOffersVoiceOnlyInAVoiceMatch(bool voice, bool emotes, bool blockable, bool canVote, string[] controls)
    {
        Assert.Equal(controls, LookLayout.PlayerControls(voice, emotes, blockable, canVote).Select(c => c.ToString()));
    }

    [Theory]
    [InlineData(1.0, 100)]
    [InlineData(0.5, 50)]
    [InlineData(0.05, 5)]
    [InlineData(0.0, 0)]
    [InlineData(2.0, 200)]
    [InlineData(-1.0, 0)]
    public void ASliderShowsAFractionAsWholePercents(double value, int percent)
    {
        Assert.Equal(percent, LookLayout.Percent(value));
    }

    [Fact]
    public void ASlidersPercentComesBackInsideTheSettingsRange()
    {
        Assert.Equal(0.7, LookLayout.FromPercent(70, 0, 1), 6);
        // The voice volume and sensitivity stop at 100%; a player's own volume goes to 200%.
        Assert.Equal(1.0, LookLayout.FromPercent(140, 0, 1), 6);
        Assert.Equal(1.4, LookLayout.FromPercent(140, 0, 2), 6);
        Assert.Equal(0.0, LookLayout.FromPercent(-5, 0, 2), 6);
    }
}
