using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using QuickMatch.Emotes;

namespace QuickMatch.Ui;

// An emote in the game's thought bubble, drawn smaller than the game's own thoughts: the status icon with a thumb
// up ("I have") or a question mark ("Who has?"), and the status's name under them. Over the sender's character in
// a fight, where the character also moves (EmoteMotion); beside their row of the player list elsewhere. One bubble
// per player; pointing at it shows the game's tooltip for the status.
internal static class EmoteBubble
{
    private const double Seconds = 3;
    private const float Scale = 0.6f;
    private const float IconSize = 70f;
    private const int NameFontSize = 30;
    // Right of the row's name, health and the Quick Match buttons.
    private static readonly Vector2 RowOffset = new(400, 36);

    private static readonly Dictionary<ulong, Node2D> Shown = new();

    public static void Show(ulong player, Emote emote)
    {
        if (Shown.Remove(player, out Node2D? old) && GodotObject.IsInstanceValid(old))
        {
            old.QueueFree();
        }
        if (EmoteRoom.RowOf(player)?.Player is not { } sender || Place(sender) is not ({ } parent, var at, var inFight))
        {
            return;
        }
        NThoughtBubbleVfx? bubble = NThoughtBubbleVfx.Create(EmoteArt.Icon(emote.Status), DialogueSide.Left, Seconds);
        if (bubble == null)
        {
            return;
        }
        // The bubble grows itself to full size as it opens, so the holder is what makes it smaller.
        var holder = new Node2D { Name = "QuickMatchEmote", Scale = Vector2.One * Scale };
        parent.AddChild(holder);
        holder.GlobalPosition = at;
        holder.AddChild(bubble);
        bubble.TreeExited += () =>
        {
            if (GodotObject.IsInstanceValid(holder))
            {
                holder.QueueFree();
            }
        };
        Shown[player] = holder;
        bool quiet = sender.Character is Silent;
        if (bubble.GetNodeOrNull<TextureRect>("%Image") is { } image)
        {
            EmotePair pair = Fill(bubble, image, emote);
            Animate(pair, emote.Ask, quiet);
            WatchPointer(pair.Row, EmoteArt.Tip(emote.Status));
        }
        if (inFight)
        {
            EmoteMotion.Play(sender, emote.Ask);
        }
    }

    private static (Control? Parent, Vector2 At, bool InFight) Place(Player sender)
    {
        if (CombatManager.Instance.IsInProgress && NCombatRoom.Instance is { } room
            && room.GetCreatureNode(sender.Creature) != null)
        {
            return (room.CombatVfxContainer, NThoughtBubbleVfx.GetCreatureSpeechPosition(sender.Creature), true);
        }
        if (EmoteRoom.RowOf(sender.NetId) is { } row)
        {
            return (NRun.Instance?.GlobalUi?.AboveTopBarVfxContainer, row.GlobalPosition + RowOffset, false);
        }
        return (null, default, false);
    }

    // The bubble's own picture area holds the pair and the name; the picture itself is not drawn, and its fade-in
    // still carries what is inside it.
    private static EmotePair Fill(NThoughtBubbleVfx bubble, TextureRect image, Emote emote)
    {
        image.SelfModulate = Colors.Transparent;
        var column = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 0);
        EmotePair pair = EmoteArt.Pair(emote, IconSize);
        pair.Row.Alignment = BoxContainer.AlignmentMode.Center;
        pair.Row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        column.AddChild(pair.Row);
        var name = new Label
        {
            Text = EmoteArt.Name(emote.Status),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (bubble.GetNodeOrNull<Control>("%Text") is { } text)
        {
            name.AddThemeFontOverride("font", text.GetThemeFont("bold_font"));
        }
        name.AddThemeFontSizeOverride("font_size", NameFontSize);
        name.AddThemeColorOverride("font_color", StsColors.gold);
        name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.35f));
        name.AddThemeConstantOverride("shadow_offset_x", 2);
        name.AddThemeConstantOverride("shadow_offset_y", 2);
        column.AddChild(name);
        image.AddChild(column);
        return pair;
    }

    private static void Animate(EmotePair pair, bool ask, bool quiet)
    {
        Tween tween = pair.Row.CreateTween();
        // The bubble lays its contents out on the first frame; the pivots need the laid-out sizes.
        tween.TweenInterval(0.05);
        tween.TweenCallback(Callable.From(() =>
        {
            pair.Row.PivotOffset = pair.Row.Size / 2;
            pair.Mark.PivotOffset = new Vector2(pair.Mark.Size.X / 2, pair.Mark.Size.Y);
        }));
        if (!quiet)
        {
            pair.Row.Scale = Vector2.One * 0.7f;
            tween.TweenProperty(pair.Row, "scale", Vector2.One * 1.08f, 0.14).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(pair.Row, "scale", Vector2.One, 0.14).SetTrans(Tween.TransitionType.Sine);
        }
        if (ask)
        {
            tween.TweenInterval(0.1);
            for (int i = 0; i < 2; i++)
            {
                tween.TweenProperty(pair.Mark, "rotation", 0.1f, 0.15).SetTrans(Tween.TransitionType.Sine);
                tween.TweenProperty(pair.Mark, "rotation", -0.1f, 0.3).SetTrans(Tween.TransitionType.Sine);
            }
            tween.TweenProperty(pair.Mark, "rotation", 0f, 0.15).SetTrans(Tween.TransitionType.Sine);
        }
        else
        {
            tween.TweenInterval(0.15);
            tween.TweenProperty(pair.Mark, "rotation", -0.25f, 0.1).SetTrans(Tween.TransitionType.Sine);
            tween.TweenProperty(pair.Mark, "rotation", 0f, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    // The bubble takes no clicks, so it never gets in the way of a card or a target: the pointer is checked
    // each frame instead.
    private static void WatchPointer(Control target, IHoverTip tip)
    {
        if (target.GetTree() is not { } tree)
        {
            return;
        }
        bool showing = false;
        void Check()
        {
            bool over = target.IsVisibleInTree() && target.GetGlobalRect().HasPoint(target.GetGlobalMousePosition());
            if (over && !showing)
            {
                NHoverTipSet.CreateAndShow(target, tip, HoverTipAlignment.Right);
            }
            else if (!over && showing)
            {
                NHoverTipSet.Remove(target);
            }
            showing = over;
        }
        tree.ProcessFrame += Check;
        target.TreeExiting += () =>
        {
            tree.ProcessFrame -= Check;
            if (showing)
            {
                NHoverTipSet.Remove(target);
            }
        };
    }
}
