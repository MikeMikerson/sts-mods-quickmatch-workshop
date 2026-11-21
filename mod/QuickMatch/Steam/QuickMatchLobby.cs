using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using QuickMatch.Lobby;
using QuickMatch.Matchmaking;
using QuickMatch.Voice;

namespace QuickMatch.Steam;

// Turns the host's friends-only lobby into a searchable quick-match lobby and back.
internal static class QuickMatchLobby
{
    public readonly record struct CompatKey(string Version, string Db, string Mods)
    {
        public static CompatKey Local()
        {
            PeerVersionInfo info = PeerVersionInfo.LocalDefault();
            return new CompatKey(
                info.version,
                info.idDatabaseHash.ToString(CultureInfo.InvariantCulture),
                MatchPolicy.ModsHash(info.gameplayAffectingMods));
        }

        public override string ToString() => $"ver={Version} db={Db} mods={Mods}";
    }

    private static ILobbyBackend Backend => LobbyBackend.Current;

    // An Any lobby's qm_size is "any", so exact-size searches never find it.
    public static void Publish(ulong lobby, int size, bool anySize, int ascension, uint createdAt)
    {
        CompatKey key = CompatKey.Local();
        string sizeValue = anySize ? LobbyKeys.SizeAny : size.ToString(CultureInfo.InvariantCulture);
        Backend.SetData(lobby, LobbyKeys.Size, sizeValue);
        Backend.SetData(lobby, LobbyKeys.Mode, MatchMode.LobbyValue(MatchMode.On));
        Backend.SetData(lobby, LobbyKeys.Voice, MatchVoice.LobbyValue(MatchVoice.On));
        Backend.SetData(lobby, LobbyKeys.Ascension, ascension.ToString(CultureInfo.InvariantCulture));
        Backend.SetData(lobby, LobbyKeys.Version, key.Version);
        Backend.SetData(lobby, LobbyKeys.Db, key.Db);
        Backend.SetData(lobby, LobbyKeys.Mods, key.Mods);
        Backend.SetData(lobby, LobbyKeys.CreatedAt, createdAt.ToString(CultureInfo.InvariantCulture));
        Backend.SetData(lobby, LobbyKeys.Members, "1");
        SetLink(lobby);
        // Marker last, so a lobby only matches searches once it is fully described.
        Backend.SetData(lobby, LobbyKeys.Proto, LobbyKeys.ProtoValue);
        bool isPublic = Backend.SetKind(lobby, LobbyKind.Public);
        MainFile.Logger.Info($"published lobby {lobby} size={sizeValue} {MatchMode.Text(MatchMode.On)} {MatchVoice.Text(MatchVoice.On)} qm_asc={ascension} t={createdAt} {key} {LinkText} public={isPublic}");
    }

    private static void SetLink(ulong lobby)
    {
        Backend.SetData(lobby, LobbyKeys.Link, LobbyKeys.LinkSockets);
        Backend.SetData(lobby, LobbyKeys.Pool, LobbyKeys.PoolDesktop);
    }

    private const string LinkText = "link=sockets pool=desktop";

    public static void UpdateMembers(ulong lobby, int members)
    {
        Backend.SetData(lobby, LobbyKeys.Members, members.ToString(CultureInfo.InvariantCulture));
    }

    // Steam server seconds; 0 clears it.
    public static void SetDeadline(ulong lobby, uint deadline)
    {
        if (deadline == 0)
        {
            Backend.DeleteData(lobby, LobbyKeys.Deadline);
        }
        else
        {
            Backend.SetData(lobby, LobbyKeys.Deadline, deadline.ToString(CultureInfo.InvariantCulture));
        }
    }

    // 0 when no countdown runs.
    public static uint Deadline(ulong lobby) =>
        uint.TryParse(Backend.GetData(lobby, LobbyKeys.Deadline), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value : 0;

    // True only for a lobby we own.
    public static bool IsQuickMatch(ulong lobby)
    {
        return Backend.GetData(lobby, LobbyKeys.Proto) == LobbyKeys.ProtoValue;
    }

    public static void Unpublish(ulong lobby)
    {
        Backend.DeleteData(lobby, LobbyKeys.Proto);
    }

    // Strangers cannot get in: they do not know the key, and the game's load lobby turns away anyone not in the save.
    public static void PublishResume(ulong lobby, string runKey)
    {
        Backend.SetData(lobby, LobbyKeys.Version, CompatKey.Local().Version);
        Backend.SetData(lobby, LobbyKeys.ResumeKey, runKey);
        if (Backend.Owner(lobby) is var owner and not 0)
        {
            Backend.SetData(lobby, LobbyKeys.ResumeHost, owner.ToString(CultureInfo.InvariantCulture));
        }
        SetLink(lobby);
        // A run's own lobby, listed while a player is absent, may still carry the match marker: a search for players must
        // not count or join it.
        Backend.DeleteData(lobby, LobbyKeys.Proto);
        Backend.SetData(lobby, LobbyKeys.ResumeProto, LobbyKeys.ResumeProtoValue);
        bool isPublic = Backend.SetKind(lobby, LobbyKind.Public);
        MainFile.Logger.Info($"published resume lobby {lobby} key={runKey} {LinkText} public={isPublic}");
    }

    // True only for a lobby we own.
    public static bool IsResume(ulong lobby)
    {
        return Backend.GetData(lobby, LobbyKeys.ResumeProto) == LobbyKeys.ResumeProtoValue;
    }

    public static void UnpublishResume(ulong lobby)
    {
        Backend.DeleteData(lobby, LobbyKeys.ResumeProto);
        Backend.DeleteData(lobby, LobbyKeys.ResumeKey);
        Backend.DeleteData(lobby, LobbyKeys.ResumeHost);
    }

    // The game closes its lobby when the run starts (private) and reopens it as friends only. Our lobbies: closed
    // drops the marker and goes private, open stays public. A reopened run is unlisted and private once it starts.
    public static void SetClosed(ulong lobby, bool isClosed)
    {
        if (IsResume(lobby))
        {
            if (isClosed)
            {
                UnpublishResume(lobby);
            }
            Backend.SetKind(lobby, isClosed ? LobbyKind.Private : LobbyKind.Public);
            MainFile.Logger.Info($"resume lobby {lobby} closed={isClosed}");
            return;
        }
        if (!IsQuickMatch(lobby))
        {
            Backend.SetKind(lobby, isClosed ? LobbyKind.Private : LobbyKind.FriendsOnly);
            return;
        }
        if (isClosed)
        {
            Unpublish(lobby);
            Backend.SetKind(lobby, LobbyKind.Private);
        }
        else
        {
            Backend.SetKind(lobby, LobbyKind.Public);
        }
        MainFile.Logger.Info($"lobby {lobby} closed={isClosed}");
    }

    // When the host leaves, Steam hands the lobby to a remaining member. Unpublish first so it is never listed again
    // without a host behind it.
    public static void OnHostStopping(ulong lobby, NetError reason)
    {
        if (IsResume(lobby))
        {
            UnpublishResume(lobby);
            Backend.SetJoinable(lobby, false);
            MainFile.Logger.Info($"resume lobby {lobby} unpublished (host stopping: {reason})");
            return;
        }
        if (!IsQuickMatch(lobby))
        {
            return;
        }
        Unpublish(lobby);
        Backend.SetJoinable(lobby, false);
        MainFile.Logger.Info($"lobby {lobby} unpublished (host stopping: {reason})");
    }
}
