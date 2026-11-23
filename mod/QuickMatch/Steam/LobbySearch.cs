using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickMatch.Lobby;
using QuickMatch.Matchmaking;
using QuickMatch.Transport;
using QuickMatch.Voice;

namespace QuickMatch.Steam;

internal static class LobbySearch
{
    private const double TimeoutSeconds = 10;

    // Lobbies of our size (every lobby for 0, an Any search, Any lobbies included), game mode, version, model database
    // and gameplay mods, and only those of this match's voice choice. Ascension and size are left for MatchPolicy.Rank
    // to filter and order. Nearby asks only Steam's default distance. An empty list on Steam errors or timeout; throws
    // only when cancelled.
    public static async Task<List<LobbyCandidate>> FindAsync(int size, CancellationToken ct, int minFreeSlots = 1, bool nearby = false)
    {
        List<LobbyCandidate> candidates = await SearchAsync(size, MatchMode.On, MatchVoice.On, ct, minFreeSlots, nearby);
        string details = string.Join(", ", candidates.Select(c => $"{c.Id} n={c.Members}/{SizeText(c)} a={c.Ascension} t={c.CreatedAt}"));
        MainFile.Logger.Info($"lobby search {(nearby ? "nearby" : "worldwide")} size={(size > 0 ? size.ToString(CultureInfo.InvariantCulture) : "all")} free>={minFreeSlots} {MatchMode.Text(MatchMode.On)} {MatchVoice.Text(MatchVoice.On)} {QuickMatchLobby.CompatKey.Local()} -> {candidates.Count} result(s){(candidates.Count > 0 ? ": " + details : "")}");
        if (candidates.Count >= MaxResults)
        {
            MainFile.Logger.Info($"lobby search hit the cap of {MaxResults} results");
        }
        return candidates;
    }

    // The picker's one wide search for its counts: every open quick-match lobby of this release in the player's mode
    // and voice pool, whatever its size and ascension.
    public static async Task<List<LobbyCandidate>> FindPoolAsync(RunMode mode, bool voice, CancellationToken ct)
    {
        List<LobbyCandidate> found = await SearchAsync(0, mode, voice, ct, 1, nearby: false);
        MainFile.Logger.Info($"pool search {MatchMode.Text(mode)} {MatchVoice.Text(voice)} -> {found.Count} open lobb{(found.Count == 1 ? "y" : "ies")}{(found.Count > 0 ? ": " + string.Join(", ", found.Select(l => $"{l.Id} n={l.Members}/{SizeText(l)} a={l.Ascension}")) : "")}{(found.Count >= MaxResults ? $" (the cap of {MaxResults})" : "")}");
        return found;
    }

    private static string SizeText(LobbyCandidate lobby) => lobby.AnySize ? $"any{lobby.Size}" : lobby.Size.ToString(CultureInfo.InvariantCulture);

    private const int MaxResults = 50;

    // A size of 0 finds every lobby: Any lobbies and each exact size.
    private static async Task<List<LobbyCandidate>> SearchAsync(int size, RunMode mode, bool voice, CancellationToken ct, int minFreeSlots, bool nearby)
    {
        QuickMatchLobby.CompatKey key = QuickMatchLobby.CompatKey.Local();
        var filters = new Dictionary<string, string>
        {
            [LobbyKeys.Proto] = LobbyKeys.ProtoValue,
            [LobbyKeys.Mode] = MatchMode.LobbyValue(mode),
            [LobbyKeys.Voice] = MatchVoice.LobbyValue(voice),
            [LobbyKeys.Version] = key.Version,
            [LobbyKeys.Db] = key.Db,
            [LobbyKeys.Mods] = key.Mods,
        };
        if (size > 0)
        {
            filters[LobbyKeys.Size] = size.ToString(CultureInfo.InvariantCulture);
        }
        var query = new LobbyQuery(
            filters,
            MinFreeSlots: minFreeSlots,
            MaxResults: MaxResults,
            ReadKeys: [LobbyKeys.Members, LobbyKeys.CreatedAt, LobbyKeys.Size, LobbyKeys.Ascension, LobbyKeys.ResumeProto, LobbyKeys.Link],
            Nearby: nearby);

        var found = new List<LobbyCandidate>();
        foreach (LobbyListing listing in await RequestAsync(query, ct) ?? [])
        {
            // A run's resume lobby may still carry the match marker, but it is no group looking for players.
            if (RejoinPolicy.IsResumeListing(listing.Data.GetValueOrDefault(LobbyKeys.ResumeProto))
                || LinkPolicy.HostOffersSockets(listing.Data.GetValueOrDefault(LobbyKeys.Link)) is not bool hostOffersSockets)
            {
                continue;
            }
            LobbyLinks.Remember(listing.Id, hostOffersSockets);
            int members = listing.Members;
            if (members <= 0)
            {
                int.TryParse(listing.Data.GetValueOrDefault(LobbyKeys.Members), NumberStyles.Integer, CultureInfo.InvariantCulture, out members);
            }
            uint.TryParse(listing.Data.GetValueOrDefault(LobbyKeys.CreatedAt), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint createdAt);
            (int lobbySize, bool any) = LobbyKeys.ParseSize(listing.Data.GetValueOrDefault(LobbyKeys.Size));
            int ascension = LobbyKeys.ParseInRange(listing.Data.GetValueOrDefault(LobbyKeys.Ascension), 0, MatchPolicy.MaxAscension, -1);
            found.Add(new LobbyCandidate(listing.Id, members, createdAt, ascension, lobbySize, any, hostOffersSockets));
        }
        return found;
    }


    // Lobbies in which a host has reopened the saved run, less those whose host (as the lobby names it) the run
    // record bans.
    public static async Task<List<ulong>> FindResumeAsync(string runKey, IReadOnlyCollection<ulong> removed, CancellationToken ct)
    {
        LobbyQuery query = ResumeLobby.Query(runKey, QuickMatchLobby.CompatKey.Local().Version);
        var listings = await RequestAsync(query, ct) ?? [];
        var found = new List<ulong>();
        int skipped = 0;
        foreach (LobbyListing listing in listings)
        {
            if (ResumeLobby.Read(listing) is not { } route)
            {
                continue;
            }
            if (RunKickPolicy.SkipsHost(route.Host, removed))
            {
                skipped++;
                continue;
            }
            LobbyLinks.Remember(listing.Id, route.OffersSockets);
            found.Add(listing.Id);
        }
        MainFile.Logger.Info($"resume search key={runKey} -> {found.Count} result(s){(found.Count > 0 ? ": " + string.Join(", ", found) : "")}{(skipped > 0 ? $"; {skipped} hosted by a player voted out of the run, passed by" : "")}");
        return found.OrderByDescending(id => LobbyLinks.OffersSockets(id) == true).ToList();
    }

    // Null on error or timeout.
    private static async Task<IReadOnlyList<LobbyListing>?> RequestAsync(LobbyQuery query, CancellationToken ct)
    {
        using var search = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<IReadOnlyList<LobbyListing>?> request = LobbyBackend.Current.SearchAsync(query, search.Token);
        Task first = await Task.WhenAny(request, GodotDelay.Seconds(TimeoutSeconds, ct));
        ct.ThrowIfCancellationRequested();
        if (first != request)
        {
            search.Cancel();
            MainFile.Logger.Warn($"lobby search timed out after {TimeoutSeconds}s");
            return null;
        }
        try
        {
            return await request;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            MainFile.Logger.Warn($"lobby search failed: {e.Message}");
            return null;
        }
    }
}
