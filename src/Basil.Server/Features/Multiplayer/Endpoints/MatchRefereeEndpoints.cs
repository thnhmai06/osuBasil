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

/// <summary>Registers the `/matches/{matchId}/refs` read and write routes.</summary>
internal static class MatchRefereeEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match referee routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchReferees(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/refs", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildRefs(match, gameRegistry, ircRegistry, users,
						cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("listMatchReferees")
			.WithSummary("List match referees.")
			.WithDescription("""
			                 Returns the match's referees as `{ referees: [...] }`.

			                 For a live stream of the same data, use `GET /matches/{matchId}/refs/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Referees")
			.Produces<MatchRefereesView>()
			.WithExample(StatusCodes.Status200OK,
				new MatchRefereesView([new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/refs/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return SseEndpoints.NotLive();

				return MatchLiveRoutes.HandleRefs(context, match, events,
					() => match.RefsSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchRefereesLive")
			.WithSummary("Stream match referees.")
			.WithDescription("""
			                 Server-Sent Events stream of the same data as `GET /matches/{matchId}/refs`.

			                 The first event is the full current list; later events carry only the fields that changed.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Referees")
			.Produces<MatchRefereesView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchRefereesView([new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)]))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapPut("/matches/{matchId:numericid}/refs", async (int matchId, ReplaceRefereesRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var (targets, error) = ResolveOnlineTargets(body.UserIds, gameRegistry, ircRegistry);
				if (error is not null) return error;

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var result = await matchControl.SetRefereesAsync(match, targets, mutation, cancellationToken);
					return result switch
					{
						MatchControlService.SetRefereesResult.WouldLeaveEmpty =>
							Results.Conflict(new ErrorResponse("Refusing to leave the match with no referees.")),
						MatchControlService.SetRefereesResult.WouldRemoveCreator =>
							Results.Conflict(
								new ErrorResponse("Refusing to remove the match's creator from referees.")),
						_ => Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, gameRegistry, ircRegistry,
							users,
							cancellationToken))
					};
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchReferees")
			.WithSummary("Replace match referees.")
			.WithDescription("""
			                 Replaces the match's referee list with `{ userIds: int[] }` and returns the updated list. Every id must be online.

			                 Returns `400 Bad Request` if any `userId` isn't online, `409 Conflict` if the result would leave the match with no referees or would drop the match's creator from the list, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Referees")
			.Produces<MatchRefereesView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchRefereesView([new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)]))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("userId 21 is required and must be online."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("Refusing to leave the match with no referees."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/matches/{matchId:numericid}/refs", async (int matchId, UpdateRefereesRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var (targets, error) = ResolveOnlineTargets(body.UserIds, gameRegistry, ircRegistry);
				if (error is not null) return error;

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var results = new List<RefereeAdditionResult>();
					foreach (var target in targets)
					{
						var result =
							await matchControl.AddRefereeAsync(null, null, match, target, mutation, cancellationToken);
						results.Add(result switch
						{
							MatchControlService.AddRefereeResult.Ok => new RefereeAdditionResult(target.Id, true, null),
							MatchControlService.AddRefereeResult.AlreadyReferee =>
								new RefereeAdditionResult(target.Id, false, "Already a referee of this match."),
							_ => new RefereeAdditionResult(target.Id, false, "Cannot make BasilBot a referee.")
						});
					}

					return Results.Json(results);
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("addMatchReferees")
			.WithSummary("Add match referees.")
			.WithDescription("""
			                 Adds `{ userIds: int[] }` to the match's referees, returning one `{ userId, ok, error }` result per target. Every id must be online.

			                 Returns `200 OK` even if some targets failed -- see each result's `ok`/`error`. Returns `400 Bad Request` if any `userId` isn't online, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Referees")
			.Produces<IReadOnlyList<RefereeAdditionResult>>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new List<RefereeAdditionResult>
			{
				new(13, true, null),
				new(9, false, "Already a referee of this match.")
			})
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("userId 21 is required and must be online."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:numericid}/refs", async (int matchId, [FromBody] RemoveRefereesRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));
				if (body.UserIds.Count == 0) return Results.BadRequest(new ErrorResponse("userIds is required."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var results = new List<RefereeRemovalResult>();
					foreach (var userId in body.UserIds)
					{
						var target = (UserSession?)gameRegistry.GetByUserId(userId) ?? ircRegistry.GetByUserId(userId);
						if (target is null)
						{
							results.Add(new RefereeRemovalResult(userId, false, "Not online with the osu! client."));
							continue;
						}

						var result =
							await matchControl.RemoveOneRefereeAsync(null, null, match, target, mutation,
								cancellationToken);
						results.Add(result switch
						{
							MatchControlService.RemoveRefereeResult.Ok => new RefereeRemovalResult(userId, true, null),
							MatchControlService.RemoveRefereeResult.WouldLeaveEmpty =>
								new RefereeRemovalResult(userId, false,
									"Refusing to leave the match with no referees."),
							MatchControlService.RemoveRefereeResult.TargetIsCreator =>
								new RefereeRemovalResult(userId, false,
									"Refusing to remove the match's creator from referees."),
							_ => new RefereeRemovalResult(userId, false, "userId is not a referee of this match.")
						});
					}

					return Results.Json(results);
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("removeMatchReferees")
			.WithSummary("Remove match referees.")
			.WithDescription("""
			                 Removes `{ userIds: int[] }` from the match's referees, returning one `{ userId, ok, error }` result per target. A target must be online to be removed.

			                 Returns `200 OK` even if some targets failed -- see each result's `ok`/`error`. Returns `400 Bad Request` if `userIds` is empty, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Referees")
			.Produces<IReadOnlyList<RefereeRemovalResult>>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new List<RefereeRemovalResult>
			{
				new(13, true, null),
				new(21, false, "Refusing to leave the match with no referees.")
			})
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userIds is required."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>
	///     Resolves a list of numeric user ids into their online <see cref="UserSession" />s. The moment
	///     any id is missing or offline, it bails out with a 400 <c>IResult</c> as the error half.
	/// </summary>
	private static (IReadOnlyCollection<UserSession> Targets, IResult? Error) ResolveOnlineTargets(
		IReadOnlyList<int> userIds, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry)
	{
		var targets = new List<UserSession>();
		foreach (var userId in userIds)
		{
			var target = (UserSession?)gameRegistry.GetByUserId(userId) ?? ircRegistry.GetByUserId(userId);
			if (target is null)
				return (targets,
					Results.BadRequest(new ErrorResponse($"userId {userId} is required and must be online.")));

			targets.Add(target);
		}

		return (targets, null);
	}
}

/// <summary>Request body for `PUT /matches/{matchId}/refs`: replaces the whole referee list.</summary>
public sealed record ReplaceRefereesRequest(IReadOnlyList<int> UserIds);

/// <summary>Request body for `PATCH /matches/{matchId}/refs`: adds to the referee list.</summary>
public sealed record UpdateRefereesRequest(IReadOnlyList<int> UserIds);

/// <summary>Request body for `DELETE /matches/{matchId}/refs`: the targets to remove.</summary>
public sealed record RemoveRefereesRequest(IReadOnlyList<int> UserIds);

/// <summary>Per-target outcome returned by `DELETE /matches/{matchId}/refs`.</summary>
public sealed record RefereeRemovalResult(int UserId, bool Ok, string? Error);

/// <summary>Per-target outcome returned by `PATCH /matches/{matchId}/refs`.</summary>
public sealed record RefereeAdditionResult(int UserId, bool Ok, string? Error);
