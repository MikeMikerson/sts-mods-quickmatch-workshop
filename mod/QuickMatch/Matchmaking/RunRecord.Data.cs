using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace QuickMatch.Matchmaking;

// The quick-match run this player last took part in. Every player writes it when a quick-match run starts; it is how a
// client (which has no save file) knows it can rejoin, and how the host knows that a save it loads came from quick
// match and should be findable by its group.
//
// A record without NoVoice is a voice match (QuickMatch.Voice.MatchVoice). StayedWith: players this player blocked
// during the run and chose to stay with until it ends; this run's rejoins let them through, and the exemption goes with
// the record, so it never reaches another run. Removed: players voted out of this run; every host this run has from
// then on refuses them, and the resume search skips a lobby they host. Every voter writes it, so it outlives a
// migration; it goes with the record.
internal sealed partial record RunRecord(string Key, ulong HostId, List<ulong> PlayerIds, int Size, long StartedAt, bool NoVoice = false,
    List<ulong>? StayedWith = null, List<ulong>? Removed = null, ulong[]? DesktopPlayers = null, ulong[]? GuestPlayers = null)
{
    public bool Voice => !NoVoice;

    [JsonIgnore]
    public bool HasGuests => GuestPlayers is { Length: > 0 };

    public bool IsQuickMatchPlayer(ulong id) => PlayerIds.Contains(id) && GuestPlayers?.Contains(id) != true;

    public IReadOnlyCollection<ulong> Stayed => StayedWith ?? [];

    public IReadOnlyCollection<ulong> Banned => Removed ?? [];
}

[JsonSerializable(typeof(RunRecord))]
internal sealed partial class RunRecordJson : JsonSerializerContext;
