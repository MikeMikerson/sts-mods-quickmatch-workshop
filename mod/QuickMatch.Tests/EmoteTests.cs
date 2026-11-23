using System.Collections.Generic;
using QuickMatch.Emotes;
using Xunit;

namespace QuickMatch.Tests;

// Emotes carry only a status and whether it is a question, each player's game limits what it shows from each
// sender, and a player can only say they have a status their cards apply.
public class EmoteTests
{
    private const ulong Self = 76561198000000001;
    private const ulong Other = 76561198000000002;
    private const ulong Stranger = 76561198000000009;
    private static readonly ulong[] Run = [Self, Other];

    [Theory]
    [InlineData(EmoteStatus.Vulnerable, false)]
    [InlineData(EmoteStatus.Weak, true)]
    [InlineData(EmoteStatus.Poison, true)]
    public void AnEmoteReadsBackAsItWasWritten(EmoteStatus status, bool ask)
    {
        byte[] message = EmoteWire.Write(new Emote(status, ask));

        Assert.Equal(EmoteWire.Length, message.Length);
        Assert.True(EmoteWire.TryRead(message, out Emote emote));
        Assert.Equal(new Emote(status, ask), emote);
    }

    [Theory]
    // A status past the last one, with and without the ask bit.
    [InlineData(new byte[] { (byte)'Q', (byte)'E', 1, 4 })]
    [InlineData(new byte[] { (byte)'Q', (byte)'E', 1, 0x84 })]
    // Another format, another magic, another length.
    [InlineData(new byte[] { (byte)'Q', (byte)'E', 2, 0 })]
    [InlineData(new byte[] { (byte)'Q', (byte)'V', 1, 0 })]
    [InlineData(new byte[] { (byte)'Q', (byte)'E', 1, 0, 0 })]
    public void AnythingElseIsNotAnEmote(byte[] message)
    {
        Assert.False(EmoteWire.TryRead(message, out _));
    }

    [Fact]
    public void EmotesKeepADistinctLengthFromTheOtherChatMessages()
    {
        Assert.NotEqual(Matchmaking.KickVoteWire.Length, EmoteWire.Length);
        Assert.NotEqual(Matchmaking.StartShortWire.Length, EmoteWire.Length);
    }

    [Fact]
    public void AnEmoteTooSoonAfterTheLastIsDroppedWithoutHidingTheSender()
    {
        var limiter = EmoteLimiter.ForReceiving();

        Assert.True(limiter.Allow(Other, 0));
        Assert.False(limiter.Allow(Other, 0.5));
        Assert.True(limiter.Allow(Other, 1.2));
    }

    [Fact]
    public void ASenderPastTheBurstIsHiddenForAMinute()
    {
        var limiter = EmoteLimiter.ForReceiving();
        for (int i = 0; i < 6; i++)
        {
            Assert.True(limiter.Allow(Other, i * 1.1));
        }

        Assert.False(limiter.Allow(Other, 7));
        Assert.False(limiter.Allow(Other, 40));
        Assert.True(limiter.Allow(Other, 68));
    }

    [Fact]
    public void OneSendersFloodDoesNotHideAnother()
    {
        var limiter = EmoteLimiter.ForReceiving();
        for (int i = 0; i < 8; i++)
        {
            limiter.Allow(Stranger, i * 1.1);
        }

        Assert.True(limiter.Allow(Other, 9));
    }

    [Fact]
    public void TheButtonsOwnLimitNeverLetsAnHonestPlayerBeHidden()
    {
        var own = EmoteLimiter.ForSending();
        var receiver = EmoteLimiter.ForReceiving();
        int sent = 0;
        // Pressing as fast as the button allows for two minutes, each emote arriving up to 0.2 s late or early.
        for (double now = 0; now < 120; now += 0.1)
        {
            if (own.Allow(Self, now))
            {
                double jitter = sent % 2 == 0 ? 0.2 : -0.2;
                Assert.True(receiver.Allow(Self, now + jitter), $"emote {sent} at {now:0.0} s was turned away");
                sent++;
            }
        }

        Assert.True(sent >= 16);
    }

    [Fact]
    public void BlockedHiddenAndOutsidePlayersAreNotShown()
    {
        var gate = new EmoteGate();

        Assert.False(gate.Shows(Other, Run, blocked: true, now: 0));
        Assert.False(gate.Shows(Stranger, Run, blocked: false, now: 0));
        gate.SetHidden(Other, true);
        Assert.False(gate.Shows(Other, Run, blocked: false, now: 0));
        gate.SetHidden(Other, false);
        Assert.True(gate.Shows(Other, Run, blocked: false, now: 0));
    }

    [Fact]
    public void APlayerHasOnlyTheStatusesTheirCardsName()
    {
        var statuses = new Dictionary<string, EmoteStatus>
        {
            ["POWER.VULNERABLE_POWER"] = EmoteStatus.Vulnerable,
            ["POWER.WEAK_POWER"] = EmoteStatus.Weak,
            ["POWER.POISON_POWER"] = EmoteStatus.Poison,
        };
        // A hand of Bash (Vulnerable), a Strike and a Defend (no statuses), and a card naming an unrelated power.
        string[][] hand = [["POWER.VULNERABLE_POWER"], [], ["KEYWORD.BLOCK"], ["POWER.DEXTERITY_POWER"]];

        Assert.Equal([EmoteStatus.Vulnerable], EmoteChoices.Have(hand, statuses));
    }

    [Fact]
    public void StatusesComeInThePickersOrderOnce()
    {
        var statuses = new Dictionary<string, EmoteStatus> { ["P"] = EmoteStatus.Poison, ["W"] = EmoteStatus.Weak };

        Assert.Equal([EmoteStatus.Weak, EmoteStatus.Poison], EmoteChoices.Have([["P"], ["W", "P"]], statuses));
    }
}
