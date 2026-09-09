using System.Text.Json;
using Basil.Domain.Login;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Shared.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}/ban` read and write routes.</summary>
internal static class MatchBanEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match ban routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchBans(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/ban", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, users,
						cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("listMatchBans")
			.WithSummary("List match bans.")
			.WithDescription("""
			                 Returns the players banned from the match as `{ bannedUsers: [...] }`. A banned id that has no registered account is omitted.

			                 For a live stream of the same data, use `GET /matches/{matchId}/ban/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/ban/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return SseEndpoints.NotLive();

				return MatchLiveRoutes.HandleBans(context, match, events,
					() => match.BansSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchBansLive")
			.WithSummary("Stream match bans.")
			.WithDescription("""
			                 Server-Sent Events stream of the same data as `GET /matches/{matchId}/ban`.

			                 The first event is the full current list; later events carry only the fields that changed.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapPut("/matches/{matchId:numericid}/ban", async (int matchId, ReplaceBansRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var unknownId = await FirstUnknownUserIdAsync(body.UserIds, users, cancellationToken);
				if (unknownId is { } bad)
					return Results.BadRequest(new ErrorResponse($"userId {bad} is not registered."));

				var refereeId = FirstRefereeUserId(body.UserIds, match);
				if (refereeId is { } refId)
					return Results.BadRequest(
						new ErrorResponse(
							$"userId {refId} is a referee and cannot be banned. Remove referee status first."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					await matchControl.SetBansAsync(match, body.UserIds, mutation, cancellationToken);
					mutation.PublishState();
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchBans")
			.WithSummary("Replace match bans.")
			.WithDescription("""
			                 Replaces the match's ban list with `{ userIds: int[] }` and returns the updated list. Ids need not be online, but must be registered. Any newly banned id that is currently seated is also kicked.

			                 A referee is immune to being banned; remove referee status first.

			                 Returns `400 Bad Request` if any id is not a registered user or is a referee, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId 99 is not registered."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/matches/{matchId:numericid}/ban", async (int matchId, UpdateBansRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var unknownId = await FirstUnknownUserIdAsync(body.UserIds, users, cancellationToken);
				if (unknownId is { } bad)
					return Results.BadRequest(new ErrorResponse($"userId {bad} is not registered."));

				var refereeId = FirstRefereeUserId(body.UserIds, match);
				if (refereeId is { } refId)
					return Results.BadRequest(
						new ErrorResponse(
							$"userId {refId} is a referee and cannot be banned. Remove referee status first."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					await matchControl.AddBansAsync(match, body.UserIds, mutation, cancellationToken);
					mutation.PublishState();
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("addMatchBans")
			.WithSummary("Add match bans.")
			.WithDescription("""
			                 Adds `{ userIds: int[] }` to the match's ban list and returns the updated list. Ids need not be online, but must be registered. Any newly banned id that is currently seated is also kicked.

			                 A referee is immune to being banned; remove referee status first.

			                 Returns `400 Bad Request` if any id is not a registered user or is a referee, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK,
				new MatchBansView([new UserBrief(21, "Mallory", Country.Ca), new UserBrief(22, "Trent", Country.Au)]))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId 99 is not registered."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:numericid}/ban", async (int matchId, [FromBody] RemoveBansRequest body,
				IMatchRegistry matchRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));
				if (body.UserIds.Count == 0) return Results.BadRequest(new ErrorResponse("userIds is required."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var results = new List<BanRemovalResult>();
					foreach (var userId in body.UserIds)
					{
						var result = await matchControl.UnbanAsync(match, userId, mutation, cancellationToken);
						results.Add(result == MatchControlService.UnbanResult.NotBanned
							? new BanRemovalResult(userId, false, "userId is not banned from this match.")
							: new BanRemovalResult(userId, true, null));
					}

					return Results.Json(results);
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("removeMatchBans")
			.WithSummary("Remove match bans.")
			.WithDescription("""
			                 Unbans `{ userIds: int[] }`, returning one `{ userId, ok, error }` result per target. Ids need not be online.

			                 Returns `200 OK` even if some targets failed -- see each result's `ok`/`error`. Returns `400 Bad Request` if `userIds` is empty, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<IReadOnlyList<BanRemovalResult>>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new List<BanRemovalResult>
			{
				new(21, true, null),
				new(22, false, "userId is not banned from this match.")
			})
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userIds is required."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>
	///     Finds the first id in <paramref name="userIds" /> that does not belong to any registered
	///     user. Bans intentionally target ids that need not be online, only registered.
	/// </summary>
	/// <returns>The first unregistered id, or <see langword="null" /> if every id resolves.</returns>
	private static async Task<int?> FirstUnknownUserIdAsync(IReadOnlyCollection<int> userIds, IUserRepository users,
		CancellationToken cancellationToken)
	{
		foreach (var userId in userIds)
			if (await users.FetchByIdAsync(userId, cancellationToken) is null)
				return userId;

		return null;
	}

	/// <summary>
	///     Finds the first id in <paramref name="userIds" /> that is currently a referee of
	///     <paramref name="match" />. A referee is immune to being banned (Issue #4); unlike the
	///     single-target `!mp ban` bot command's own guard (<see cref="MatchControlService.BanAsync" />),
	///     this bulk API path (<see cref="MatchControlService.SetBansAsync" />/
	///     <see cref="MatchControlService.AddBansAsync" />) has no guard of its own, so the route
	///     checks before ever mutating the banlist.
	/// </summary>
	/// <returns>The first referee id found among <paramref name="userIds" />, or <see langword="null" /> if none is.</returns>
	private static int? FirstRefereeUserId(IReadOnlyCollection<int> userIds, MatchSession match)
	{
		foreach (var userId in userIds)
			if (match.IsReferee(userId))
				return userId;

		return null;
	}
}

/// <summary>Request body for `PUT /matches/{matchId}/ban`: replaces the whole ban list.</summary>
public sealed record ReplaceBansRequest(IReadOnlyList<int> UserIds);

/// <summary>Request body for `PATCH /matches/{matchId}/ban`: adds to the ban list.</summary>
public sealed record UpdateBansRequest(IReadOnlyList<int> UserIds);

/// <summary>Request body for `DELETE /matches/{matchId}/ban`: the targets to unban.</summary>
public sealed record RemoveBansRequest(IReadOnlyList<int> UserIds);

/// <summary>Per-target outcome returned by `DELETE /matches/{matchId}/ban`.</summary>
public sealed record BanRemovalResult(int UserId, bool Ok, string? Error);
