using Basil.Application.Services.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Sessions.Multiplayer;

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
    string mapName,
    int mapId,
    string mapMd5,
    int hostId,
    GameMode mode,
    Mods mods,
    MatchWinCondition winCondition,
    MatchTeamType teamType,
    bool freemods,
    int seed,
    string chatChannelName,
    bool createdViaMakeCommand = false)
{
    private readonly HashSet<int> _bannedIds = [];
    private readonly HashSet<int> _invitedIds = [];
    private readonly HashSet<int> _referees = [];
    private readonly HashSet<int> _tourneyClients = [];

    /// <summary>Held for the duration of any read-then-mutate-then-broadcast sequence on this match's slots or settings.</summary>
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public int Id { get; } = id;
    public string Name { get; set; } = name;
    public string Password { get; set; } = password;

    public int HostId { get; set; } = hostId;

    public int MapId { get; set; } = mapId;
    public int PrevMapId { get; set; }
    public string MapMd5 { get; set; } = mapMd5;
    public string MapName { get; set; } = mapName;

    public Mods Mods { get; set; } = mods;
    public GameMode Mode { get; set; } = mode;
    public bool Freemods { get; set; } = freemods;

    public string ChatChannelName { get; } = chatChannelName;

    public MatchTeamType TeamType { get; set; } = teamType;
    public MatchWinCondition WinCondition { get; set; } = winCondition;

    public bool InProgress { get; set; }
    public int Seed { get; } = seed;

    /// <summary>
    ///     Set by `!mp lock`/`!mp unlock` (no slot argument) — blocks new players from joining the
    ///     room entirely, matching real Bancho's room-level lock. Distinct from a slot's own
    ///     <see cref="SlotStatus.Locked" />, which is set by the `MatchLock` wire packet (in-client
    ///     per-slot lock) and by `!mp size` shrinking the room.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    ///     Set by <c>!mp private [0|1]</c> — when <c>true</c>, the room cannot be (re)joined by anyone
    ///     but staff or <see cref="InvitedIds" />, via any path (<c>!mp join</c>, the native client
    ///     join packet, or an <c>osump://</c> URL) — see
    ///     <see cref="MatchMembershipService.Join" />. The host
    ///     is NOT exempt: hosting only grants in-room settings control, not a standing invite, so a
    ///     host who leaves a private room needs a referee's <c>!mp invite</c> like anyone else to get
    ///     back in. Also hidden from <c>#lobby</c>. Distinct from <see cref="IsLocked" />, which blocks
    ///     every non-member outright regardless of invite status. Defaults to <c>false</c> for all new
    ///     rooms. Not persisted to the database — a room that closes while private loses this flag
    ///     (and its invite list).
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    ///     Teardown-strategy marker, not a privacy/history flag: true for rooms made via `!mp make`,
    ///     which persist until `!mp close` or until <see cref="Referees" /> empties out, instead of
    ///     MatchMembershipService.Leave's normal all-slots-empty auto-teardown (which still applies
    ///     unchanged to rooms created the normal way, via the client's `MATCH_CREATE` packet).
    /// </summary>
    public bool CreatedViaMakeCommand { get; } = createdViaMakeCommand;

    /// <summary>
    ///     Non-null while a `!mp start &lt;seconds&gt;`/`!mp timer` countdown is running for this match —
    ///     `!mp aborttimer` cancels it, and it must be cancelled whenever the match itself is torn down
    ///     (see MatchMembershipService.TeardownMatch) so no announcement fires into a dead channel.
    /// </summary>
    public CancellationTokenSource? PendingTimer { get; set; }

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
    ///     any gather/wait step at MatchComplete — see ScoreSubmissionService's doc comment.
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

    public IReadOnlyCollection<int> BannedIds => _bannedIds;

    /// <summary>Players `!mp invite` has been used on — see <see cref="IsPrivate" />'s doc comment.</summary>
    public IReadOnlyCollection<int> InvitedIds => _invitedIds;

    public void AddReferee(int playerId)
    {
        _referees.Add(playerId);
    }

    public void RemoveReferee(int playerId)
    {
        _referees.Remove(playerId);
    }

    public void AddBan(int playerId)
    {
        _bannedIds.Add(playerId);
    }

    public void RemoveBan(int playerId)
    {
        _bannedIds.Remove(playerId);
    }

    public void AddInvite(int playerId)
    {
        _invitedIds.Add(playerId);
    }

    /// <summary>
    ///     Whether `playerId` may issue `!mp` commands on this match. Referee is a pure permission
    ///     flag granted by `!mp addref`/`!mp make` (auto-added at creation) — it does NOT require
    ///     being physically in the room, and the host is not automatically a referee: hosting only
    ///     grants direct in-client settings control (MatchChangeSettings etc.), which ranks below
    ///     referee authority for `!mp` purposes.
    /// </summary>
    public bool IsReferee(int playerId)
    {
        return _referees.Contains(playerId);
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