using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.addons.mega_text;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Patches;

// Puts the voice controls into the game's screens. Each control hides itself outside a quick match
// (VoiceChat.InMatch), so ordinary runs and other lobbies look as in vanilla.
[PatchFeature(GameFeature.Voice)]
public static class VoicePatches
{
    // Every change of the game's Master Volume goes through here (the slider, the game's start, the fade when
    // muted in the background); while voice plays it is moved off the bus voice uses.
    [HarmonyPatch(typeof(NDebugAudioManager), nameof(NDebugAudioManager.SetMasterAudioVolume))]
    public static class MasterVolume
    {
        public static void Postfix() => GameMasterBus.Follow();
    }

    // Speaking ring and voice state on each player's portrait in the run's player list, and the mic button
    // beside the player's own row.
    [HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
    public static class PlayerRow
    {
        public static void Postfix(NMultiplayerPlayerState __instance)
        {
            if (__instance.Player == null)
            {
                return;
            }
            if (__instance.GetNodeOrNull<Control>("%CharacterIcon") is { } icon)
            {
                VoiceBadge.Attach(icon, __instance.Player.NetId);
            }
            AddMicIfOwn(__instance, __instance.Player.NetId);
        }
    }

    // The Quick Match row (voice state and Player Options) in the panel a clicked player row opens.
    [PatchFeature(GameFeature.PlayerWindow)]
    [HarmonyPatch(typeof(NMultiplayerPlayerExpandedState), nameof(NMultiplayerPlayerExpandedState.Create))]
    public static class PlayerPanel
    {
        public static void Postfix(Player player, NMultiplayerPlayerExpandedState __result)
        {
            if (player != null && __result != null)
            {
                PlayerEntry.AttachToPlayerPanel(__result, player.NetId);
            }
        }
    }

    // Speaking ring and voice state on each player slot of the lobby (character select, Blitz and Dread), a
    // click on the name to open the player's window (PlayerEntry), and the mic button beside the player's own
    // slot: voice works while players pick characters.
    [PatchFeature(GameFeature.PlayerWindow)]
    [HarmonyPatch(typeof(NRemoteLobbyPlayer), nameof(NRemoteLobbyPlayer._Ready))]
    public static class LobbySlot
    {
        public static void Postfix(NRemoteLobbyPlayer __instance)
        {
            if (__instance.GetNodeOrNull<Control>("%CharacterIcon") is { } icon)
            {
                VoiceBadge.Attach(icon, __instance.PlayerId);
            }
            PlayerEntry.AttachToLobbySlot(__instance);
            AddMicIfOwn(__instance, __instance.PlayerId);
        }
    }

    // Right of the name and health bar, which end near x = 260 in both the lobby slot and the run row.
    private static readonly Vector2 MicPosition = new(264, 6);

    private static void AddMicIfOwn(Control entry, ulong player)
    {
        if (player == 0 || player != BlockPlayers.LocalId() || entry.GetNodeOrNull("QuickMatchVoice") != null)
        {
            return;
        }
        Control button = VoiceButton.Create("QuickMatchVoice");
        button.Position = MicPosition;
        entry.AddChild(button);
    }

    // One Quick Match button in the pause menu, under Settings, made from the game's own Settings button. It
    // opens the Quick Match screen (QuickMatchMenu): on its Voice tab in a match with voice on, else on
    // Blocked players. Shown in a quick-match run, and in any match with voice on
    // (LookLayout.PauseButtonShown).
    [PatchFeature(GameFeature.PlayerWindow)]
    [HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu._Ready))]
    public static class PauseMenu
    {
        private const string NodeName = "QuickMatchPause";

        public static void Postfix(NPauseMenu __instance)
        {
            var container = __instance.GetNodeOrNull<Control>("%ButtonContainer");
            var settings = container?.GetNodeOrNull<NPauseMenuButton>("Settings");
            if (container == null || settings == null || container.GetNodeOrNull(NodeName) != null)
            {
                return;
            }
            // Without the signals: the menu connected Settings to its own handler, and the copy connects its own in _Ready.
            var voice = (NPauseMenuButton)settings.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation));
            voice.Name = NodeName;
            // The highlight is a shader parameter on the button image's material: the copy needs its own,
            // or hovering the copy would light up Settings too. Before _Ready, which picks the material up.
            if (voice.GetNodeOrNull<CanvasItem>("ButtonImage") is { Material: { } material } image)
            {
                image.Material = (Material)material.Duplicate();
            }
            container.AddChild(voice);
            container.MoveChild(voice, settings.GetIndex() + 1);
            voice.GetNodeOrNull<MegaLabel>("Label")?.SetTextAutoSize(Loc.Get("QUICKMATCH.title"));
            voice.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => QuickMatchMenu.Open(LookLayout.PauseTab(VoiceChat.InMatch))));
            voice.Visible = LookLayout.PauseButtonShown(VoiceChat.InMatch, Regroup.InQuickMatchRun);
            __instance.VisibilityChanged += () =>
            {
                if (GodotObject.IsInstanceValid(voice))
                {
                    voice.Visible = LookLayout.PauseButtonShown(VoiceChat.InMatch, Regroup.InQuickMatchRun);
                }
            };
        }
    }
}
