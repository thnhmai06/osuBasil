using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Builds the lightweight, in-memory-only payloads pushed over the live SSE channels
///     (<see cref="IMatchLiveEvents" />) — deliberately cheaper than <see cref="MatchReportService" />'s
///     DB-backed <c>MatchReport</c>, since these fire on every state-changing packet (join, ready,
///     slot change, ...), not on demand. Consequently, the main channel carries live slot/map/state
///     only, not aggregated team scores or a round winner — those come from polling GET /match/{id}.
///     Every user reference is resolved via <see cref="UserBriefResolver" /> (fast for online players,
///     falling back to the cached <see cref="IUserRepository" /> for offline ones), which is why every
///     builder that can reference a user is async.
/// </summary>
public static class MatchLiveSnapshotBuilder
{
	public static async Task<MatchLiveSnapshot> BuildMain(MatchSession match, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, IMapRepository maps, CancellationToken cancellationToken = default)
	{
		var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);

		var host = match.HostId == 0
			? null
			: await ResolveOrPlaceholder(match.HostId, sessionRegistry, users, cancellationToken);

		var referees = new List<UserBrief>();
		foreach (var id in match.Referees)
			referees.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

		var slots = await BuildSlotViews(match, sessionRegistry, users, cancellationToken);
		var beatmap = await ResolveBeatmapAsync(match.MapMd5, maps, cancellationToken);

		return new MatchLiveSnapshot(
			match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
			match.MapId, match.Mods, match.Freemods, match.TeamType, match.WinCondition, match.Mode,
			match.InProgress, host, referees, beatmap, slots);
	}

	/// <summary>
	///     The room-core-plus-map-plus-in-progress shape reused by both `GET /matches` list items and
	///     <see cref="MatchReportService" />'s <c>Live</c> field — no host/referees/slots (those are
	///     membership details, not room configuration).
	/// </summary>
	public static async Task<MatchRoomLive> BuildRoomLive(MatchSession match, IMapRepository maps,
		CancellationToken cancellationToken = default)
	{
		var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);
		var beatmap = await ResolveBeatmapAsync(match.MapMd5, maps, cancellationToken);

		return new MatchRoomLive(
			match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
			match.MapId, match.Mods, match.Freemods, match.TeamType, match.WinCondition, match.Mode,
			match.InProgress, beatmap);
	}

	public static PlayerLiveScore BuildPlayerScore(PlayerSession player, ScoreFrameData frame)
	{
		return new PlayerLiveScore(
			new UserBrief(player.Id, player.Name, player.Geoloc.Country),
			frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
			frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp,
			frame.ScoreV2);
	}

	/// <summary>
	///     The `api.` host's `/match/{id}/settings` payload shape — never the raw <see cref="MatchSession.Password" />,
	///     only whether one is set, even for an admin-elevated caller (a public, unauthenticated SSE
	///     channel is not the place to leak it).
	/// </summary>
	public static async Task<MatchSettingsView> BuildSettings(MatchSession match,
		IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, IMapRepository maps, CancellationToken cancellationToken = default)
	{
		var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);

		var host = match.HostId == 0
			? null
			: await ResolveOrPlaceholder(match.HostId, sessionRegistry, users, cancellationToken);

		var referees = new List<UserBrief>();
		foreach (var id in match.Referees)
			referees.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

		var beatmap = await ResolveBeatmapAsync(match.MapMd5, maps, cancellationToken);

		return new MatchSettingsView(
			match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
			match.MapId, match.Mods, match.Freemods, match.TeamType, match.WinCondition, match.Mode,
			host, referees, beatmap);
	}

	public static async Task<MatchHostView> BuildHost(MatchSession match, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		if (match.HostId == 0) return new MatchHostView(null);

		var host = await ResolveOrPlaceholder(match.HostId, sessionRegistry, users, cancellationToken);
		return new MatchHostView(host);
	}

	public static async Task<MatchRefereesView> BuildRefs(MatchSession match, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		var referees = new List<UserBrief>();
		foreach (var id in match.Referees)
			referees.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

		return new MatchRefereesView(referees);
	}

	public static async Task<MatchBansView> BuildBans(MatchSession match, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		var banned = new List<UserBrief>();
		foreach (var id in match.BannedIds)
			banned.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

		return new MatchBansView(banned);
	}

	public static MatchTimerView BuildTimer(MatchSession match)
	{
		if (match.PendingTimer is null || match.TimerStartedAt is null || match.TimerTotalSeconds is null)
			return new MatchTimerView(false, null, false);

		var elapsed = (DateTimeOffset.UtcNow - match.TimerStartedAt.Value).TotalSeconds;
		var remaining = Math.Max(0, match.TimerTotalSeconds.Value - (int)elapsed);
		return new MatchTimerView(true, remaining, match.PendingTimerIsAutoStart);
	}

	public static async Task<MatchSlotsView> BuildSlots(MatchSession match, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		return new MatchSlotsView(await BuildSlotViews(match, sessionRegistry, users, cancellationToken));
	}

	private static async Task<IReadOnlyList<MatchSlotView>> BuildSlotViews(MatchSession match,
		IPlayerSessionRegistry sessionRegistry, IUserRepository users, CancellationToken cancellationToken)
	{
		var slots = new List<MatchSlotView>(match.Slots.Count);
		for (var i = 0; i < match.Slots.Count; i++)
		{
			var slot = match.Slots[i];
			var user = slot.PlayerId is { } pid
				? await ResolveOrPlaceholder(pid, sessionRegistry, users, cancellationToken)
				: null;
			slots.Add(new MatchSlotView(i, user, slot.Status, slot.Team, slot.Mods,
				slot.Status == SlotStatus.Ready, slot.Loaded));
		}

		return slots;
	}

	/// <summary>
	///     Shared by every beatmap embed in this plan (live snapshot, settings view, TRT rounds/live
	///     info) — a blank md5 means "no beatmap assigned yet" (a freshly created empty room), which is
	///     never worth a repository round-trip and must not be confused with a stale/unresolvable md5
	///     (both end up `null`, but only the latter is an actual lookup miss).
	/// </summary>
	public static async Task<BeatmapDetail?> ResolveBeatmapAsync(string mapMd5, IMapRepository maps,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(mapMd5)) return null;

		var beatmap = await maps.FetchOneAsync(md5: mapMd5, includePrivate: true, cancellationToken: cancellationToken);
		if (beatmap is null) return null;

		var siblings = await maps.FetchAllBySetIdAsync(beatmap.Mapset.Id, true,
			cancellationToken);
		var beatmapset = beatmap.Mapset.ToSummary(siblings.Count);
		return beatmap.ToDetail(beatmapset);
	}

	/// <summary>
	///     Every host/referee/ban/slot-occupant id embedded by this plan is, by definition, actually
	///     assigned/referenced — unlike <see cref="UserBriefResolver.ResolveAsync" />'s plain null
	///     (which the caller uses for genuine structural absence, e.g. host id 0 or an empty slot), a
	///     resolution failure here must never silently shrink a list or get confused with "nothing is
	///     assigned" — it falls back to a placeholder that still carries the real id.
	/// </summary>
	public static async Task<UserBrief> ResolveOrPlaceholder(int userId, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, CancellationToken cancellationToken)
	{
		return await UserBriefResolver.ResolveAsync(userId, sessionRegistry, users, cancellationToken)
		       ?? new UserBrief(userId, "Unknown", Country.Xx);
	}
}

/// <summary>
///     The one reused `{id, name, country}` embed for every user reference across this plan's
///     routes/SSE payloads (settings host/referees, hosts/refs/bans views, slot occupants, TRT
///     actor/target/winner/scorer, per-player live streams) — resolved via <see cref="UserBriefResolver" />.
///     <see cref="Country" /> stays enum-typed here (type-safe in C#); <see cref="Json.CountryJsonConverter" />
///     owns converting it to the wire's lowercase 2-letter acronym.
/// </summary>
public sealed record UserBrief(int Id, string Name, Country Country);

/// <summary>
///     Configuration fields shared by every match "room" shape — no membership (host/referees/slots)
///     and no rounds. <see cref="MapId" /> keeps the bancho `-1` sentinel ("choosing beatmap" or "map
///     not on this system") rather than going nullable; <see cref="Beatmap" /> (on each derived record)
///     is null in both of those cases.
/// </summary>
public abstract record MatchRoomCore(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	GameMode Mode);

/// <summary>
///     The `live` object embedded in a `GET /matches` list item and in <see cref="MatchReportService" />'s report —
///     room config plus the current map/in-progress flag, no slots/host/refs/rounds.
/// </summary>
public sealed record MatchRoomLive(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	GameMode Mode,
	bool InProgress,
	BeatmapDetail? Beatmap)
	: MatchRoomCore(Id, Name, HasPassword, IsPrivate, IsLocked, Size, MapId, Mods, Freemod, TeamType, WinCondition,
		Mode);

/// <summary>
///     Payload for the SSE `/match/{id}/settings` channel and the response of every settings write — carries
///     host/referees since this resource manages membership control, not just config.
/// </summary>
public sealed record MatchSettingsView(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int MapId,
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

/// <summary>Payload for the SSE `/match/{id}` main channel — the full live snapshot, including slots.</summary>
public sealed record MatchLiveSnapshot(
	int Id,
	string Name,
	bool HasPassword,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int MapId,
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

/// <summary>Payload for `GET /matches/{matchId}/hosts` — null when the room has no host (id 0).</summary>
public sealed record MatchHostView(UserBrief? Host);

/// <summary>Payload for `GET /matches/{matchId}/refs`.</summary>
public sealed record MatchRefereesView(IReadOnlyList<UserBrief> Referees);

/// <summary>Payload for `GET /matches/{matchId}/ban`.</summary>
public sealed record MatchBansView(IReadOnlyList<UserBrief> BannedUsers);

/// <summary>
///     Payload for `GET /matches/{matchId}/timer`. <c>AutoStart</c> mirrors
///     <see cref="MatchSession.PendingTimerIsAutoStart" />.
/// </summary>
public sealed record MatchTimerView(bool Running, int? SecondsRemaining, bool AutoStart);

/// <summary>
///     One slot in `GET /matches/{matchId}/slots` — <see cref="Status" />/<see cref="Team" />/
///     <see cref="Mods" /> stay enum-typed (never a `.ToString()` name). <see cref="Ready" /> mirrors
///     <see cref="Status" /> being <see cref="SlotStatus.Ready" />, kept as its own bool since a
///     consumer checking readiness shouldn't have to know the full <see cref="SlotStatus" /> flag set.
/// </summary>
public sealed record MatchSlotView(
	int Index,
	UserBrief? User,
	SlotStatus Status,
	MatchTeam Team,
	Mods Mods,
	bool Ready,
	bool Loaded);

/// <summary>Payload for `GET/PUT/PATCH /matches/{matchId}/slots` — always 16 entries, index 0-15.</summary>
public sealed record MatchSlotsView(IReadOnlyList<MatchSlotView> Slots);

/// <summary>
///     One `GET /matches` list item — bare match metadata plus `Live` (non-null while the room is currently tracked
///     in memory), replacing the old flat `IsOpen` bool.
/// </summary>
public sealed record MatchListItem(int Id, string Name, DateTime CreatedAt, DateTime? EndedAt, MatchRoomLive? Live);

/// <summary>Response body for `POST /matches/{matchId}/close` — net-new, not wired into any route yet (Phase 3).</summary>
public sealed record MatchClosedView(int MatchId, DateTime EndedAt);

/// <summary>Payload for the SSE `/match/{id}/{playerName}` channel.</summary>
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