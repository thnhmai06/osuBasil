using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Formats;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints for listing, creating, and streaming multiplayer matches.
/// </summary>
/// <remarks>
///     Match listings, settings, and live streams are publicly readable, while creating a match and
///     modifying its settings require administrator authorization. Live streams are dedicated
///     server-sent-events channels that only open for matches that are currently live.
/// </remarks>
internal static class MatchRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     `!mp make`'s own default room size, applied when <see cref="CreateMatchRequest.Size" /> is omitted (JSON
	///     default 0).
	/// </summary>
	private const int DefaultCreateSize = 16;

	/// <summary>
	///     Registers the `/matches` list, create, settings, and live-stream routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/matches", HandleList)
			.WithGroupName("basilapi")
			.WithName("listMatches")
			.WithSummary("List matches.")
			.WithDescription("""
			                 Lists matches by status.

			                 `online` (default) returns matches that are live right now, `offline` returns closed matches, and `all` returns both, newest first. A private live match appears only for callers with a valid admin key.

			                 Results are paginated with `page` (default 1) and `pageSize` (default 50).
			                 """)
			.WithTags("Matches")
			.Produces<PagedResult<MatchListItem>>()
			.WithExample(StatusCodes.Status200OK, new PagedResult<MatchListItem>(1, 50, 1,
			[
				new MatchListItem(42, "Grand Finals: Alpha vs Bravo", DateTime.Parse("2026-07-20T12:00:00Z"), null,
					SampleRoomLive())
			]))
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("Invalid status 'foo'. Expected 'online', 'offline', or 'all'."));

		group.MapPost("/matches", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createMatch")
			.WithSummary("Create a match.")
			.WithDescription("""
			                 Creates a new multiplayer match and returns its room settings.

			                 Every field is required except `password` and `mapId`; a `null` or omitted `mapId` means no beatmap has been chosen yet. The new match starts with host id 0 and no referees; set the host with `PUT /matches/{matchId}/hosts` and add referees with `PATCH /matches/{matchId}/refs`.
			                 """ + AdminKeyNote)
			.WithTags("Matches")
			.Produces<MatchSettingsView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable)
			.WithExample(StatusCodes.Status201Created, SampleSettings())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
			.WithLink(StatusCodes.Status201Created, "GetMatchReport", "getMatchReport",
				"Fetch the full TRT report for the newly created match.",
				("matchId", "$response.body#/data/id"))
			.WithLink(StatusCodes.Status201Created, "GetMatchSettings", "getMatchSettings",
				"Read back the newly created match's room settings.",
				("matchId", "$response.body#/data/id"))
			.WithLink(StatusCodes.Status201Created, "ReplaceMatchSettings", "replaceMatchSettings",
				"Replace the newly created match's room settings.",
				("matchId", "$response.body#/data/id"))
			.WithLink(StatusCodes.Status201Created, "UpdateMatchSettings", "updateMatchSettings",
				"Partially update the newly created match's room settings.",
				("matchId", "$response.body#/data/id"));

		group.MapGet("/matches/{matchId:numericid}", async (int matchId, MatchReportService reportService,
				CancellationToken cancellationToken) =>
			{
				var report = await reportService.BuildAsync(matchId, cancellationToken);
				return report is null ? Results.NotFound(new ErrorResponse("Match not found.")) : Results.Json(report);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchReport")
			.WithSummary("Retrieve a match report.")
			.WithDescription("""
			                 Returns a complete match report, including its events, rounds, and per-round scores.
			                 If the match is still live, the report also includes its current room state, such as the host, referees, slots, beatmap, win condition, team mode, mods, and match status.
			                 For real-time updates, use `GET /matches/{matchId}/live`.

			                 Returns `404 Not Found` if the match does not exist.
			                 """)
			.WithTags("Match Report")
			.Produces<MatchReport>()
			.WithExample(StatusCodes.Status200OK, SampleMatchReport())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/settings", HandleSettingsGet)
			.WithGroupName("basilapi")
			.WithName("getMatchSettings")
			.WithSummary("Get match settings.")
			.WithDescription("""
			                 Returns the match's room configuration.

			                 The password is never returned, only `hasPassword`.

			                 For a live stream of the same data, use `GET /matches/{matchId}/settings/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Settings")
			.Produces<MatchSettingsView>()
			.WithExample(StatusCodes.Status200OK, SampleSettings())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/settings/live", HandleSettingsStream)
			.WithGroupName("basilapi")
			.WithName("getMatchSettingsLive")
			.WithSummary("Stream match settings.")
			.WithDescription("""
			                 Server-Sent Events stream of the match's room configuration.

			                 The first event is the full current settings; each later event carries only the fields that changed. The password is never returned, only `hasPassword`.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Settings")
			.Produces<MatchSettingsView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSettings());

		group.MapPut("/matches/{matchId:numericid}/settings", HandleSettingsReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchSettings")
			.WithSummary("Replace match settings.")
			.WithDescription("""
			                 Replaces the match's entire room configuration and returns the updated settings.

			                 Every field is required except `password` and `mapId`; a `null` or omitted `mapId` clears or skips the beatmap selection. `freemod: true` enables FreeMod and ignores `mods` for that call; `mods` alone sets the room's fixed mod set.

			                 Returns `400 Bad Request` if `mapId` doesn't resolve to a known beatmap, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Settings")
			.Produces<MatchSettingsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleSettings())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/matches/{matchId:numericid}/settings", HandleSettingsUpdate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateMatchSettings")
			.WithSummary("Update match settings.")
			.WithDescription("""
			                 Partially updates the match's room configuration and returns the updated settings. Only the fields present in the body are changed.

			                 Returns `400 Bad Request` if `mapId` doesn't resolve to a known beatmap, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Settings")
			.Produces<MatchSettingsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleSettings())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/live", HandleMainLiveStream)
			.WithGroupName("basilapi")
			.WithName("getMatchLive")
			.WithSummary("Stream full match state.")
			.WithDescription("""
			                 Server-Sent Events stream of the match's full live state: room config, host, referees, current beatmap, in-progress flag, and every slot.

			                 The first event is the full current state; each later event carries only the fields that changed. This channel carries no per-player score data; use `GET /matches/{matchId}/live/{slotIndex}` for that.

			                 For a one-shot snapshot including historical rounds and events, use `GET /matches/{matchId}`.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Live")
			.Produces<MatchLiveSnapshot>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleLiveSnapshot());

		group.MapGet("/matches/{matchId:numericid}/live/{slotIndex:int}", HandleLiveSlotStream)
			.WithGroupName("basilapi")
			.WithName("getMatchSlotLiveStream")
			.WithSummary("Stream one match slot.")
			.WithDescription("""
			                 Server-Sent Events stream for a single slot, `{slotIndex}` in 1-16 (matching `!mp move`'s convention).

			                 Each event is one of three types, carried by the SSE `event` field:

			                 - `slot`: the slot's occupancy, status, team, and mods (full first, then deltas)
			                 - `score`: the current occupant's live score frames during a round
			                 - `input`: the current occupant's raw spectator-input frames, the same shape as `GET /users/{idOrName}/live`

			                 The stream follows whoever currently occupies the slot; if the occupant changes, later `score`/`input` events match the new occupant automatically.

			                 Returns `404 Not Found` if the match isn't currently live or `slotIndex` is out of range.
			                 """)
			.WithTags("Match Live")
			.Produces<PlayerLiveScore>()
			.WithSlotLiveExamples()
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapMatchSubResourceRoutes();
	}

	private static async Task<IResult> HandleList(
		[FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize,
		HttpContext context, IMatchRegistry matchRegistry, IMatchRepository matchRepository,
		IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var (p, ps) = Pagination.Normalize(page, pageSize);
		var mode = (status ?? "online").ToLowerInvariant();
		if (mode is not ("online" or "offline" or "all"))
			return Results.BadRequest(
				new ErrorResponse($"Invalid status '{status}'. Expected 'online', 'offline', or 'all'."));
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);

		var rows = await matchRepository.FetchAllMatchesAsync(cancellationToken);
		var filtered = rows
			.Select(row => (Row: row, Live: matchRegistry.GetByDbId(row.Id)))
			.Where(t =>
			{
				var isOpen = t.Live is not null;
				switch (mode)
				{
					case "online" when !isOpen:
					case "offline" when isOpen:
						return false;
					default:
						return !isOpen || !t.Live!.IsPrivate || isAdmin;
				}
			})
			.OrderByDescending(t => t.Row.Id)
			.ToList();

		var items = new List<MatchListItem>(filtered.Count);
		foreach (var (match, matchLive) in filtered)
		{
			var live = matchLive is not null
				? await MatchLiveSnapshotBuilder.BuildRoomLive(matchLive, beatmaps, cancellationToken)
				: null;
			items.Add(new MatchListItem(match.Id, match.Name, match.CreatedAt, match.EndedAt, live));
		}

		var overqueried = items.Skip((p - 1) * ps).Take(ps + 1).ToList();
		return Results.Json(Pagination.Trim(overqueried, p, ps, items.Count));
	}

	private static async Task<IResult> HandleCreate(CreateMatchRequest body, MatchMembershipService matchMembership,
		MatchControlService matchControl, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
		IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var name = string.IsNullOrEmpty(body.Name) ? "New match" : body.Name;
		if (name.Length > MatchControlService.MaxMatchNameLength) name = name[..MatchControlService.MaxMatchNameLength];

		var data = new MatchState(
			0, false, 0, 0, name, body.Password ?? "",
			"", 0, "",
			[], [], [], 0, 0,
			0, 0, false, [], 0);

		var match = await matchMembership.CreateEmptyAsync(data, cancellationToken);
		if (match is null) return Results.Problem("Couldn't create the match: server is full.", statusCode: 503);

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await matchControl.SetPrivateAsync(match, body.IsPrivate, cancellationToken);
			await matchControl.SetSizeAsync(match, body.Size > 0 ? body.Size : DefaultCreateSize, cancellationToken);

			var mapError = await ApplyFullMapAsync(match, body.MapId, matchControl, cancellationToken);
			if (mapError is not null) return mapError;

			await ApplyFullModsAsync(match, body.Mods, body.Freemod, matchControl, cancellationToken);
			await matchControl.SetTeamTypeWinConditionAndSizeAsync(match, body.TeamType, body.WinCondition, null,
				cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}

		var settings =
			await MatchLiveSnapshotBuilder.BuildSettings(match, gameRegistry, ircRegistry, users, beatmaps,
				cancellationToken);
		return Results.Created($"/matches/{match.DbId}/settings", settings);
	}

	private static async Task<IResult> HandleSettingsGet(int matchId, IMatchRegistry matchRegistry,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps,
		CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

		return Results.Json(
			await MatchLiveSnapshotBuilder.BuildSettings(match, gameRegistry, ircRegistry, users, beatmaps,
				cancellationToken));
	}

	private static IResult HandleSettingsStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
		IMatchLiveEvents events, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return LiveSseRoutes.NotLive();

		return LiveSseRoutes.HandleSettings(context, match, events,
			() => match.SettingsSnapshot.Latest is { } snapshot
				? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
				: null,
			cancellationToken);
	}

	private static IResult HandleMainLiveStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
		IMatchLiveEvents events, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return LiveSseRoutes.NotLive();

		return LiveSseRoutes.HandleMain(context, match, events,
			() => match.MainSnapshot.Latest is { } snapshot
				? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
				: null,
			cancellationToken);
	}

	private static IResult HandleLiveSlotStream(int matchId, int slotIndex, HttpContext context,
		IMatchRegistry matchRegistry, IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents,
		ISessionRegistry<GameSession> gameRegistry, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null || slotIndex is < 1 or > 16)
			return LiveSseRoutes.SseError(StatusCodes.Status404NotFound,
				"Match is not currently live, or slotIndex is out of range.");

		var index = slotIndex - 1;
		return LiveSseRoutes.HandleLiveSlot(context, match, index, matchEvents, inputEvents, gameRegistry,
			() => match.SlotSnapshots[index].Latest is { } snapshot
				? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
				: null,
			cancellationToken);
	}

	private static async Task<IResult> HandleSettingsReplace(int matchId, ReplaceMatchSettingsRequest body,
		IMatchRegistry matchRegistry, MatchControlService matchControl, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await matchControl.SetNameAsync(match, body.Name, cancellationToken);
			await matchControl.SetPasswordAsync(match, body.Password ?? "", cancellationToken);
			await matchControl.SetPrivateAsync(match, body.IsPrivate, cancellationToken);
			MatchControlService.SetLocked(match, body.IsLocked);
			await matchControl.SetSizeAsync(match, body.Size, cancellationToken);

			var mapError = await ApplyFullMapAsync(match, body.MapId, matchControl, cancellationToken);
			if (mapError is not null) return mapError;

			await ApplyFullModsAsync(match, body.Mods, body.Freemod, matchControl, cancellationToken);
			await matchControl.SetTeamTypeWinConditionAndSizeAsync(match, body.TeamType, body.WinCondition, null,
				cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}

		return Results.Json(
			await MatchLiveSnapshotBuilder.BuildSettings(match, gameRegistry, ircRegistry, users, beatmaps,
				cancellationToken));
	}

	private static async Task<IResult> HandleSettingsUpdate(int matchId, UpdateMatchSettingsRequest body,
		IMatchRegistry matchRegistry, MatchControlService matchControl, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var applyResult = await ApplySettingsAsync(match, body, matchControl, cancellationToken);
			if (applyResult is not null) return applyResult;
		}
		finally
		{
			match.Lock.Release();
		}

		return Results.Json(
			await MatchLiveSnapshotBuilder.BuildSettings(match, gameRegistry, ircRegistry, users, beatmaps,
				cancellationToken));
	}

	/// <summary>
	///     Shared by <see cref="HandleCreate" /> and <see cref="HandleSettingsReplace" />: both apply every
	///     field unconditionally (PUT-style, no "was this given" check), unlike
	///     <see cref="ApplySettingsAsync" />'s PATCH-style partial application. `null` means "no beatmap
	///     chosen"; a caller still sending the legacy `-1` sentinel, or `0` (ids in this schema
	///     auto-increment from 1, so 0 can never be a real beatmap), is treated the same way. All three
	///     are skipped entirely rather than attempted as a lookup, which would otherwise fail with a
	///     confusing "beatmap not found" error for a caller correctly signaling "no map".
	/// </summary>
	private static async Task<IResult?> ApplyFullMapAsync(MatchSession match, int? mapId,
		MatchControlService matchControl, CancellationToken cancellationToken)
	{
		if (mapId is null or <= 0) return null;

		var (result, _) = await matchControl.SetMapAsync(match, mapId.Value, cancellationToken);
		return result == MatchControlService.SetMapResult.BeatmapNotFound
			? Results.BadRequest(new ErrorResponse($"No beatmap with id {mapId} found on the server."))
			: null;
	}

	/// <summary>
	///     Shared by <see cref="HandleCreate" /> and <see cref="HandleSettingsReplace" />: `freemod: true` ignores `mods`
	///     for that call, matching real Bancho.
	/// </summary>
	private static async Task ApplyFullModsAsync(MatchSession match, Mods mods, bool freemod,
		MatchControlService matchControl, CancellationToken cancellationToken)
	{
		if (freemod)
			await matchControl.SetModsAsync(match, Mods.NoMod, true, cancellationToken);
		else
			await matchControl.SetModsAsync(match, mods, false, cancellationToken);
	}

	/// <summary>Caller must hold <paramref name="match" />'s Lock. Returns a non-null error IResult on failure.</summary>
	private static async Task<IResult?> ApplySettingsAsync(MatchSession match, UpdateMatchSettingsRequest body,
		MatchControlService matchControl, CancellationToken cancellationToken)
	{
		if (body.Name is not null) await matchControl.SetNameAsync(match, body.Name, cancellationToken);
		if (body.Password is not null) await matchControl.SetPasswordAsync(match, body.Password, cancellationToken);
		if (body.IsPrivate is not null)
			await matchControl.SetPrivateAsync(match, body.IsPrivate.Value, cancellationToken);
		if (body.IsLocked is not null) MatchControlService.SetLocked(match, body.IsLocked.Value);
		if (body.Size is not null) await matchControl.SetSizeAsync(match, body.Size.Value, cancellationToken);

		if (body.MapId is not null)
		{
			var (result, _) = await matchControl.SetMapAsync(match, body.MapId.Value, cancellationToken);
			if (result == MatchControlService.SetMapResult.BeatmapNotFound)
				return Results.BadRequest(new ErrorResponse($"No beatmap with id {body.MapId.Value} found locally."));
		}

		if (body.Freemod == true)
			await matchControl.SetModsAsync(match, Mods.NoMod, true, cancellationToken);
		else if (body.Mods is not null)
			await matchControl.SetModsAsync(match, body.Mods.Value, false, cancellationToken);

		if (body.TeamType is not null || body.WinCondition is not null)
			await matchControl.SetTeamTypeWinConditionAndSizeAsync(match,
				body.TeamType ?? match.TeamType, body.WinCondition, null, cancellationToken);

		return null;
	}

	private static MatchSettingsView SampleSettings()
	{
		return new MatchSettingsView(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard,
			new UserBrief(7, "Alice", Country.Us),
			[new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)],
			SampleBeatmap());
	}

	private static MatchRoomLive SampleRoomLive()
	{
		return new MatchRoomLive(true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard,
			true, SampleBeatmap());
	}

	internal static MatchLiveSnapshot SampleLiveSnapshot()
	{
		var slots = new List<MatchSlotView>(16);
		for (var i = 0; i < 16; i++)
			slots.Add(new MatchSlotView(i, null, SlotStatus.Open, MatchTeam.Neutral, Mods.NoMod, false, false));
		slots[0] = new MatchSlotView(0, new UserBrief(7, "Alice", Country.Us), SlotStatus.Playing, MatchTeam.Red,
			Mods.NoMod, true, true);

		return new MatchLiveSnapshot(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard, true,
			new UserBrief(7, "Alice", Country.Us),
			[new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)],
			SampleBeatmap(), slots);
	}

	private static BeatmapDetail SampleBeatmap()
	{
		var created = DateTime.Parse("2026-06-01T10:00:00Z");
		var beatmapset = new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created,
			created, false, false, BeatmapStatus.Loved, 1);
		var difficulty = new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42);
		var objectCounts = new OsuBeatmapObjectCounts
			{ Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 };
		return new BeatmapDetail("d41d8cd98f00b204e9800998ecf8427e", 654, "Extreme",
			difficulty, objectCounts, false, beatmapset);
	}

	private static MatchReport SampleMatchReport()
	{
		var created = DateTime.Parse("2026-06-01T10:00:00Z");
		var started = DateTime.Parse("2026-07-20T12:00:00Z");
		var ended = DateTime.Parse("2026-07-20T12:04:30Z");

		var beatmapset = new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created,
			created, false, false, BeatmapStatus.Loved, 1);
		var difficulty = new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42);
		var objectCounts = new OsuBeatmapObjectCounts
			{ Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 };
		var beatmap = new BeatmapDetail("d41d8cd98f00b204e9800998ecf8427e", 654, "Extreme",
			difficulty, objectCounts, false, beatmapset);

		var live = new MatchRoomLive(true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard, false, beatmap);

		var score = new MatchReportScore(new UserBrief(7, "Alice", Country.Vn), MatchTeam.Red, Mods.NoMod, 4_850_213,
			98.42, 1234, 720, 45, 3, 2, 12, 5, Grade.A, false, ended);
		var round = new MatchReportRound(0, "d41d8cd98f00b204e9800998ecf8427e", beatmap,
			GameMode.Standard, MatchWinCondition.ScoreV2, MatchTeamType.TeamVs, Mods.NoMod, false, started, ended,
			new UserBrief(7, "Alice", Country.Vn), MatchTeam.Red, MatchWinCondition.Score, 1_200_000, [score]);
		var evt = new MatchReportEvent(MatchEventType.Created, new UserBrief(7, "Alice", Country.Vn), null, started,
			null);

		return new MatchReport(42, "Grand Finals: Alpha vs Bravo", started, null, live, [evt], [round]);
	}
}

/// <summary>
///     Request body for `POST /matches`. Every field is required except `password` and `mapId`; a
///     `null` or omitted `mapId` means no beatmap has been chosen yet.
/// </summary>
public sealed record CreateMatchRequest(
	string Name,
	string? Password,
	bool IsPrivate,
	int? MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition,
	int Size);

/// <summary>
///     Request body for `PUT /matches/{matchId}/settings`: full replace. Every field is required
///     except `password` and `mapId`; a `null` or omitted `mapId` means no beatmap has been chosen yet.
/// </summary>
public sealed record ReplaceMatchSettingsRequest(
	string Name,
	string? Password,
	bool IsPrivate,
	bool IsLocked,
	int Size,
	int? MapId,
	Mods Mods,
	bool Freemod,
	MatchTeamType TeamType,
	MatchWinCondition WinCondition);

/// <summary>
///     Request body for `PATCH /matches/{matchId}/settings`: every field is optional, and only the ones present are
///     applied.
/// </summary>
public sealed record UpdateMatchSettingsRequest(
	string? Name = null,
	string? Password = null,
	bool? IsPrivate = null,
	bool? IsLocked = null,
	int? Size = null,
	int? MapId = null,
	Mods? Mods = null,
	bool? Freemod = null,
	MatchTeamType? TeamType = null,
	MatchWinCondition? WinCondition = null);