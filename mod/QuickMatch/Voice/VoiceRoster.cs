using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Voice;

internal enum TeammateState
{
    OnVoice,
    NoVoice,
    Left,
}

// Everyone else who has been in this match, in the order they were first seen, and whether their voice reaches us.
// A mixed match's voice rides the relay while its play also goes over lobby chat, so a teammate can still be playing
// with no voice between us.
internal sealed class VoiceRoster
{
    private readonly List<ulong> _seen = new();
    private readonly HashSet<ulong> _present = new();
    private readonly HashSet<ulong> _onVoice = new();
    private ulong _lobby;

    public IReadOnlyCollection<ulong> VoiceMembers => _onVoice;

    public void Update(ulong lobby, ulong local, IEnumerable<ulong> inLobby, IEnumerable<ulong> onVoice, Func<ulong, bool> isQuickMatchPeer)
    {
        if (lobby != _lobby)
        {
            _lobby = lobby;
            _seen.Clear();
        }
        _onVoice.Clear();
        _onVoice.UnionWith(onVoice.Where(id => id != local && isQuickMatchPeer(id)));
        _present.Clear();
        _present.UnionWith(inLobby.Where(id => id != local));
        _present.UnionWith(_onVoice);
        _seen.AddRange(_present.Where(id => !_seen.Contains(id)).OrderBy(id => id));
    }

    public IEnumerable<(ulong Player, TeammateState State)> Teammates => _seen.Select(id => (id, StateOf(id)));

    private TeammateState StateOf(ulong player)
    {
        if (_onVoice.Contains(player))
        {
            return TeammateState.OnVoice;
        }
        return _present.Contains(player) ? TeammateState.NoVoice : TeammateState.Left;
    }
}
