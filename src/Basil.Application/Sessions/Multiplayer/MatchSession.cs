using System.Collections.Concurrent;
using Basil.Application.Services;
using Basil.Application.Services.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Represents an osu! multiplayer match in memory, holding the room's settings, its live slot
///     state, and the snapshot state that feeds the live event channels.
/// </summary>
/// <remarks>
///     Concurrency model: a read-then-mutate of slot or settings state is not atomic between
///     awaits. <see cref="Lock" /> is the per-match answer: callers that read then mutate slot state
///     (join, change-slot, part, and so on) must hold it across the whole read-mutate-broadcast
///     sequence. The snapshot state on this type is deliberately lock-free instead (see
///     <see cref="SnapshotChannel{T}" />).
/// </remarks>
/// <param name="id">
///     The 0 to 63 registry slot this match occupies, which is what the bancho wire protocol uses as the
///     match id.
/// </param>
/// <param name="name">The room's name.</param>
/// <param name="password">The room's password, used in its invitation url.</param>
/// <param name="mapName">The name of the currently selected beatmap.</param>
/// <param name="mapId">The id of the currently selected beatmap.</param>
/// <param name="mapMd5">The md5 of the currently selected beatmap.</param>
/// <param name="hostId">The id of the userSession hosting the room.</param>
/// <param name="mode">The game mode the room plays in.</param>
/// <param name="mods">The mods applied to the whole room.</param>
/// <param name="winCondition">The condition that decides the winner of a round.</param>
/// <param name="teamType">The team arrangement used for the room.</param>
/// <param name="freemods">A value that indicates whether freemod mode is enabled.</param>
/// <param name="seed">The room's random seed, broadcast to clients as part of the match's data.</param>
/// <param name="chatChannelName">The name of the chat channel bound to this room.</param>
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
	string chatChannelName)
{
	private readonly ConcurrentDictionary<int, byte> _bannedIds = new();
	private readonly ConcurrentDictionary<int, byte> _invitedIds = new();
	private readonly ConcurrentDictionary<int, byte> _referees = new();
	private readonly ConcurrentDictionary<int, byte> _tourneyClients = new();

	/// <summary>The host id a match carries while nobody holds gameplay host.</summary>
	public static int NoHostId => SystemUserIds.BasilBot;

	/// <summary>
	///     Gets the per-match semaphore that serializes all read-then-mutate-then-broadcast
	///     sequences on this match's slots and settings, held for the duration of any such sequence.
	/// </summary>
	public InstrumentedMatchLock Lock { get; } = new();

	/// <summary>
	///     Gets the lock-free full-snapshot and delta state for the match's main live channel. See
	///     <see cref="SnapshotChannel{T}" />'s doc comment for why this is never guarded by
	///     <see cref="Lock" />.
	/// </summary>
	public SnapshotChannel<MatchLiveSnapshot> MainSnapshot { get; } = new();

	/// <summary>Gets the same lock-free full-snapshot and delta state, scoped to the match's settings channel.</summary>
	public SnapshotChannel<MatchSettingsView> SettingsSnapshot { get; } = new();

	/// <summary>
	///     Gets one <see cref="SnapshotChannel{T}" /> per slot, indexed like <see cref="Slots" />,
	///     feeding the per-slot "slot" sub-event channel.
	/// </summary>
	public IReadOnlyList<SnapshotChannel<MatchSlotView>> SlotSnapshots { get; } =
		[.. Enumerable.Range(0, 16).Select(_ => new SnapshotChannel<MatchSlotView>())];

	/// <summary>Gets the same lock-free full-snapshot and delta state, scoped to the match's host channel.</summary>
	public SnapshotChannel<MatchHostView> HostSnapshot { get; } = new();

	/// <summary>Gets the same lock-free full-snapshot and delta state, scoped to the match's referee-list channel.</summary>
	public SnapshotChannel<MatchRefereesView> RefsSnapshot { get; } = new();

	/// <summary>Gets the same lock-free full-snapshot and delta state, scoped to the match's banlist channel.</summary>
	public SnapshotChannel<MatchBansView> BansSnapshot { get; } = new();

	/// <summary>Gets the same lock-free full-snapshot and delta state, scoped to the match's countdown-timer channel.</summary>
	public SnapshotChannel<MatchTimerView> TimerSnapshot { get; } = new();

	/// <summary>
	///     Gets the same lock-free full-snapshot and delta state, scoped to the match's slot channel:
	///     a whole-arrangement dict view, distinct from <see cref="SlotSnapshots" />'s per-index list
	///     (which still feeds the per-slot "slot" sub-event).
	/// </summary>
	public SnapshotChannel<MatchSlotsView> SlotsSnapshot { get; } = new();

	/// <summary>
	///     Gets the 0 to 63 registry slot this match occupies, which is what the bancho wire protocol uses as the match
	///     id.
	/// </summary>
	public int Id { get; } = id;

	/// <summary>Gets or sets the room's name.</summary>
	public string Name { get; set; } = name;

	/// <summary>Gets or sets the room's password, used in its invitation url.</summary>
	public string Password { get; set; } = password;

	/// <summary>Gets or sets the id of the current host. <see cref="NoHostId" /> means no host.</summary>
	public int HostId { get; set; } = hostId;

	/// <summary>Gets a value that indicates whether a player currently holds a gameplay host.</summary>
	public bool HasGameplayHost => HostId != NoHostId;

	/// <summary>Gets or sets the id of the currently selected beatmap.</summary>
	public int MapId { get; set; } = mapId;

	/// <summary>
	///     Gets or sets the id of the beatmap that was selected before the current one, updated whenever the room's map
	///     changes.
	/// </summary>
	// ReSharper disable once UnusedAutoPropertyAccessor.Global
	public int PrevMapId { get; set; }

	/// <summary>Gets or sets the md5 of the currently selected beatmap.</summary>
	public string MapMd5 { get; set; } = mapMd5;

	/// <summary>Gets or sets the name of the currently selected beatmap.</summary>
	public string MapName { get; set; } = mapName;

	/// <summary>
	///     Gets or sets the md5 of the last client-supplied map selection that failed to resolve
	///     locally, or null when none is currently pending. Set when the "Beatmap not found locally"
	///     warning fires, cleared on a successful resolve or an explicit deselecting. Exists solely to
	///     deduplicate that warning: osu! clients resend their full settings snapshot on any
	///     unrelated room-setting change, and without this field the warning would re-fire on every
	///     one of those instead of only the first failed lookup (see MatchChangeSettingsHandler).
	/// </summary>
	public string? UnresolvedMapMd5 { get; set; }

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public Mods Mods { get; set; } = mods;

	/// <summary>Gets or sets the game mode the room plays in.</summary>
	public GameMode Mode { get; set; } = mode;

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods { get; set; } = freemods;

	/// <summary>Gets the name of the chat channel bound to this room.</summary>
	public string ChatChannelName { get; } = chatChannelName;

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public MatchTeamType TeamType { get; set; } = teamType;

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public MatchWinCondition WinCondition { get; set; } = winCondition;

	/// <summary>Gets or sets a value that indicates whether a round is currently being played.</summary>
	public bool InProgress { get; set; }

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	public int Seed { get; } = seed;

	/// <summary>
	///     Gets or sets a value that indicates whether the room is locked against new players
	///     entirely. Set by <c>!mp lock</c> and <c>!mp unlock</c> (without a slot argument),
	///     matching real Bancho's room-level lock. Distinct from a slot's own
	///     <see cref="SlotStatus.Locked" />, which is set by the MatchLock wire packet (an in-client
	///     per-slot lock) and by <c>!mp size</c> shrinking the room.
	/// </summary>
	public bool IsLocked { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the room is private. Set by
	///     <c>!mp private [0|1]</c>; when true, the room cannot be (re)joined by anyone but staff or
	///     <see cref="InvitedIds" />, through any path (<c>!mp join</c>, the native client join
	///     packet, or an <c>osump://</c> url), see <see cref="MatchMembershipService.JoinAsync" />.
	///     The host is not exempt: hosting only grants in-room settings control, not a standing
	///     invite, so a host who leaves a private room needs a referee's <c>!mp invite</c> like
	///     anyone else to get back in. Also hidden from <c>#lobby</c>. Distinct from
	///     <see cref="IsLocked" />, which blocks every non-member outright regardless of invite
	///     status. Defaults to <see langword="false" /> for all new rooms. Not persisted to the
	///     database: a room that closes while private loses this flag, along with its invite list.
	/// </summary>
	public bool IsPrivate { get; set; }

	/// <summary>
	///     Cancels the pending empty-room auto-close, or null when the room currently has at least one
	///     occupied slot. Set whenever the room becomes fully empty and canceled the moment any slot
	///     is occupied again, matching the <see cref="PendingTimer" /> lifecycle pattern.
	/// </summary>
	public CancellationTokenSource? EmptyRoomTimer { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the 60-second warning for the pending
	///     empty-room auto-close has already been announced, so a player joining after that point gets
	///     a "canceled" notice instead of silence.
	/// </summary>
	public bool EmptyRoomWarningSent { get; set; }

	/// <summary>
	///     Gets or sets a non-null source while a <c>!mp start &lt;seconds&gt;</c> or <c>!mp timer</c>
	///     countdown is running for this match. <c>!mp aborttimer</c> cancels it, and it must also be
	///     canceled whenever the match is torn down (see MatchMembershipService.TeardownMatch) so no
	///     announcement fires into a dead channel.
	/// </summary>
	public CancellationTokenSource? PendingTimer { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether <see cref="PendingTimer" /> is a
	///     <c>!mp start &lt;seconds&gt;</c> countdown that will actually start the match when it
	///     reaches zero, as opposed to a plain <c>!mp timer</c> that only announces. A
	///     gameplay-affecting settings change (map, team type, win condition, size, a userSession's team)
	///     cancels only this kind (see MatchMembershipService.CancelQueuedAutoStart).
	/// </summary>
	public bool PendingTimerIsAutoStart { get; set; }

	/// <summary>
	///     Gets or sets the time the current countdown started, set alongside <see cref="PendingTimer" />
	///     and cleared alongside it. Backs the remaining-seconds computation of GET
	///     /matches/{matchId}/timer.
	/// </summary>
	public DateTimeOffset? TimerStartedAt { get; set; }

	/// <summary>
	///     Gets or sets the total length of the current countdown in seconds, set alongside <see cref="PendingTimer" />
	///     and cleared alongside it.
	/// </summary>
	public int? TimerTotalSeconds { get; set; }

	/// <summary>
	///     Gets or sets the persistent database Matches.Id for this room, distinct from
	///     <see cref="Id" /> (the 0 to 63 in-memory registry slot, which is what the bancho wire
	///     protocol actually uses as a match id). Set once, right after the room is created, by
	///     MatchMembershipService.CreateAsync.
	/// </summary>
	public int DbId { get; set; }

	/// <summary>
	///     Gets or sets the id of the userSession who created this match, or null when the room was
	///     created via the HTTP API with no session behind it. Set once, right after the room is
	///     created, by MatchMembershipService.CreateAsync.
	/// </summary>
	public int? CreatorId { get; set; }

	/// <summary>
	///     Gets or sets the Rounds.Id of the beatmap currently being played, or null when no round is
	///     in progress. Set at match start, when a new Round row is created per beatmap played and
	///     cleared at MatchComplete. Score submissions link to this, so score-to-round linking does
	///     not depend on any gather or wait step at MatchComplete (see ScoreSubmissionService).
	/// </summary>
	public int? CurrentRoundId { get; set; }

	/// <summary>Gets or sets the 1-based index of the next Round to be created for this match, incremented at match start.</summary>
	public int NextRoundIndex { get; set; } = 1;

	/// <summary>Gets the room's invitation url.</summary>
	// ReSharper disable once MemberCanBePrivate.Global
	public string Url => $"osump://{Id}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string Embed => $"[{Url} {Name}]";

	/// <summary>Gets the match's 16 slots, in order.</summary>
	public IReadOnlyList<MatchSlot> Slots { get; } = [.. Enumerable.Range(0, 16).Select(_ => new MatchSlot())];

	/// <summary>Gets the ids of the players granted referee authority for this match.</summary>
	public IReadOnlyCollection<int> Referees => (IReadOnlyCollection<int>)_referees.Keys;

	/// <summary>Gets the ids of the tourney-client connections attached to this match.</summary>
	public IReadOnlyCollection<int> TourneyClients => (IReadOnlyCollection<int>)_tourneyClients.Keys;

	/// <summary>Gets the ids of the players banned from this match.</summary>
	public IReadOnlyCollection<int> BannedIds => (IReadOnlyCollection<int>)_bannedIds.Keys;

	/// <summary>Gets the ids of the players a referee has invited via <c>!mp invite</c>, see <see cref="IsPrivate" />.</summary>
	public IReadOnlyCollection<int> InvitedIds => (IReadOnlyCollection<int>)_invitedIds.Keys;

	/// <summary>
	///     Grants referee authority on this match to a userSession.
	/// </summary>
	/// <param name="playerId">The id of the userSession being granted referee authority.</param>
	public void AddReferee(int playerId)
	{
		_referees[playerId] = 0;
	}

	/// <summary>
	///     Revokes referee authority on this match from a userSession.
	/// </summary>
	/// <param name="playerId">The id of the userSession whose referee authority is being revoked.</param>
	public void RemoveReferee(int playerId)
	{
		_referees.TryRemove(playerId, out _);
	}

	/// <summary>
	///     Adds a userSession to this match's banlist, blocking them from joining.
	/// </summary>
	/// <param name="playerId">The id of the userSession being banned.</param>
	public void AddBan(int playerId)
	{
		_bannedIds[playerId] = 0;
	}

	/// <summary>
	///     Removes a userSession from this match's banlist.
	/// </summary>
	/// <param name="playerId">The id of the userSession being unbanned.</param>
	public void RemoveBan(int playerId)
	{
		_bannedIds.TryRemove(playerId, out _);
	}

	/// <summary>
	///     Adds a userSession to this match's invite list, letting them join a private room.
	/// </summary>
	/// <param name="playerId">The id of the userSession being invited.</param>
	public void AddInvite(int playerId)
	{
		_invitedIds[playerId] = 0;
	}

	/// <summary>
	///     Gets a value that indicates whether <paramref name="playerId" /> may issue <c>!mp</c>
	///     commands on this match.
	/// </summary>
	/// <remarks>
	///     Referee is a pure permission flag granted by <c>!mp addref</c> or <c>!mp make</c> (which
	///     auto-adds one at creation). It does not require being physically in the room, and the host
	///     is not automatically a referee: hosting only grants direct in-client settings control,
	///     which ranks below referee authority for <c>!mp</c> purposes. Kept by userSession id, exactly
	///     like <see cref="BannedIds" />, so it never depends on any live session: disconnecting,
	///     logging out, or leaving the room's slots leaves it untouched. Only <c>!mp removeref</c> (or
	///     the referee-removal API) revokes it, and both refuse to remove a match's last referee or the
	///     room's creator (see <see cref="IsCreator" />) — a room can be emptied of every player
	///     without ever losing referee control. The creator additionally counts as a referee here
	///     unconditionally, even on the rare match where they were somehow never granted the flag
	///     itself, so <c>!mp</c> authority never depends on that grant having happened.
	/// </remarks>
	/// <param name="playerId">The id of the userSession to check.</param>
	/// <returns><see langword="true" /> if the userSession is a referee; otherwise, <see langword="false" />.</returns>
	public bool IsReferee(int playerId)
	{
		return _referees.ContainsKey(playerId) || IsCreator(playerId);
	}

	/// <summary>Gets a value that indicates whether <paramref name="playerId" /> created this match.</summary>
	/// <remarks>
	///     The creator holds full <c>!mp</c> authority for the room's lifetime regardless of referee
	///     status (see <see cref="IsReferee" />) and can never be stripped of it: <c>!mp addref</c>/
	///     <c>!mp removeref</c> are restricted to the creator alone, and removing the creator from
	///     <see cref="Referees" /> — by name or by a full-list replace — is always refused.
	/// </remarks>
	/// <param name="playerId">The id of the userSession to check.</param>
	/// <returns><see langword="true" /> if the userSession created this match; otherwise, <see langword="false" />.</returns>
	public bool IsCreator(int playerId)
	{
		return CreatorId == playerId;
	}

	/// <summary>
	///     Adds a userSession to this match's tourney-client set, marking their connection as a
	///     tournament client.
	/// </summary>
	/// <param name="playerId">The id of the userSession whose connection is a tourney client.</param>
	public void AddTourneyClient(int playerId)
	{
		_tourneyClients[playerId] = 0;
	}

	/// <summary>
	///     Removes a userSession from this match's tourney-client set.
	/// </summary>
	/// <param name="playerId">The id of the userSession whose tourney-client connection is being removed.</param>
	public void RemoveTourneyClient(int playerId)
	{
		_tourneyClients.TryRemove(playerId, out _);
	}

	/// <summary>
	///     Gets the slot currently occupied by <paramref name="playerId" />, or null when the userSession
	///     is not in the match.
	/// </summary>
	/// <param name="playerId">The id of the userSession to look up.</param>
	/// <returns>The userSession's slot, or null when the userSession is not in the match.</returns>
	public MatchSlot? GetSlot(int playerId)
	{
		return Slots.FirstOrDefault(s => s.PlayerId == playerId);
	}

	/// <summary>
	///     Gets the index of the slot currently occupied by <paramref name="playerId" />, or null
	///     when the userSession is not in the match.
	/// </summary>
	/// <param name="playerId">The id of the userSession to look up.</param>
	/// <returns>The userSession's slot index, or null when the userSession is not in the match.</returns>
	public int? GetSlotId(int playerId)
	{
		for (var i = 0; i < Slots.Count; i++)
			if (Slots[i].PlayerId == playerId)
				return i;
		return null;
	}

	/// <summary>
	///     Gets the index of the first slot with <see cref="SlotStatus.Open" />, or null when every
	///     slot is occupied.
	/// </summary>
	/// <remarks>
	///     Reading this and then occupying the returned slot is not atomic: callers must hold
	///     <see cref="Lock" /> across both steps.
	/// </remarks>
	/// <returns>The index of a free slot, or null when all slots are occupied.</returns>
	public int? GetFreeSlotId()
	{
		for (var i = 0; i < Slots.Count; i++)
			if (Slots[i].Status == SlotStatus.Open)
				return i;
		return null;
	}

	/// <summary>
	///     Gets the slot currently occupied by the host, or null when the host is not in the match.
	/// </summary>
	/// <returns>The host's slot, or null when the host is not in the match.</returns>
	public MatchSlot? GetHostSlot()
	{
		return GetSlot(HostId);
	}

	/// <summary>
	///     Sets every slot whose status equals <paramref name="expected" /> to
	///     <see cref="SlotStatus.NotReady" />, used to unready all ready players when a round restarts.
	/// </summary>
	/// <param name="expected">The status to reset, defaulting to ready.</param>
	public void UnreadyPlayers(SlotStatus expected = SlotStatus.Ready)
	{
		foreach (var slot in Slots)
			if (slot.Status == expected)
				slot.Status = SlotStatus.NotReady;
	}

	/// <summary>
	///     Clears the loaded and skipped flags on every slot, called at the start of a round so the
	///     match can wait for each userSession's load and skip again.
	/// </summary>
	public void ResetPlayersLoadedStatus()
	{
		foreach (var slot in Slots)
		{
			slot.Loaded = false;
			slot.Skipped = false;
		}
	}
}