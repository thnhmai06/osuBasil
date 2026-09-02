using System.Text.Json.Serialization;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;

// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Builds the lightweight, in-memory-only payloads pushed over the live event channels
///     (<see cref="IMatchLiveEvents" />).
/// </summary>
/// <remarks>
///     These payloads are deliberately cheaper than <see cref="MatchReportService" />'s DB-backed
///     <c>MatchReport</c>, since they fire on every state-changing packet (join, ready, slot change,
///     and so on), not on demand. Consequently, the main channel carries live slot, map, and state
///     only, not aggregated team scores or a round winner. Every user reference is resolved via
///     <see cref="UserBriefResolver" /> (fast for online players, falling back to the user
///     repository for offline ones), which is why every builder that can reference a user is async.
/// </remarks>
public static class MatchLiveSnapshotBuilder
{
	/// <summary>Builds the full live snapshot payload for the match's main live channel.</summary>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="gameRegistry">The registry used to resolve online <see cref="GameSession" /> players.</param>
	/// <param name="ircRegistry">The registry used to resolve online <see cref="IrcSession" /> players.</param>
	/// <param name="users">The repository used to resolve offline players.</param>
	/// <param name="beatmaps">The repository used to resolve the assigned beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the user and beatmap lookups.</param>
	/// <returns>The <see cref="MatchLiveSnapshot" />, including host, referees, beatmap, and slots.</returns>
	public static async Task<MatchLiveSnapshot> BuildMain(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps, CancellationToken cancellationToken = default)
	{
		var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);

		var host = match.HostId != MatchSession.NoHostId // has real host
			? await ResolveOrPlaceholder(match.HostId, gameRegistry, ircRegistry, users, cancellationToken)
			: null;

		var referees = new List<UserBrief>();
		foreach (var id in match.Referees)
			referees.Add(await ResolveOrPlaceholder(id, gameRegistry, ircRegistry, users, cancellationToken));

		var slots = await BuildSlotViews(match, gameRegistry, ircRegistry, users, cancellationToken);
		var beatmap = await ResolveBeatmapAsync(match.MapMd5, beatmaps, cancellationToken);

		return new MatchLiveSnapshot(
			match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
			match.MapId, match.Mods, match.Freemods, match.TeamType, match.WinCondition, match.Mode,
			match.InProgress, host, referees, beatmap, slots);
	}

	/// <summary>Builds the room-configuration payload reused by match list items and the report's live field.</summary>
	/// <remarks>
	///     Both <c>GET /matches</c> list items and <see cref="MatchReportService" />'s <c>Live</c>
	///     field use this shape: room configuration plus the current map and in-progress flag. Host,
	///     referees, and slots are left out, since they are membership details, not configuration.
	/// </remarks>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="beatmaps">The repository used to resolve the assigned beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the beatmap lookup.</param>
	/// <returns>The <see cref="MatchRoomLive" /> payload.</returns>
	public static async Task<MatchRoomLive> BuildRoomLive(MatchSession match, IBeatmapRepository beatmaps,
		CancellationToken cancellationToken = default)
	{
		var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);
		var beatmap = await ResolveBeatmapAsync(match.MapMd5, beatmaps, cancellationToken);

		return new MatchRoomLive(
			!string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
			match.MapId, match.Mods, match.Freemods, match.TeamType, match.WinCondition, match.Mode,
			match.InProgress, beatmap);
	}

	/// <summary>Builds the per-userSession live score payload for the SSE <c>/match/{id}/{playerName}</c> channel.</summary>
	/// <param name="userSession">The userSession whose score frame to broadcast.</param>
	/// <param name="frame">The decoded score frame from the client.</param>
	/// <returns>The <see cref="PlayerLiveScore" /> payload.</returns>
	public static PlayerLiveScore BuildPlayerScore(UserSession userSession, ScoreFrame frame)
	{
		return new PlayerLiveScore(
			new UserBrief(userSession.Id, userSession.Name, userSession.Country),
			frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
			frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp,
			frame.ScoreV2);
	}

	/// <summary>Builds the settings payload for the SSE <c>/match/{id}/settings</c> channel and for settings writes.</summary>
	/// <remarks>
	///     This is the <c>api.</c> host's <c>/match/{id}/settings</c> payload shape. It never exposes
	///     the raw <see cref="MatchSession.Password" />, only whether one is set, even for an
	///     admin-elevated caller; a public, unauthenticated SSE channel is not the place to leak it.
	/// </remarks>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="gameRegistry">The registry used to resolve online <see cref="GameSession" /> players.</param>
	/// <param name="ircRegistry">The registry used to resolve online <see cref="IrcSession" /> players.</param>
	/// <param name="users">The repository used to resolve offline players.</param>
	/// <param name="beatmaps">The repository used to resolve the assigned beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the user and beatmap lookups.</param>
	/// <returns>The <see cref="MatchSettingsView" /> payload, including host and referees.</returns>
	public static async Task<MatchSettingsView> BuildSettings(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps, CancellationToken cancellationToken = default)
	{
		var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);

		var host = match.HostId != BotBootstrapService.BotId // a real host: id 0 means none
			? await ResolveOrPlaceholder(match.HostId, gameRegistry, ircRegistry, users, cancellationToken)
			: null;

		var referees = new List<UserBrief>();
		foreach (var id in match.Referees)
			referees.Add(await ResolveOrPlaceholder(id, gameRegistry, ircRegistry, users, cancellationToken));

		var beatmap = await ResolveBeatmapAsync(match.MapMd5, beatmaps, cancellationToken);

		return new MatchSettingsView(
			match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
			match.MapId, match.Mods, match.Freemods, match.TeamType, match.WinCondition, match.Mode,
			host, referees, beatmap);
	}

	/// <summary>Builds the host payload for <c>GET /matches/{matchId}/hosts</c>.</summary>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="gameRegistry">The registry used to resolve an online <see cref="GameSession" /> host.</param>
	/// <param name="ircRegistry">The registry used to resolve an online <see cref="IrcSession" /> host.</param>
	/// <param name="users">The repository used to resolve an offline host.</param>
	/// <param name="cancellationToken">A token that cancels the user lookup.</param>
	/// <returns>
	///     The <see cref="MatchHostView" /> payload, whose <c>Host</c> is <see langword="null" /> when the room has no
	///     host.
	/// </returns>
	public static async Task<MatchHostView> BuildHost(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		if (match.HostId == 0) return new MatchHostView(null);

		var host = await ResolveOrPlaceholder(match.HostId, gameRegistry, ircRegistry, users, cancellationToken);
		return new MatchHostView(host);
	}

	/// <summary>Builds the referee list payload for <c>GET /matches/{matchId}/refs</c>.</summary>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="gameRegistry">The registry used to resolve online <see cref="GameSession" /> referees.</param>
	/// <param name="ircRegistry">The registry used to resolve online <see cref="IrcSession" /> referees.</param>
	/// <param name="users">The repository used to resolve offline referees.</param>
	/// <param name="cancellationToken">A token that cancels the user lookups.</param>
	/// <returns>The <see cref="MatchRefereesView" /> payload.</returns>
	public static async Task<MatchRefereesView> BuildRefs(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		var referees = new List<UserBrief>();
		foreach (var id in match.Referees)
			referees.Add(await ResolveOrPlaceholder(id, gameRegistry, ircRegistry, users, cancellationToken));

		return new MatchRefereesView(referees);
	}

	/// <summary>Builds the banlist payload for <c>GET /matches/{matchId}/ban</c>.</summary>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="gameRegistry">The registry used to resolve online <see cref="GameSession" /> banned players.</param>
	/// <param name="ircRegistry">The registry used to resolve online <see cref="IrcSession" /> banned players.</param>
	/// <param name="users">The repository used to resolve offline banned players.</param>
	/// <param name="cancellationToken">A token that cancels the user lookups.</param>
	/// <returns>The <see cref="MatchBansView" /> payload.</returns>
	public static async Task<MatchBansView> BuildBans(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		var banned = new List<UserBrief>();
		foreach (var id in match.BannedIds)
			banned.Add(await ResolveOrPlaceholder(id, gameRegistry, ircRegistry, users, cancellationToken));

		return new MatchBansView(banned);
	}

	/// <summary>Builds the countdown timer payload for <c>GET /matches/{matchId}/timer</c>.</summary>
	/// <param name="match">The match whose timer to read.</param>
	/// <returns>The <see cref="MatchTimerView" /> payload, with <c>Running</c> false when no countdown is pending.</returns>
	public static MatchTimerView BuildTimer(MatchSession match)
	{
		if (match.PendingTimer is null || match.TimerStartedAt is null || match.TimerTotalSeconds is null)
			return new MatchTimerView(false, null, false);

		var elapsed = (DateTimeOffset.UtcNow - match.TimerStartedAt.Value).TotalSeconds;
		var remaining = Math.Max(0, match.TimerTotalSeconds.Value - (int)elapsed);
		return new MatchTimerView(true, remaining, match.PendingTimerIsAutoStart);
	}

	/// <summary>Builds the full 16-slot view payload for <c>GET/PUT/PATCH /matches/{matchId}/slots</c>.</summary>
	/// <param name="match">The match being snapshotted.</param>
	/// <param name="gameRegistry">The registry used to resolve online <see cref="GameSession" /> occupants.</param>
	/// <param name="ircRegistry">The registry used to resolve online <see cref="IrcSession" /> occupants.</param>
	/// <param name="users">The repository used to resolve offline occupants.</param>
	/// <param name="cancellationToken">A token that cancels the user lookups.</param>
	/// <returns>The <see cref="MatchSlotsView" /> payload.</returns>
	public static async Task<MatchSlotsView> BuildSlots(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		return new MatchSlotsView(await BuildSlotViews(match, gameRegistry, ircRegistry, users, cancellationToken));
	}

	/// <summary>Builds the per-slot view list shared by the main snapshot and the slots view.</summary>
	/// <param name="match">The match to build slot views for.</param>
	/// <param name="gameRegistry">The registry used to resolve online <see cref="GameSession" /> occupants.</param>
	/// <param name="ircRegistry">The registry used to resolve online <see cref="IrcSession" /> occupants.</param>
	/// <param name="users">The repository used to resolve offline occupants.</param>
	/// <param name="cancellationToken">A token that cancels the user lookups.</param>
	/// <returns>One <see cref="MatchSlotView" /> per slot, index-aligned with <see cref="MatchSession.Slots" />.</returns>
	private static async Task<IReadOnlyList<MatchSlotView>> BuildSlotViews(MatchSession match,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, CancellationToken cancellationToken)
	{
		var slots = new List<MatchSlotView>(match.Slots.Count);
		for (var i = 0; i < match.Slots.Count; i++)
		{
			var slot = match.Slots[i];
			var user = slot.PlayerId is { } pid
				? await ResolveOrPlaceholder(pid, gameRegistry, ircRegistry, users, cancellationToken)
				: null;
			slots.Add(user is null
				? new MatchSlotView(i, null, slot.Status, null, null, null, null)
				: new MatchSlotView(i, user, slot.Status, slot.Team, slot.Mods,
					slot.Status == SlotStatus.Ready, slot.Loaded));
		}

		return slots;
	}

	/// <summary>
	///     Resolves a beatmap md5 into the <see cref="BeatmapDetail" /> embed used by every beatmap reference in these
	///     payloads.
	/// </summary>
	/// <remarks>
	///     A blank md5 means "no beatmap assigned yet" (a freshly created empty room), which never
	///     warrants a repository round-trip and must not be confused with a stale or unresolvable md5.
	///     Both end up <see langword="null" />, but only the latter is an actual lookup miss.
	/// </remarks>
	/// <param name="mapMd5">The stored beatmap md5, or an empty string when nothing is assigned.</param>
	/// <param name="beatmaps">The repository used to resolve the beatmap and its siblings.</param>
	/// <param name="cancellationToken">A token that cancels the beatmap lookups.</param>
	/// <returns>
	///     The <see cref="BeatmapDetail" /> for the md5, or <see langword="null" /> when the md5 is blank or no longer
	///     resolves.
	/// </returns>
	public static async Task<BeatmapDetail?> ResolveBeatmapAsync(string mapMd5, IBeatmapRepository beatmaps,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(mapMd5)) return null;

		var beatmap =
			await beatmaps.FetchOneAsync(md5: mapMd5, includePrivate: true, cancellationToken: cancellationToken);
		if (beatmap is null) return null;

		var siblings = await beatmaps.FetchAllBySetIdAsync(beatmap.Beatmapset.Id, true,
			cancellationToken);
		var beatmapset = beatmap.Beatmapset.ToSummary(siblings.Count);
		return beatmap.ToDetail(beatmapset);
	}

	/// <summary>Resolves a user id to a brief, substituting a placeholder when the id is unknown.</summary>
	/// <remarks>
	///     Every host, referee, ban, and slot-occupant id embedded in these payloads is, by definition,
	///     actually assigned or referenced. Unlike <see cref="UserBriefResolver.ResolveAsync" />'s
	///     plain <see langword="null" /> (which the caller uses for genuine structural absence, for
	///     example, host id 0 or an empty slot), a resolution failure here must never silently shrink
	///     a list or get confused with "nothing is assigned". It falls back to a placeholder that
	///     still carries the real id.
	/// </remarks>
	/// <param name="userId">The user id to resolve.</param>
	/// <param name="gameRegistry">The registry used to resolve an online <see cref="GameSession" />.</param>
	/// <param name="ircRegistry">The registry used to resolve an online <see cref="IrcSession" />.</param>
	/// <param name="users">The repository used to resolve offline players.</param>
	/// <param name="cancellationToken">A token that cancels the offline lookup.</param>
	/// <returns>A <see cref="UserBrief" /> for the id, or a placeholder carrying the id when neither source knows it.</returns>
	public static async Task<UserBrief> ResolveOrPlaceholder(int userId,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, CancellationToken cancellationToken)
	{
		return await UserBriefResolver.ResolveAsync(userId, gameRegistry, ircRegistry, users, cancellationToken)
		       ?? new UserBrief(userId, "Unknown", Country.Xx);
	}
}

/// <summary>
///     A user reference embedded in the match routes and SSE payloads.
/// </summary>
/// <remarks>
///     This <c>{id, name, country}</c> embed is reused for every user reference across these routes
///     and SSE payloads (settings host and referees, hosts/refs/bans views, slot occupants, match
///     report actors, and spectate streams). <see cref="Country" /> serializes to its lowercase
///     2-letter acronym on the wire.
/// </remarks>
/// <param name="Id">The user's id.</param>
/// <param name="Name">The user's name.</param>
/// <param name="Country">The user's country.</param>
public sealed record UserBrief(int Id, string Name, Country Country);

/// <summary>
///     Configuration fields shared by every match room shape.
/// </summary>
/// <remarks>
///     The shape carries no membership (host, referees, or slots) and no rounds. <see cref="MapId" />
///     is <see langword="null" /> in the same cases <see cref="Beatmap" /> (on each derived record) is
///     -- no beatmap chosen, or the chosen one no longer resolves on this server.
/// </remarks>
/// <param name="Id">The match's id.</param>
/// <param name="Name">The room name.</param>
/// <param name="HasPassword">Whether the room has a password set.</param>
/// <param name="IsPrivate">Whether the room is private.</param>
/// <param name="IsLocked">Whether the room blocks new joins.</param>
/// <param name="Size">The number of open slots.</param>
/// <param name="MapId">The assigned beatmap id, or <see langword="null" /> when none is chosen.</param>
/// <param name="Mods">The room-level mods.</param>
/// <param name="Freemod">Whether the room is in freemod mode.</param>
/// <param name="TeamType">The room's team type.</param>
/// <param name="WinCondition">The room's win condition.</param>
/// <param name="Mode">The room's game mode.</param>
public abstract record MatchRoomCore(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int? MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	GameMode Mode);

/// <summary>The <c>live</c> object embedded in a <c>GET /matches</c> list item and in a match report.</summary>
/// <remarks>
///     Carries room configuration plus the current map and in-progress flag, with no slots, host,
///     referees, or rounds. Deliberately omits <c>id</c>/<c>name</c> — every place this is embedded
///     (<see cref="MatchListItem" />, <see cref="MatchReport" />) already carries
///     those at its own top level, so repeating them here would be redundant.
/// </remarks>
/// <param name="HasPassword">Whether the room has a password set.</param>
/// <param name="IsPrivate">Whether the room is private.</param>
/// <param name="IsLocked">Whether the room blocks new joins.</param>
/// <param name="Size">The number of open slots.</param>
/// <param name="MapId">The assigned beatmap id, or <see langword="null" /> when none is chosen.</param>
/// <param name="Mods">The room-level mods.</param>
/// <param name="Freemod">Whether the room is in freemod mode.</param>
/// <param name="TeamType">The room's team type.</param>
/// <param name="WinCondition">The room's win condition.</param>
/// <param name="Mode">The room's game mode.</param>
/// <param name="InProgress">Whether the room is currently mid-round.</param>
/// <param name="Beatmap">
///     The resolved assigned beatmap, or <see langword="null" /> when none is assigned or it no longer
///     resolves.
/// </param>
public sealed record MatchRoomLive(
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int? MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	GameMode Mode,
	bool InProgress,
	BeatmapDetail? Beatmap);

/// <summary>
///     The payload for the SSE <c>/match/{id}/settings</c> channel and the response body for
///     settings writes.
/// </summary>
/// <remarks>Carries host and referees because this resource controls membership, not just configuration.</remarks>
/// <param name="Id">The match's id.</param>
/// <param name="Name">The room name.</param>
/// <param name="HasPassword">Whether the room has a password set.</param>
/// <param name="IsPrivate">Whether the room is private.</param>
/// <param name="IsLocked">Whether the room blocks new joins.</param>
/// <param name="Size">The number of open slots.</param>
/// <param name="MapId">The assigned beatmap id, or <see langword="null" /> when none is chosen.</param>
/// <param name="Mods">The room-level mods.</param>
/// <param name="Freemod">Whether the room is in freemod mode.</param>
/// <param name="TeamType">The room's team type.</param>
/// <param name="WinCondition">The room's win condition.</param>
/// <param name="Mode">The room's game mode.</param>
/// <param name="Host">The resolved host, or <see langword="null" /> when the room has no host.</param>
/// <param name="Referees">The resolved referee list.</param>
/// <param name="Beatmap">
///     The resolved assigned beatmap, or <see langword="null" /> when none is assigned or it no longer
///     resolves.
/// </param>
public sealed record MatchSettingsView(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int? MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	GameMode Mode,
	UserBrief? Host,
	IReadOnlyList<UserBrief> Referees,
	BeatmapDetail? Beatmap)
	: MatchRoomCore(Id, Name, HasPassword, IsPrivate, IsLocked, Size, MapId, Mods, Freemod, TeamType, WinCondition,
		Mode);

/// <summary>The payload for the SSE <c>/match/{id}</c> main channel: the full live snapshot, including slots.</summary>
/// <param name="Id">The match's id.</param>
/// <param name="Name">The room name.</param>
/// <param name="HasPassword">Whether the room has a password set.</param>
/// <param name="IsPrivate">Whether the room is private.</param>
/// <param name="IsLocked">Whether the room blocks new joins.</param>
/// <param name="Size">The number of open slots.</param>
/// <param name="MapId">The assigned beatmap id, or <see langword="null" /> when none is chosen.</param>
/// <param name="Mods">The room-level mods.</param>
/// <param name="Freemod">Whether the room is in freemod mode.</param>
/// <param name="TeamType">The room's team type.</param>
/// <param name="WinCondition">The room's win condition.</param>
/// <param name="Mode">The room's game mode.</param>
/// <param name="InProgress">Whether the room is currently mid-round.</param>
/// <param name="Host">The resolved host, or <see langword="null" /> when the room has no host.</param>
/// <param name="Referees">The resolved referee list.</param>
/// <param name="Beatmap">
///     The resolved assigned beatmap, or <see langword="null" /> when none is assigned or it no longer
///     resolves.
/// </param>
/// <param name="Slots">The resolved slot views.</param>
public sealed record MatchLiveSnapshot(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int? MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	GameMode Mode,
	bool InProgress,
	UserBrief? Host,
	IReadOnlyList<UserBrief> Referees,
	BeatmapDetail? Beatmap,
	IReadOnlyList<MatchSlotView> Slots)
	: MatchRoomCore(Id, Name, HasPassword, IsPrivate, IsLocked, Size, MapId, Mods, Freemod, TeamType, WinCondition,
		Mode);

/// <summary>The payload for <c>GET /matches/{matchId}/hosts</c>.</summary>
/// <param name="Host">The resolved host, or <see langword="null" /> when the room has no host (id 0).</param>
public sealed record MatchHostView(UserBrief? Host);

/// <summary>The payload for <c>GET /matches/{matchId}/refs</c>.</summary>
/// <param name="Referees">The resolved referee list.</param>
public sealed record MatchRefereesView(IReadOnlyList<UserBrief> Referees);

/// <summary>The payload for <c>GET /matches/{matchId}/ban</c>.</summary>
/// <param name="BannedUsers">The resolved banlist.</param>
public sealed record MatchBansView(IReadOnlyList<UserBrief> BannedUsers);

/// <summary>The payload for <c>GET /matches/{matchId}/timer</c>.</summary>
/// <param name="Running">Whether a countdown is currently pending.</param>
/// <param name="SecondsRemaining">The whole seconds remaining, or <see langword="null" /> when no countdown is running.</param>
/// <param name="AutoStart">Whether the pending countdown will start the match at zero.</param>
public sealed record MatchTimerView(bool Running, int? SecondsRemaining, bool AutoStart);

/// <summary>One slot in <c>GET /matches/{matchId}/slots</c>.</summary>
/// <remarks>
///     <see cref="Status" />, <see cref="Team" />, and <see cref="Mods" /> serialize as their numeric
///     enum values. <see cref="Ready" /> is true exactly when <see cref="Status" /> is
///     <see cref="SlotStatus.Ready" />. <see cref="Team" />, <see cref="Mods" />, <see cref="Ready" />,
///     and <see cref="Loaded" /> are occupant state and only meaningful with one present; each is
///     omitted from the JSON entirely (not merely <see langword="null" />) on an empty slot.
///     <see cref="Status" /> is not one of them -- an empty slot is still meaningfully
///     <see cref="SlotStatus.Open" /> or <see cref="SlotStatus.Locked" />, so it always serializes.
/// </remarks>
/// <param name="Index">The 0-based slot index.</param>
/// <param name="User">The occupant, or <see langword="null" /> for an empty slot.</param>
/// <param name="Status">The slot's current status.</param>
/// <param name="Team">The occupant's team, or <see langword="null" /> for an empty slot.</param>
/// <param name="Mods">The occupant's mods, or <see langword="null" /> for an empty slot.</param>
/// <param name="Ready">Whether the occupant is ready, or <see langword="null" /> for an empty slot.</param>
/// <param name="Loaded">
///     Whether the occupant has finished loading the map, or <see langword="null" /> for an empty slot.
/// </param>
public sealed record MatchSlotView(
	int Index,
	UserBrief? User,
	SlotStatus Status,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	MatchTeam? Team,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	Mods? Mods,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? Ready,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? Loaded);

/// <summary>
///     The payload for <c>GET/PUT/PATCH /matches/{matchId}/slots</c>, always 16 entries, indexes 0 through
///     15.
/// </summary>
/// <param name="Slots">The slot views, index-aligned with the match's slots.</param>
public sealed record MatchSlotsView(IReadOnlyList<MatchSlotView> Slots);

/// <summary>One <c>GET /matches</c> list item.</summary>
/// <remarks>
///     Carries bare match metadata plus <see cref="Live" />, which is non-null while the room is
///     currently live.
/// </remarks>
/// <param name="Id">The match's id.</param>
/// <param name="Name">The room name.</param>
/// <param name="CreatedAt">When the match was created.</param>
/// <param name="EndedAt">When the match was closed, or <see langword="null" /> while it is still open.</param>
/// <param name="Live">
///     The current room configuration, or <see langword="null" /> once the room is no longer live.
/// </param>
public sealed record MatchListItem(int Id, string Name, DateTime CreatedAt, DateTime? EndedAt, MatchRoomLive? Live);

/// <summary>The response body for <c>POST /matches/{matchId}/close</c>.</summary>
/// <param name="MatchId">The closed match's id.</param>
/// <param name="EndedAt">When the match was closed.</param>
public sealed record MatchClosedView(int MatchId, DateTime EndedAt);

/// <summary>The response body for <c>POST /matches/{matchId}/abort</c>.</summary>
/// <param name="MatchId">The aborted match's id.</param>
/// <param name="AbortedAt">When the round was aborted.</param>
public sealed record MatchAbortedView(int MatchId, DateTime AbortedAt);

/// <summary>One player's live score frame on the SSE <c>/matches/{matchId}/live/{slotIndex}</c> channel.</summary>
/// <param name="User">The player the score belongs to.</param>
/// <param name="Time">The score frame's time.</param>
/// <param name="Num300">The number of 300 judgments.</param>
/// <param name="Num100">The number of 100 judgments.</param>
/// <param name="Num50">The number of 50 judgments.</param>
/// <param name="NumGeki">The number of geki judgments.</param>
/// <param name="NumKatu">The number of katu judgments.</param>
/// <param name="NumMiss">The number of misses.</param>
/// <param name="TotalScore">The total score at this frame.</param>
/// <param name="MaxCombo">The maximum combo achieved so far.</param>
/// <param name="CurrentCombo">The current combo.</param>
/// <param name="Perfect">Whether the play has been perfect so far.</param>
/// <param name="CurrentHp">The current health value.</param>
/// <param name="ScoreV2">Whether the play uses the ScoreV2 scoring rules.</param>
public sealed record PlayerLiveScore(
	UserBrief User,
	int Time,
	int Num300,
	int Num100,
	int Num50,
	int NumGeki,
	int NumKatu,
	int NumMiss,
	int TotalScore,
	int MaxCombo,
	int CurrentCombo,
	bool Perfect,
	int CurrentHp,
	bool ScoreV2);