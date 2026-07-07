using Bancho.Domain;

namespace Bancho.Application.Sessions;

/// <summary>
/// An osu! multiplayer match. Ported from app/objects/match.py's Match.
///
/// Unlike bancho.py, which relies on asyncio's single-threaded event loop to make slot mutations
/// atomic between `await` points, ASP.NET Core dispatches packet handlers on the real thread
/// pool — there is nothing to "port" for synchronization, since the Python source has no lock of
/// any kind. <see cref="Lock"/> is this port's from-scratch answer: callers that read-then-mutate
/// slot state (join, change-slot, part, ...) must hold it for the whole read-mutate-broadcast
/// sequence, exactly mirroring the atomicity asyncio gave Python for free. The one place that
/// must NOT hold it across an await is scrim's await_submissions poll (up to 10s), run by
/// MatchScoringService after MATCH_COMPLETE's lock is released, re-acquiring it only briefly at
/// the end to record the point.
/// </summary>
public sealed class MatchSession(
    int id,
    string name,
    string password,
    bool hasPublicHistory,
    string mapName,
    int mapId,
    string mapMd5,
    int hostId,
    GameMode mode,
    Mods mods,
    MatchWinConditions winCondition,
    MatchTeamTypes teamType,
    bool freemods,
    int seed,
    string chatChannelName)
{
    private readonly HashSet<int> _referees = [];
    private readonly HashSet<int> _tourneyClients = [];
    private readonly Dictionary<ScrimParticipant, int> _matchPoints = [];
    private readonly HashSet<(Mods Mods, int MapId)> _bans = [];
    private readonly List<ScrimParticipant?> _winners = [];

    /// <summary>Held for the duration of any read-then-mutate-then-broadcast sequence on this match's slots or settings.</summary>
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public int Id { get; } = id;
    public string Name { get; set; } = name;
    public string Password { get; set; } = password;
    public bool HasPublicHistory { get; } = hasPublicHistory;

    public int HostId { get; set; } = hostId;

    public int MapId { get; set; } = mapId;
    public int PrevMapId { get; set; }
    public string MapMd5 { get; set; } = mapMd5;
    public string MapName { get; set; } = mapName;

    public Mods Mods { get; set; } = mods;
    public GameMode Mode { get; set; } = mode;
    public bool Freemods { get; set; } = freemods;

    public string ChatChannelName { get; } = chatChannelName;

    public MatchTeamTypes TeamType { get; set; } = teamType;
    public MatchWinConditions WinCondition { get; set; } = winCondition;

    public bool InProgress { get; set; }
    public int Seed { get; } = seed;

    public bool IsScrimming { get; set; }
    public int WinningPoints { get; set; }

    /// <summary>Ported from Match.match_points — accumulated scrim points per team or player.</summary>
    public IReadOnlyDictionary<ScrimParticipant, int> MatchPoints => _matchPoints;

    public int GetMatchPoints(ScrimParticipant participant) => _matchPoints.GetValueOrDefault(participant);

    public void AddMatchPoint(ScrimParticipant participant) => _matchPoints[participant] = GetMatchPoints(participant) + 1;

    /// <summary>Ported from mp_rematch's `match.match_points[recent_winner] -= 1` — rolls back the most recent point.</summary>
    public void DecrementMatchPoint(ScrimParticipant participant) => _matchPoints[participant] = GetMatchPoints(participant) - 1;

    /// <summary>Ported from Match.bans — (mods, map id) pairs banned for the current scrim.</summary>
    public IReadOnlyCollection<(Mods Mods, int MapId)> Bans => _bans;

    public void AddBan(Mods mods, int mapId) => _bans.Add((mods, mapId));

    /// <summary>Ported from Match.winners — one entry per completed point; null means a tie.</summary>
    public IReadOnlyList<ScrimParticipant?> Winners => _winners;

    public void RecordWinner(ScrimParticipant? winner) => _winners.Add(winner);

    /// <summary>Ported from mp_rematch's `match.winners.pop()` — removes and returns the most recently recorded point.</summary>
    public ScrimParticipant? PopLastWinner()
    {
        var last = _winners[^1];
        _winners.RemoveAt(_winners.Count - 1);
        return last;
    }

    /// <summary>Ported from Match.reset_scrim — clears match points, winners, and bans (not IsScrimming itself).</summary>
    public void ResetScrim()
    {
        _matchPoints.Clear();
        _winners.Clear();
        _bans.Clear();
    }

    /// <summary>Ported from Match.url — the match's invitation url.</summary>
    public string Url => $"osump://{Id}/{Password}";

    /// <summary>Ported from Match.embed — an osu! chat embed for this match.</summary>
    public string Embed => $"[{Url} {Name}]";

    public IReadOnlyList<MatchSlot> Slots { get; } = [.. Enumerable.Range(0, 16).Select(_ => new MatchSlot())];

    public IReadOnlyCollection<int> Referees => _referees;

    public void AddReferee(int playerId) => _referees.Add(playerId);

    public void RemoveReferee(int playerId) => _referees.Remove(playerId);

    /// <summary>Ported from Match.refs — referees plus the current host.</summary>
    public bool IsReferee(int playerId) => playerId == HostId || _referees.Contains(playerId);

    public IReadOnlyCollection<int> TourneyClients => _tourneyClients;

    public void AddTourneyClient(int playerId) => _tourneyClients.Add(playerId);

    public void RemoveTourneyClient(int playerId) => _tourneyClients.Remove(playerId);

    /// <summary>Ported from Match.get_slot.</summary>
    public MatchSlot? GetSlot(int playerId) => Slots.FirstOrDefault(s => s.PlayerId == playerId);

    /// <summary>Ported from Match.get_slot_id.</summary>
    public int? GetSlotId(int playerId)
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].PlayerId == playerId)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Ported from Match.get_free. THE race: reading this and then occupying the returned slot
    /// is not atomic — callers MUST hold <see cref="Lock"/> across both steps.
    /// </summary>
    public int? GetFreeSlotId()
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Status == SlotStatus.Open)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Ported from Match.get_host_slot.</summary>
    public MatchSlot? GetHostSlot() => GetSlot(HostId);

    /// <summary>Ported from Match.unready_players.</summary>
    public void UnreadyPlayers(SlotStatus expected = SlotStatus.Ready)
    {
        foreach (var slot in Slots)
        {
            if (slot.Status == expected)
            {
                slot.Status = SlotStatus.NotReady;
            }
        }
    }

    /// <summary>Ported from Match.reset_players_loaded_status.</summary>
    public void ResetPlayersLoadedStatus()
    {
        foreach (var slot in Slots)
        {
            slot.Loaded = false;
            slot.Skipped = false;
        }
    }
}
