using System.Text.Json;
using Basil.Domain.Login;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}/hosts` read and write routes.</summary>
internal static class MatchHostEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match host routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchHosts(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/hosts", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, users,
						cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchHost")
			.WithSummary("Get match host.")
			.WithDescription("""
			                 Returns the match's host as `{ host }`. `host` is null when the room has none.

			                 For a live stream of the same data, use `GET /matches/{matchId}/hosts/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/hosts/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return SseEndpoints.NotLive();

				return MatchLiveRoutes.HandleHost(context, match, events,
					() => match.HostSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchHostLive")
			.WithSummary("Stream match host.")
			.WithDescription("""
			                 Server-Sent Events stream of the same data as `GET /matches/{matchId}/hosts`.

			                 The first event is the full current host; later events carry only the fields that changed.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapPut("/matches/{matchId:numericid}/hosts", async (int matchId, SetHostRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var target = gameRegistry.GetByUserId(body.UserId);
				if (target is null)
					return Results.BadRequest(
						new ErrorResponse("userId is required and must be online with the osu! client."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var result = await matchControl.SetHostAsync(match, target, mutation, cancellationToken);
					if (result == MatchControlService.SetHostResult.TargetNotInMatch)
						return Results.BadRequest(new ErrorResponse("userId must be seated in this match."));

					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMatchHost")
			.WithSummary("Set match host.")
			.WithDescription("""
			                 Makes `userId` the match host and returns the updated `{ host }`.

			                 Returns `400 Bad Request` if `userId` isn't online or isn't seated in this match, or
			                 `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is required and must be online."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:numericid}/hosts", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					await matchControl.ClearHostAsync(match, mutation, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("clearMatchHost")
			.WithSummary("Clear match host.")
			.WithDescription("""
			                 Clears the host, returning `{ host: null }`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.WithExample(StatusCodes.Status200OK, new MatchHostView(null))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}
}

/// <summary>Request body for `PUT /matches/{matchId}/hosts`.</summary>
public sealed record SetHostRequest(int UserId);