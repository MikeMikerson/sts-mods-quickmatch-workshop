using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace QuickMatch.Emotes;

// The game's own art and text for each status: its power's icon, name and tooltip, and the reaction wheel's thumb
// up ("I have") and question mark ("Who has?"). Loaded from the game at run time.
internal static class EmoteArt
{
    private const string QuestionPath = "res://images/ui/emote/question.png";
    private const string ThumbUpPath = "res://images/ui/emote/thumb_up.png";
    private const string ButtonPath = "res://images/ui/emote/happy_cultist.png";

    private static Dictionary<string, EmoteStatus>? _statusByPowerId;

    public static PowerModel PowerOf(EmoteStatus status) => status switch
    {
        EmoteStatus.Vulnerable => ModelDb.Power<VulnerablePower>(),
        EmoteStatus.Weak => ModelDb.Power<WeakPower>(),
        EmoteStatus.Strength => ModelDb.Power<StrengthPower>(),
        _ => ModelDb.Power<PoisonPower>(),
    };

    public static Texture2D Icon(EmoteStatus status) => PowerOf(status).Icon;

    public static string Name(EmoteStatus status) => PowerOf(status).Title.GetFormattedText();

    public static IHoverTip Tip(EmoteStatus status) => HoverTipFactory.FromPower(PowerOf(status));

    public static Texture2D? Mark(bool ask) => ResourceLoader.Load<Texture2D>(ask ? QuestionPath : ThumbUpPath);

    public static Texture2D? ButtonIcon => ResourceLoader.Load<Texture2D>(ButtonPath);

    // A card's tooltip for a power carries the power's id.
    public static IReadOnlyDictionary<string, EmoteStatus> StatusByPowerId
    {
        get
        {
            if (_statusByPowerId == null)
            {
                _statusByPowerId = new Dictionary<string, EmoteStatus>();
                foreach (EmoteStatus status in Statuses)
                {
                    _statusByPowerId[PowerOf(status).Id.ToString()] = status;
                }
            }
            return _statusByPowerId;
        }
    }

    public static readonly EmoteStatus[] Statuses = [EmoteStatus.Vulnerable, EmoteStatus.Weak, EmoteStatus.Strength, EmoteStatus.Poison];

    // The status icon with its mark beside it: the whole message, read left to right.
    public static EmotePair Pair(Emote emote, float iconSize)
    {
        var row = new HBoxContainer { Name = "QuickMatchEmotePair", MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 2);
        TextureRect icon = Picture(Icon(emote.Status), iconSize);
        TextureRect mark = Picture(Mark(emote.Ask), iconSize * 0.8f);
        row.AddChild(icon);
        row.AddChild(mark);
        return new EmotePair(row, icon, mark);
    }

    private static TextureRect Picture(Texture2D? texture, float size) => new()
    {
        Texture = texture,
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        PivotOffset = new Vector2(size, size) / 2,
    };
}

internal sealed record EmotePair(HBoxContainer Row, TextureRect Icon, TextureRect Mark);
