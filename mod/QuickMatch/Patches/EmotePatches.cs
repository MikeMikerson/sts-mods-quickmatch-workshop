using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using QuickMatch.Emotes;
using QuickMatch.Ui;

namespace QuickMatch.Patches;

// The run's player list, once it holds a row per player: each row is where its player's emotes show outside
// fights, and the player's own row gets the emote button.
[PatchFeature(GameFeature.Emotes)]
[HarmonyPatch(typeof(NMultiplayerPlayerStateContainer), nameof(NMultiplayerPlayerStateContainer.Initialize))]
public static class EmotePatches
{
    private const string NodeName = "QuickMatchEmote";

    public static void Postfix(NMultiplayerPlayerStateContainer __instance)
    {
        foreach (NMultiplayerPlayerState row in __instance.GetChildren().OfType<NMultiplayerPlayerState>())
        {
            if (row.Player == null)
            {
                continue;
            }
            EmoteRoom.AddRow(row);
            if (row.Player.NetId == BlockPlayers.LocalId() && row.GetNodeOrNull(NodeName) == null)
            {
                row.AddChild(EmoteButton.Create(NodeName));
            }
        }
    }
}
