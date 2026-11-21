using QuickMatch.Voice;
using Xunit;

namespace QuickMatch.Tests;

// Voice staying off for a player in a match needs the game, so it is checked in a match.
public class VoiceModeTests
{
    [Theory]
    [InlineData(true, true, true)]
    // Voice turned off since the run began: never heard in it.
    [InlineData(true, false, false)]
    // A no-voice run stays without voice whatever the player chooses now.
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ARunGoneBackIntoHasVoiceOnlyIfTheRunAndTheChoiceBothSayYes(bool runVoice, bool choice, bool voice)
    {
        Assert.Equal(voice, MatchVoice.ForRun(runVoice, choice));
    }
}
