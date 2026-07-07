using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;

/// <summary>
///     An osu! multiplayer match. Ported from app/objects/match.py's Match.
///     Unlike bancho.py, which relies on asyncio's single-threaded event loop to make slot mutations
///     atomic between `await` points, ASP.NET Core dispatches packet handlers on the real thread
///     pool — there is nothing to "port" for synchronization, since the Python source has no lock of
///     any kind. <see cref="Lock" /> is this port's from-scratch answer: callers that read-then-mutate
///     slot state (join, change-slot, part, ...) must hold it for the whole read-mutate-broadcast
///     sequence, exactly mirroring the atomicity asyncio gave Python for free.
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

    /// <summary>
    ///     The persistent database Matches.Id for this room, distinct from <see cref="Id" /> (the
    ///     0-63 in-memory registry slot, which is what the bancho wire protocol actually uses as a
    ///     match id). Set once, right after the room is created, by MatchMembershipService.CreateAsync.
    /// </summary>
    public int DbId { get; set; }

    /// <summary>
    ///     The Rounds.Id of the beatmap currently being played, or null when no round is in progress.
    ///     Set at match start (a new Round row is created per beatmap played) and cleared at
    ///     MatchComplete. Score submissions link to this so score-to-round linking doesn't depend on
    ///     any gather/wait step at MatchComplete — see ScoreSubmissionUseCase's doc comment.
    /// </summary>
    public int? CurrentRoundId { get; set; }

    /// <summary>1-based index of the next Round to be created for this match (incremented at match start).</summary>
    public int NextRoundIndex { get; set; } = 1;

    /// <summary>Ported from Match.url — the match's invitation url.</summary>
    public string Url => $"osump://{Id}/{Password}";

    /// <summary>Ported from Match.embed — an osu! chat embed for this match.</summary>
    public string Embed => $"[{Url} {Name}]";

    public IReadOnlyList<MatchSlot> Slots { get; } = [.. Enumerable.Range(0, 16).Select(_ => new MatchSlot())];

    public IReadOnlyCollection<int> Referees => _referees;

    public IReadOnlyCollection<int> TourneyClients => _tourneyClients;

    public void AddReferee(int playerId)
    {
        _referees.Add(playerId);
    }

    public void RemoveReferee(int playerId)
    {
        _referees.Remove(playerId);
    }

    /// <summary>Ported from Match.refs — referees plus the current host.</summary>
    public bool IsReferee(int playerId)
    {
        return playerId == HostId || _referees.Contains(playerId);
    }

    public void AddTourneyClient(int playerId)
    {
        _tourneyClients.Add(playerId);
    }

    public void RemoveTourneyClient(int playerId)
    {
        _tourneyClients.Remove(playerId);
    }

    /// <summary>Ported from Match.get_slot.</summary>
    public MatchSlot? GetSlot(int playerId)
    {
        return Slots.FirstOrDefault(s => s.PlayerId == playerId);
    }

    /// <summary>Ported from Match.get_slot_id.</summary>
    public int? GetSlotId(int playerId)
    {
        for (var i = 0; i < Slots.Count; i++)
            if (Slots[i].PlayerId == playerId)
                return i;

        return null;
    }

    /// <summary>
    ///     Ported from Match.get_free. THE race: reading this and then occupying the returned slot
    ///     is not atomic — callers MUST hold <see cref="Lock" /> across both steps.
    /// </summary>
    public int? GetFreeSlotId()
    {
        for (var i = 0; i < Slots.Count; i++)
            if (Slots[i].Status == SlotStatus.Open)
                return i;

        return null;
    }

    /// <summary>Ported from Match.get_host_slot.</summary>
    public MatchSlot? GetHostSlot()
    {
        return GetSlot(HostId);
    }

    /// <summary>Ported from Match.unready_players.</summary>
    public void UnreadyPlayers(SlotStatus expected = SlotStatus.Ready)
    {
        foreach (var slot in Slots)
            if (slot.Status == expected)
                slot.Status = SlotStatus.NotReady;
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