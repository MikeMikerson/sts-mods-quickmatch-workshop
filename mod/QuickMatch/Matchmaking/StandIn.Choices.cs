using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Models;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace QuickMatch.Matchmaking;

internal static partial class StandIn
{
    public static void OnWaitingForChoice(Player player, uint choiceId)
    {
        if (IsHost && IsAbsent(player.NetId))
        {
            Safely(() => Answer(player, choiceId), "answer their choice");
        }
    }

    private static void AnswerPendingChoices(ulong playerId)
    {
        if (_state?.GetPlayer(playerId) is not { } player)
        {
            return;
        }
        var waiting = new List<uint>();
        foreach (object entry in (IList)Field(RunManager.Instance.PlayerChoiceSynchronizer, "_receivedChoices")!)
        {
            Type type = entry.GetType();
            var sender = (ulong)type.GetField("senderId", Members)!.GetValue(entry)!;
            var choiceId = (uint)type.GetField("choiceId", Members)!.GetValue(entry)!;
            var source = (TaskCompletionSource<NetPlayerChoiceResult>)type.GetField("completionSource", Members)!.GetValue(entry)!;
            if (sender == playerId && !source.Task.IsCompleted)
            {
                waiting.Add(choiceId);
            }
        }
        foreach (uint choiceId in waiting)
        {
            Answer(player, choiceId);
        }
    }

    private static void Answer(Player player, uint choiceId)
    {
        MainFile.Logger.Info($"host stands in for {player.NetId}: choice {choiceId} answered with nothing chosen");
        _net!.SendMessage(new QuickMatchStandInChoiceMessage { player = player.NetId, choiceId = choiceId });
        ResumeOwed[player.NetId] = ResumeOwed.GetValueOrDefault(player.NetId) + 1;
        ApplyNothingChosen(player, choiceId);
    }

    private static void OnStandInChoiceMessage(QuickMatchStandInChoiceMessage message, ulong senderId)
    {
        if (!_active || _net?.Type != NetGameType.Client || _state?.GetPlayer(message.player) is not { } player)
        {
            return;
        }
        MainFile.Logger.Info($"the host answered choice {message.choiceId} of absent player {message.player} with nothing chosen");
        Safely(() => ApplyNothingChosen(player, message.choiceId), "take the host's answer");
    }

    private static void ApplyNothingChosen(Player player, uint choiceId)
    {
        var answer = new NetPlayerChoiceResult { type = PlayerChoiceType.Index, indexes = NothingChosen };
        Method(typeof(PlayerChoiceSynchronizer), "OnReceivePlayerChoice").Invoke(RunManager.Instance.PlayerChoiceSynchronizer, [player, choiceId, answer]);
    }

    // The game reads a choice as the kind its caller expects (cards, a player, indexes); the answer is an empty index
    // list, which the patched readers of the other kinds turn into "nothing chosen" of their own kind.
    public static bool IsNothingChosen(List<int>? indexes) => indexes != null && ReferenceEquals(indexes, NothingChosen);

    // The game resumes an action that waited for a player's choice on every peer only when its owner asks; for a
    // choice the host answered, the host asks in the absent owner's place.
    public static bool TakeResume(ulong owner)
    {
        if (!IsHost || ResumeOwed.GetValueOrDefault(owner) <= 0)
        {
            return false;
        }
        ResumeOwed[owner]--;
        MainFile.Logger.Info($"host resumes the action of absent player {owner} after its choice");
        return true;
    }

    // A hook of an absent player's (a relic or card at the turn's start, say) that asks them to choose would be put in
    // the queue by its owner, so the host does, unless the owner did before leaving.
    public static void EnqueueHookIfAbsent(GenericHookGameAction action)
    {
        if (IsHost && IsAbsent(action.OwnerId) && action.State == GameActionState.None)
        {
            MainFile.Logger.Info($"host stands in for {action.OwnerId}: hook action {action.HookId}");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueueHookAction(action);
        }
    }
}
