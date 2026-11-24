using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

internal static class SavedInviteButton
{
    public static void Add(NMultiplayerLoadGameScreen screen, ulong lobby, ulong host)
    {
        var button = GameLook.Button("QuickMatchSavedInvite", Loc.Get("QUICKMATCH.LINK.copy"), () =>
        {
            DisplayServer.ClipboardSet(LinkPolicy.InviteLink(lobby, host));
            Notice.Show(Loc.Get("QUICKMATCH.LINK.copied"), 2);
        });
        screen.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        button.OffsetRight = -40;
        button.OffsetLeft = button.OffsetRight - button.CustomMinimumSize.X;
        button.OffsetTop = 40;
        button.OffsetBottom = button.OffsetTop + button.CustomMinimumSize.Y;
        screen.VisibilityChanged += RemoveWhenClosed;

        void RemoveWhenClosed()
        {
            if (!screen.Visible)
            {
                screen.VisibilityChanged -= RemoveWhenClosed;
                button.QueueFree();
            }
        }
    }
}
