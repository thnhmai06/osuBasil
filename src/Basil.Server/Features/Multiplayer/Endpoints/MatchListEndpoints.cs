using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Shared.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches` list and create routes.</summary>
internal static class MatchListEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     `!mp make`'s own default room size, applied when <see cref="CreateMatchRequest.Size" /> is omitted (JSON
	///     default 0).
	/// </summary>
	private const int DefaultCreateSize = 16;

	/// <summary>Registers the match list and create routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchList(this RouteGroupBuilder group)
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
				new MatchListItem(42, "Grand Finals: Alpha vs Bravo", DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
					null, MatchSampleFixtures.SampleRoomLive())
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
			.WithExample(StatusCodes.Status201Created, MatchSampleFixtures.SampleSettings())
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
			items.Add(new MatchListItem(match.Id, match.Name, match.CreatedAt.AsUtcOffset(),
				match.EndedAt?.AsUtcOffset(), live));
		}

		var overqueried = items.Skip((p - 1) * ps).Take(ps + 1).ToList();
		return Results.Json(Pagination.Trim(overqueried, p, ps, items.Count));
	}

	private static async Task<IResult> HandleCreate(CreateMatchRequest body, MatchLifecycle matchLifecycle,
		MatchControlService matchControl, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
		IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var name = string.IsNullOrEmpty(body.Name) ? "New match" : body.Name;
		if (name.Length > MatchControlService.MaxMatchNameLength) name = name[..MatchControlService.MaxMatchNameLength];

		// Validated before the match is registered at all: a match that's created and then immediately
		// rejected for a bad mapId would otherwise be left behind, orphaned and half-initialized, with
		// no caller left to close it (Issue #4: "INVALID MATCH DATA CAN STILL CREATE A MATCH").
		if (body.MapId is > 0 && await beatmaps.FetchOneAsync(body.MapId.Value, cancellationToken: cancellationToken)
			    is null)
			return Results.BadRequest(new ErrorResponse($"No beatmap with id {body.MapId} found locally."));

		var data = new MatchState(
			0, false, 0, 0, name, body.Password ?? "",
			MatchControlService.NoBeatmapSelectedName, 0, "",
			[], [], [], 0, 0,
			0, 0, false, [], 0);

		var match = await matchLifecycle.CreateEmptyAsync(data, cancellationToken);
		if (match is null) return Results.Problem("Couldn't create the match: server is full.", statusCode: 503);

		await using (var mutation = await match.BeginMutationAsync(cancellationToken))
		{
			await matchControl.SetPrivateAsync(match, body.IsPrivate, mutation, cancellationToken);
			await matchControl.SetSizeAsync(match, body.Size > 0 ? body.Size : DefaultCreateSize, mutation,
				cancellationToken);

			// The mapId is already known-valid (checked above), so this cannot fail here.
			await matchControl.SetMapIfProvidedAsync(match, body.MapId, mutation, cancellationToken);

			await matchControl.ApplyModsAsync(match, body.Mods, body.Freemod, mutation, cancellationToken);
			await matchControl.SetTeamTypeWinConditionAndSizeAsync(match, body.TeamType, body.WinCondition, null,
				mutation, cancellationToken);
		}

		var settings =
			await MatchLiveSnapshotBuilder.BuildSettings(match, gameRegistry, ircRegistry, users, beatmaps,
				cancellationToken);
		return Results.Created($"/matches/{match.DbId}/settings", settings);
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