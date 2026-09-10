using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}/settings` read, replace, and update routes.</summary>
internal static class MatchSettingsEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match settings routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchSettings(this RouteGroupBuilder group)
	{
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
			.WithExample(StatusCodes.Status200OK, MatchSampleFixtures.SampleSettings())
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
			.WithExample(StatusCodes.Status200OK, MatchSampleFixtures.SampleSettings())
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

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
			.WithExample(StatusCodes.Status200OK, MatchSampleFixtures.SampleSettings())
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
			.WithExample(StatusCodes.Status200OK, MatchSampleFixtures.SampleSettings())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
			.ProducesProblem(StatusCodes.Status404NotFound);
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
		ILiveEventHub hub, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return SseEndpoints.NotLive();

		return MatchLiveRoutes.HandleSettings(context, match, hub, cancellationToken);
	}

	private static async Task<IResult> HandleSettingsReplace(int matchId, ReplaceMatchSettingsRequest body,
		IMatchRegistry matchRegistry, MatchControlService matchControl, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

		await using (var mutation = await match.BeginMutationAsync(cancellationToken))
		{
			await matchControl.SetNameAsync(match, body.Name, mutation, cancellationToken);
			await matchControl.SetPasswordAsync(match, body.Password ?? "", mutation, cancellationToken);
			await matchControl.SetPrivateAsync(match, body.IsPrivate, mutation, cancellationToken);
			MatchControlService.SetLocked(match, body.IsLocked);
			await matchControl.SetSizeAsync(match, body.Size, mutation, cancellationToken);

			if (await matchControl.SetMapIfProvidedAsync(match, body.MapId, mutation, cancellationToken)
			    == MatchControlService.SetMapResult.BeatmapNotFound)
				return Results.BadRequest(new ErrorResponse($"No beatmap with id {body.MapId} found locally."));

			await matchControl.ApplyModsAsync(match, body.Mods, body.Freemod, mutation, cancellationToken);
			await matchControl.SetTeamTypeWinConditionAndSizeAsync(match, body.TeamType, body.WinCondition, null,
				mutation, cancellationToken);
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

		await using (var mutation = await match.BeginMutationAsync(cancellationToken))
		{
			var result = await matchControl.ApplyPartialSettingsAsync(match, body.Name, body.Password,
				body.IsPrivate, body.IsLocked, body.Size, body.MapId, body.Mods, body.Freemod, body.TeamType,
				body.WinCondition, mutation, cancellationToken);
			if (result == MatchControlService.ApplySettingsResult.BeatmapNotFound)
				return Results.BadRequest(new ErrorResponse($"No beatmap with id {body.MapId!.Value} found locally."));
		}

		return Results.Json(
			await MatchLiveSnapshotBuilder.BuildSettings(match, gameRegistry, ircRegistry, users, beatmaps,
				cancellationToken));
	}
}

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