using System;
using System.Collections.Generic;
using System.Linq;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// The layout choices behind QuickMatch's panels and strips in the game's look, kept free of Godot so the
// tests can run them.
internal static class LookLayout
{
    // In the paginator's order; 0 is Any.
    public static readonly IReadOnlyList<int> Sizes = [0, 2, 3, 4];

    // Any for a size the paginator does not have.
    public static int SizePage(int size) => Math.Max(0, Sizes.ToList().IndexOf(size));

    public static int SizeAt(int page) => Sizes[Math.Clamp(page, 0, Sizes.Count - 1)];

    // The Ascension paginator's pages: Any, then the levels chosen.
    public const int AnyPage = 0;
    public const int ChosenPage = 1;

    public static int AscensionPage(AscensionChoice choice) => choice.Mode == AscensionMode.Choose && choice.Chosen is { Count: > 0 } ? ChosenPage : AnyPage;

    // The levels the Ascension row's second page stands for, and the Choose panel opens on: the last levels
    // chosen that are unlocked now, or the player's maximum alone for a player who never chose (or whose
    // levels are all locked again).
    public static IReadOnlyList<int> ChosenLevels(AscensionChoice last, int maxAscension)
    {
        int max = Math.Clamp(maxAscension, 0, MatchPolicy.MaxAscension);
        List<int> kept = last.Mode == AscensionMode.Choose
            ? (last.Chosen ?? []).Where(a => a >= 0 && a <= max).Distinct().Order().ToList()
            : [];
        return kept.Count > 0 ? kept : [max];
    }

    // One row up to 6 levels, else two rows as even as they go (A0 to A10: 6 and 5).
    public static int ChooserColumns(int levels) => levels <= 6 ? Math.Max(1, levels) : (levels + 1) / 2;

    // At least, or gap below the lowest of the things the strip must not cover (a screen's title, a strip
    // above it, the lobby's status area; 0 when not shown).
    public static float StripTop(float least, float gap, params float[] edges)
    {
        float top = least;
        foreach (float edge in edges)
        {
            if (edge > 0)
            {
                top = Math.Max(top, edge + gap);
            }
        }
        return top;
    }

    // The widest and narrowest a button's plate is drawn, against its own shape: further looks stretched or squeezed.
    public const float MostStretch = 4f / 3;
    public const float MostSqueeze = 3f / 4;

    // At least least, wide enough for its text (textWidth at the button's font size) with room for the
    // plate's pointed ends, but never wider than the plate drawn a third wider than its own shape (natural
    // wide at the button's height), where a longer text shrinks instead, and never narrower than a quarter
    // less than that shape.
    public static float ButtonWidth(float least, float textWidth, float natural)
    {
        float wanted = Math.Max(least, textWidth + 64);
        float most = Math.Max(least, natural * MostStretch);
        return Math.Max(Math.Min(wanted, most), natural * MostSqueeze);
    }

    // As the game's settings sliders show it.
    public static string PercentText(int percent) => $"{percent}%";

    // The setting is kept as a fraction (a volume of 1 is 100%); the slider has whole percents.
    public static int Percent(double value) => (int)Math.Round(Math.Clamp(value, 0, 10) * 100);

    public static double FromPercent(int percent, double least, double most) => Math.Clamp(percent / 100.0, least, most);

    public static bool PauseButtonShown(bool voiceMatch, bool quickMatchRun) => voiceMatch || quickMatchRun;

    public static MenuTab PauseTab(bool voiceMatch) => voiceMatch ? MenuTab.Voice : MenuTab.Blocked;

    public static IReadOnlyList<VoiceRow> MoreRows() => [VoiceRow.Sensitivity, VoiceRow.Strangers];

    // Cancel goes on the right, the side the game's own popups give their safe answer. With nothing to choose
    // (the player is blocked already, or cannot be blocked again yet) the one answer is Close.
    public static IReadOnlyList<BlockAnswer> BlockAnswers(bool blockable, bool inRun)
    {
        if (!blockable)
        {
            return [BlockAnswer.Close];
        }
        return inRun ? [BlockAnswer.Stay, BlockAnswer.Leave, BlockAnswer.Cancel] : [BlockAnswer.Block, BlockAnswer.Cancel];
    }

    // Top to bottom, then the buttons left to right.
    public static IReadOnlyList<PlayerControl> PlayerControls(bool voice, bool blockable, bool canVote)
    {
        var controls = new List<PlayerControl>();
        if (voice)
        {
            controls.Add(PlayerControl.Mute);
            controls.Add(PlayerControl.Volume);
        }
        if (blockable)
        {
            controls.Add(PlayerControl.Block);
        }
        if (canVote)
        {
            controls.Add(PlayerControl.Vote);
        }
        controls.Add(PlayerControl.Close);
        return controls;
    }

    public static IReadOnlyList<VoteAnswer> VoteAnswers() => [VoteAnswer.Start, VoteAnswer.Cancel];

    // A window never passes the view's edges less margin; one larger than that is shrunk to fit.
    public static float FitScale(float width, float height, float viewWidth, float viewHeight, float margin)
    {
        if (width <= 0 || height <= 0 || viewWidth <= 2 * margin || viewHeight <= 2 * margin)
        {
            return 1f;
        }
        return Math.Min(1f, Math.Min((viewWidth - 2 * margin) / width, (viewHeight - 2 * margin) / height));
    }
}

internal enum MenuTab
{
    Voice,
    Blocked,
}

internal enum VoiceRow
{
    Sensitivity,
    Strangers,
}

internal enum BlockAnswer
{
    // Block, and stay in the run with them until it ends.
    Stay,
    // Block, and leave the run now.
    Leave,
    // Block (not in a run with them).
    Block,
    Cancel,
    // Nothing to choose: the window only says why.
    Close,
}

internal enum PlayerControl
{
    Mute,
    Volume,
    Block,
    Vote,
    Close,
}

internal enum VoteAnswer
{
    Start,
    Cancel,
}
