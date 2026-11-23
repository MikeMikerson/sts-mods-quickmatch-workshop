using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace QuickMatch.Ui;

// A short gesture by the sender's character in a fight, in that character's manner, played on the creature's body
// node (the game scales and tints the node above it, never this one). Every move is relative to where the body
// was, and the body is put back exactly when the gesture ends. One gesture per player at a time.
internal static class EmoteMotion
{
    private static readonly Dictionary<ulong, Tween> Running = new();

    public static void Play(Player sender, bool ask)
    {
        if (Running.TryGetValue(sender.NetId, out Tween? running) && running.IsValid() && running.IsRunning())
        {
            return;
        }
        if (sender.Creature.IsDead)
        {
            return;
        }
        Creature mover = sender.Character is Necrobinder && !ask && sender.IsOstyAlive && sender.Osty is { } osty
            ? osty
            : sender.Creature;
        if (NCombatRoom.Instance?.GetCreatureNode(mover) is not NCreature node
            || node.Body is not { } body)
        {
            return;
        }
        var rest = new Pose(body.Position, body.Scale, body.Rotation, body.Modulate);
        Tween tween = body.CreateTween();
        var g = new Gesture(tween, body, rest);
        switch (sender.Character)
        {
            case Ironclad when ask: g.Lean(); break;
            case Ironclad: g.Nod(); break;
            case Silent when ask: g.QuietTilt(); break;
            case Silent: g.ShadowDip(); break;
            case Defect when ask: g.Jitter(); break;
            case Defect: g.ServoRise(); break;
            case Regent when ask: g.Stamps(); break;
            case Regent: g.PuffUp(); break;
            case Necrobinder when ask: g.BoredSway(); break;
            case Necrobinder when mover != sender.Creature: g.Hop(24); break;
            case Necrobinder: g.SassySway(); break;
            default:
                if (ask) g.Tilt(); else g.Hop(18);
                break;
        }
        tween.TweenCallback(Callable.From(() => rest.Apply(body)));
        Running[sender.NetId] = tween;
    }

    private sealed record Pose(Vector2 Position, Vector2 Scale, float Rotation, Color Modulate)
    {
        public void Apply(Node2D body)
        {
            body.Position = Position;
            body.Scale = Scale;
            body.Rotation = Rotation;
            body.Modulate = Modulate;
        }
    }

    // Moves in the body's own terms: up is a lift in pixels, stretch multiplies its scale, lean adds to its rotation
    // (positive leans toward the enemies, as the players face right).
    private sealed class Gesture(Tween tween, Node2D body, Pose rest)
    {
        public void Hop(float height)
        {
            To(stretch: new(1.06f, 0.92f), seconds: 0.08);
            To(up: height, stretch: new(0.96f, 1.06f), seconds: 0.14, ease: Tween.EaseType.Out);
            To(stretch: new(1.05f, 0.94f), seconds: 0.12, ease: Tween.EaseType.In);
            To(seconds: 0.1);
        }

        public void Tilt()
        {
            To(lean: -0.1f, seconds: 0.18);
            tween.TweenInterval(0.25);
            To(seconds: 0.2);
        }

        // A slow dip and a firm stop.
        public void Nod()
        {
            To(up: -4, stretch: new(1f, 0.95f), seconds: 0.22, ease: Tween.EaseType.InOut);
            tween.TweenInterval(0.08);
            To(seconds: 0.12, trans: Tween.TransitionType.Expo, ease: Tween.EaseType.Out);
        }

        public void Lean()
        {
            To(lean: 0.09f, seconds: 0.15, trans: Tween.TransitionType.Back, ease: Tween.EaseType.Out);
            tween.TweenInterval(0.35);
            To(seconds: 0.2);
        }

        // Into the shadows and back.
        public void ShadowDip()
        {
            To(stretch: new(1.03f, 0.9f), fade: 0.45f, seconds: 0.12);
            tween.TweenInterval(0.12);
            To(seconds: 0.18);
        }

        public void QuietTilt()
        {
            To(lean: -0.07f, seconds: 0.35);
            tween.TweenInterval(0.3);
            To(seconds: 0.35);
        }

        // Three stiff ticks up, then down in one go.
        public void ServoRise()
        {
            for (int tick = 1; tick <= 3; tick++)
            {
                To(up: tick * 6, seconds: 0.03, trans: Tween.TransitionType.Linear);
                tween.TweenInterval(0.09);
            }
            To(seconds: 0.08, trans: Tween.TransitionType.Quad, ease: Tween.EaseType.In);
        }

        public void Jitter()
        {
            for (int i = 0; i < 8; i++)
            {
                To(right: i % 2 == 0 ? 4 : -4, seconds: 0.035, trans: Tween.TransitionType.Linear);
            }
            To(seconds: 0.035, trans: Tween.TransitionType.Linear);
        }

        public void PuffUp()
        {
            To(stretch: new(1.08f, 1.1f), seconds: 0.2, trans: Tween.TransitionType.Back, ease: Tween.EaseType.Out);
            tween.TweenInterval(0.4);
            To(seconds: 0.25);
        }

        public void Stamps()
        {
            for (int i = 0; i < 2; i++)
            {
                To(up: 10, stretch: new(0.97f, 1.04f), seconds: 0.08, ease: Tween.EaseType.Out);
                To(stretch: new(1.06f, 0.94f), seconds: 0.06, ease: Tween.EaseType.In);
                To(seconds: 0.1);
            }
        }

        public void SassySway()
        {
            To(lean: -0.08f, seconds: 0.2);
            To(lean: 0.08f, seconds: 0.25);
            To(seconds: 0.2);
        }

        public void BoredSway()
        {
            To(lean: -0.06f, seconds: 0.35);
            To(lean: 0.06f, seconds: 0.5);
            To(seconds: 0.35);
        }

        // One step of the gesture, from wherever the last step left the body; anything not named goes back to rest.
        private void To(double seconds, float up = 0, float right = 0, Vector2? stretch = null, float lean = 0,
            float fade = 1, Tween.TransitionType? trans = null, Tween.EaseType? ease = null)
        {
            tween.SetTrans(trans ?? Tween.TransitionType.Sine).SetEase(ease ?? Tween.EaseType.InOut);
            tween.TweenProperty(body, "position", rest.Position + new Vector2(right, -up), seconds);
            tween.Parallel().TweenProperty(body, "scale", rest.Scale * (stretch ?? Vector2.One), seconds);
            tween.Parallel().TweenProperty(body, "rotation", rest.Rotation + lean, seconds);
            tween.Parallel().TweenProperty(body, "modulate:a", rest.Modulate.A * fade, seconds);
        }
    }
}
