using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Formats;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web.Auth;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints for a match's hosts, referees, bans, slots, timer, abort, and
///     close actions.
/// </summary>
/// <remarks>
///     Each resource is publicly readable as plain JSON (404 if the match isn't currently live) or as
///     a server-sent-events stream on its `/live` sibling (409 if the match isn't currently live).
///     Every write requires administrator authorization.
/// </remarks>
internal static class MatchSubResourceRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/matches/{matchId}` host, referees, ban, slots, timer, abort, and close
	///     sub-routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchSubResourceRoutes(this RouteGroupBuilder group)
	{
		MapHosts(group);
		MapRefs(group);
		MapBans(group);
		MapSlots(group);
		MapTimer(group);
		MapAbort(group);
		MapClose(group);
		MapChat(group);
	}

	/// <summary>Registers the `/matches/{matchId}/chat` stream and send routes.</summary>
	private static void MapChat(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/chat/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				return match is not null
					? LiveSseRoutes.HandleChat(context, match, events, cancellationToken)
					: LiveSseRoutes.NotLive();
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("getMatchChatLive")
			.WithSummary("Stream match chat.")
			.WithDescription("""
			                 Server-Sent Events stream of every line said in the match's own chat, whoever said
			                 it and however they are connected — an osu! client, an IRC client, or BasilBot
			                 answering a command.

			                 Chat is never stored, so the stream carries only what is said from the moment it
			                 opens; there is no history to read back and no plain-JSON sibling of this route.

			                 The admin key travels in the `Authorization` header, which a browser's built-in
			                 `EventSource` cannot set — consume this from a server-side client, or a client that
			                 supports request headers.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Chat")
			.Produces<MatchChatMessage>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchChatMessage(new UserBrief(8, "Bob", Country.Gb), "glhf",
					DateTimeOffset.Parse("2026-07-20T14:30:00Z")));

		group.MapPost("/matches/{matchId:int}/chat", (int matchId, SendMatchChatRequest body,
				IMatchRegistry matchRegistry, IChannelRegistry channelRegistry, ChatDispatchService chatDispatch) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				if (string.IsNullOrWhiteSpace(body.Text))
					return Results.BadRequest(new ErrorResponse("text must not be empty."));

				var channel = channelRegistry.GetByName(match.ChatChannelName);
				if (channel is null) return Results.NotFound();

				var sent = chatDispatch.SendAsBot(channel, body.Text);
				return sent == 0
					? Results.Json(new ErrorResponse("BasilBot is not online."),
						statusCode: StatusCodes.Status503ServiceUnavailable)
					: Results.Json(new MatchChatSentView(sent));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("sendMatchChat")
			.WithSummary("Say something in a match's chat as BasilBot.")
			.WithDescription("""
			                 Body: `{ text }`. Everyone in the room sees it as an ordinary message from
			                 BasilBot, and it appears on `GET /matches/{matchId}/chat/live` like any other line.

			                 Nothing is ever truncated: the text is split into one message per newline, and any
			                 line still too long for a single chat message is wrapped at a word boundary into as
			                 many messages as it takes. Blank lines are dropped. The response reports how many
			                 messages the text became.

			                 Returns `400 Bad Request` for empty text, `404 Not Found` if the match isn't
			                 currently live, and `503 Service Unavailable` if BasilBot is not online.
			                 """ + AdminKeyNote)
			.WithTags("Match Chat")
			.Produces<MatchChatSentView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
			.WithExample(StatusCodes.Status200OK, new MatchChatSentView(2))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("text must not be empty."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>
	///     Turns a request's per-slot assignments into the team/lock map <see cref="MatchControlService" />
	///     consumes, translating each <see cref="MatchTeam" /> into the `"Red"`/`"Blue"` strings it
	///     expects (null for a neutral team).
	/// </summary>
	/// <param name="slots">The slot assignments from the request body.</param>
	private static IReadOnlyDictionary<int, MatchControlService.SlotPatchEntry> ToPatchEntries(
		IReadOnlyList<SlotAssignment> slots)
	{
		var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>();
		foreach (var slot in slots)
		{
			var team = slot.Team switch
			{
				MatchTeam.Red => "Red",
				MatchTeam.Blue => "Blue",
				_ => null
			};
			entries[slot.Index] = new MatchControlService.SlotPatchEntry(slot.UserId, team, slot.Locked);
		}

		return entries;
	}

	/// <summary>Registers the `/matches/{matchId}/hosts` read and write routes.</summary>
	private static void MapHosts(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/hosts", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

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

		group.MapGet("/matches/{matchId:int}/hosts/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return LiveSseRoutes.NotLive();

				return LiveSseRoutes.HandleHost(context, match, events,
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
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)));

		group.MapPut("/matches/{matchId:int}/hosts", async (int matchId, SetHostRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var target = gameRegistry.GetByUserId(body.UserId);
				if (target is null)
					return Results.BadRequest(
						new ErrorResponse("userId is required and must be online with the osu! client."));

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.SetHostAsync(match, target, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMatchHost")
			.WithSummary("Set match host.")
			.WithDescription("""
			                 Makes `userId` the match host and returns the updated `{ host }`.

			                 Returns `400 Bad Request` if `userId` isn't online, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is required and must be online."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/hosts", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.ClearHostAsync(match, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
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

	/// <summary>Registers the `/matches/{matchId}/refs` read and write routes.</summary>
	private static void MapRefs(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/refs", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

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

		group.MapGet("/matches/{matchId:int}/refs/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return LiveSseRoutes.NotLive();

				return LiveSseRoutes.HandleRefs(context, match, events,
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
				new MatchRefereesView([new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)]));

		group.MapPut("/matches/{matchId:int}/refs", async (int matchId, ReplaceRefereesRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var (targets, error) = ResolveOnlineTargets(body.UserIds, gameRegistry, ircRegistry);
				if (error is not null) return error;

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = await matchControl.SetRefereesAsync(match, targets, cancellationToken);
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
				finally
				{
					match.Lock.Release();
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

		group.MapPatch("/matches/{matchId:int}/refs", async (int matchId, UpdateRefereesRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var (targets, error) = ResolveOnlineTargets(body.UserIds, gameRegistry, ircRegistry);
				if (error is not null) return error;

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.AddRefereesAsync(match, targets, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildRefs(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("addMatchReferees")
			.WithSummary("Add match referees.")
			.WithDescription("""
			                 Adds `{ userIds: int[] }` to the match's referee list and returns the updated list. Every id must be online.

			                 Returns `400 Bad Request` if any `userId` isn't online, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Referees")
			.Produces<MatchRefereesView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK,
				new MatchRefereesView([
					new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie),
					new UserBrief(9, "Carol", Country.Us)
				]))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("userId 21 is required and must be online."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/refs", async (int matchId, int? userId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				if (userId is not { } uid) return Results.BadRequest(new ErrorResponse("userId is required."));
				var target = (UserSession?)gameRegistry.GetByUserId(uid) ?? ircRegistry.GetByUserId(uid);
				if (target is null)
					return Results.BadRequest(new ErrorResponse("userId is required and must be online."));

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = await matchControl.RemoveOneRefereeAsync(null, null, match, target, cancellationToken);
					return result switch
					{
						MatchControlService.RemoveRefereeResult.WouldLeaveEmpty =>
							Results.Conflict(new ErrorResponse("Refusing to leave the match with no referees.")),
						MatchControlService.RemoveRefereeResult.NotAReferee =>
							Results.BadRequest(new ErrorResponse("userId is not a referee of this match.")),
						MatchControlService.RemoveRefereeResult.TargetIsCreator =>
							Results.Conflict(
								new ErrorResponse("Refusing to remove the match's creator from referees.")),
						_ => Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, gameRegistry, ircRegistry,
							users,
							cancellationToken))
					};
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("removeMatchReferee")
			.WithSummary("Remove a match referee.")
			.WithDescription("""
			                 Removes the referee identified by the `userId` query param and returns the updated list.

			                 Returns `400 Bad Request` if `userId` isn't a referee or isn't online, `409 Conflict` if this would leave the match with no referees or `userId` is the match's creator, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Referees")
			.Produces<MatchRefereesView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchRefereesView([new UserBrief(13, "Erin", Country.Ie)]))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not a referee of this match."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("Refusing to leave the match with no referees."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>Registers the `/matches/{matchId}/ban` read and write routes.</summary>
	private static void MapBans(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/ban", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

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

		group.MapGet("/matches/{matchId:int}/ban/live", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return LiveSseRoutes.NotLive();

				return LiveSseRoutes.HandleBans(context, match, events,
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
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]));

		group.MapPut("/matches/{matchId:int}/ban", async (int matchId, ReplaceBansRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, MatchMembershipService matchMembership,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.SetBansAsync(match, body.UserIds, cancellationToken);
					await matchMembership.EnqueueStateAsync(match, match.NextStateVersion(),
						cancellationToken: cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchBans")
			.WithSummary("Replace match bans.")
			.WithDescription("""
			                 Replaces the match's ban list with `{ userIds: int[] }` and returns the updated list. Ids need not be online. Any newly banned id that is currently seated is also kicked.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/matches/{matchId:int}/ban", async (int matchId, UpdateBansRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, MatchMembershipService matchMembership,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.AddBansAsync(match, body.UserIds, cancellationToken);
					await matchMembership.EnqueueStateAsync(match, match.NextStateVersion(),
						cancellationToken: cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("addMatchBans")
			.WithSummary("Add match bans.")
			.WithDescription("""
			                 Adds `{ userIds: int[] }` to the match's ban list and returns the updated list. Ids need not be online. Any newly banned id that is currently seated is also kicked.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.WithExample(StatusCodes.Status200OK,
				new MatchBansView([new UserBrief(21, "Mallory", Country.Ca), new UserBrief(22, "Trent", Country.Au)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/ban", async (int matchId, int? userId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				if (userId is not { } uid) return Results.BadRequest(new ErrorResponse("userId is required."));

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = await matchControl.UnbanAsync(match, uid, cancellationToken);
					return result == MatchControlService.UnbanResult.NotBanned
						? Results.BadRequest(new ErrorResponse("userId is not banned from this match."))
						: Results.Json(await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("removeMatchBan")
			.WithSummary("Remove a match ban.")
			.WithDescription("""
			                 Unbans the player identified by the `userId` query param and returns the updated list.

			                 Returns `400 Bad Request` if `userId` isn't banned from this match, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MatchBansView([]))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not banned from this match."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>Registers the `/matches/{matchId}/slots` read, reassign, kick, and invite routes.</summary>
	private static void MapSlots(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/slots", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry, users,
						cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchSlots")
			.WithSummary("Get match slots.")
			.WithDescription("""
			                 Returns the match's slots as `{ slots: [...] }`. Always 16 entries (index 0-15); `user` is null when the slot is empty.

			                 For a live stream of the same data, use `GET /matches/{matchId}/slots/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:int}/slots/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return LiveSseRoutes.NotLive();

				return LiveSseRoutes.HandleSlots(context, match, events,
					() => match.SlotsSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchSlotsLive")
			.WithSummary("Stream match slots.")
			.WithDescription("""
			                 Server-Sent Events stream of the same data as `GET /matches/{matchId}/slots`.

			                 The first event is the full current list; later events carry only the fields that changed.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSlots());

		group.MapPut("/matches/{matchId:int}/slots", (int matchId, ReplaceSlotsRequest body,
					IMatchRegistry matchRegistry,
					ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
					IUserRepository users, MatchControlService matchControl,
					CancellationToken cancellationToken) =>
				HandleSlotsWrite(matchId, body.Slots, true, matchRegistry, gameRegistry, ircRegistry, users,
					matchControl, cancellationToken))
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchSlots")
			.WithSummary("Replace match slots.")
			.WithDescription("""
			                 Reassigns the match's slots and returns the updated arrangement. `{ slots: [{ index, userId?, team?, locked? }, ...] }`.

			                 Every currently seated player's id must appear exactly once across the payload (reassignment/team/lock only; nobody may be silently added or dropped). Omitted `team` leaves that slot's existing team unchanged.

			                 Returns `400 Bad Request` if an entry sets both `userId` and `locked: true`, `409 Conflict` if the payload's player set doesn't match the match's current occupants exactly or any `userId` isn't currently seated somewhere in this match, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("An entry cannot set both userId and locked: true."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("The payload's player set doesn't match this match's current occupants."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/matches/{matchId:int}/slots", (int matchId, UpdateSlotsRequest body,
					IMatchRegistry matchRegistry,
					ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
					IUserRepository users, MatchControlService matchControl,
					CancellationToken cancellationToken) =>
				HandleSlotsWrite(matchId, body.Slots, false, matchRegistry, gameRegistry, ircRegistry, users,
					matchControl, cancellationToken))
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateMatchSlots")
			.WithSummary("Update match slots.")
			.WithDescription("""
			                 Same body and rules as `PUT /matches/{matchId}/slots`, but only the slots actually given are validated and touched; not every current occupant needs to be listed.

			                 Returns `400 Bad Request` if an entry sets both `userId` and `locked: true`, `409 Conflict` if a referenced `userId` isn't currently seated in this match, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("An entry cannot set both userId and locked: true."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("A referenced userId is not currently seated in this match."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/matches/{matchId:int}/slots", async (int matchId, InviteRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, MatchControlService matchControl,
				MatchMembershipService matchMembership, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var userIds = body.UserIds;
				if (userIds.Count == 0) return Results.BadRequest(new ErrorResponse("userIds is required."));

				var results = new List<InviteResult>();
				var anySeated = false;

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var sender = (UserSession?)gameRegistry.GetByUserId(match.HostId) ??
					             ircRegistry.GetByUserId(match.HostId) ??
					             (UserSession?)gameRegistry.GetByUserId(BotBootstrapService.BotId) ??
					             ircRegistry.GetByUserId(BotBootstrapService.BotId);

					foreach (var userId in userIds)
					{
						var target = gameRegistry.GetByUserId(userId);
						if (target is null)
						{
							results.Add(new InviteResult(userId, false, "Not online with the osu! client."));
							continue;
						}

						if (body.Force)
						{
							var forceResult = await matchControl.ForceInviteAsync(match, target, cancellationToken);
							if (forceResult == MatchControlService.ForceInviteResult.Ok) anySeated = true;
							results.Add(forceResult switch
							{
								MatchControlService.ForceInviteResult.Ok => new InviteResult(userId, true, null),
								MatchControlService.ForceInviteResult.TargetBanned =>
									new InviteResult(userId, false, "Banned from this match."),
								MatchControlService.ForceInviteResult.TargetInAnotherMatch =>
									new InviteResult(userId, false, "Already in another match."),
								MatchControlService.ForceInviteResult.TargetIsBot =>
									new InviteResult(userId, false, "Cannot invite BasilBot."),
								_ => new InviteResult(userId, false, "No free slot.")
							});
							continue;
						}

						if (sender is null)
						{
							results.Add(
								new InviteResult(userId, false, "No session available to send the invite from."));
							continue;
						}

						var inviteResult = MatchControlService.Invite(sender, match, target);
						results.Add(inviteResult switch
						{
							MatchControlService.InviteResult.TargetAlreadyInRoom =>
								new InviteResult(userId, false, "Already in the room."),
							MatchControlService.InviteResult.TargetIsBot =>
								new InviteResult(userId, false, "Cannot invite BasilBot."),
							_ => new InviteResult(userId, true, null)
						});
					}
				}
				finally
				{
					match.Lock.Release();
				}

				if (anySeated)
					await matchMembership.EnqueueStateAsync(match, match.NextStateVersion(),
						cancellationToken: cancellationToken);

				return Results.Json(results);
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("inviteMatchPlayers")
			.WithSummary("Invite players to a match.")
			.WithDescription("""
			                 Invites `{ userIds: int[], force }` to the match, returning one `{ userId, ok, error }` result per target.

			                 Without `force`, sends a standing invite (same as `!mp invite`): the target still needs to join themselves, subject to the room's password/private/lock gating. With `force: true`, bypasses password/private/lock and seats the target directly. A banned target is still rejected regardless of `force`.

			                 Returns `200 OK` even if some targets failed.

			                 Returns `400 Bad Request` if `userIds` is empty, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<IReadOnlyList<InviteResult>>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new List<InviteResult>
			{
				new(9, true, null),
				new(21, false, "Banned from this match.")
			})
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userIds is required."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/slots", async (int matchId, [FromBody] KickPlayerRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, MatchMembershipService matchMembership,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var targetUser = await users.FetchByIdAsync(body.UserId, cancellationToken);
				if (targetUser is null)
					return Results.BadRequest(new ErrorResponse("userId is not registered."));

				MatchControlService.KickResult result;
				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					result = await matchControl.KickAsync(null, null, match, targetUser.Id, targetUser.Name,
						cancellationToken);
				}
				finally
				{
					match.Lock.Release();
				}

				return result switch
				{
					MatchControlService.KickResult.TargetNotInMatch =>
						Results.BadRequest(new ErrorResponse("userId is not in this match.")),
					MatchControlService.KickResult.TargetIsReferee =>
						Results.BadRequest(new ErrorResponse("userId is a referee; remove referee status first.")),
					MatchControlService.KickResult.TargetIsBot =>
						Results.BadRequest(new ErrorResponse("userId is BasilBot and cannot be kicked.")),
					_ => await KickedResponseAsync()
				};

				async Task<IResult> KickedResponseAsync()
				{
					await matchMembership.EnqueueStateAsync(match, match.NextStateVersion(),
						cancellationToken: cancellationToken);
					return Results.Json(await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry,
						users, cancellationToken));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("kickMatchPlayer")
			.WithSummary("Kick a player from a match.")
			.WithDescription("""
			                 Kicks the player identified by `{ userId }` and returns the resulting slot arrangement.

			                 Returns `400 Bad Request` if `userId` is not registered, not currently present in this match, is a referee (remove referee status first), or is BasilBot, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not in this match."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static MatchSlotsView SampleSlots()
	{
		var slots = new List<MatchSlotView>(16);
		for (var i = 0; i < 16; i++)
			slots.Add(new MatchSlotView(i, null, SlotStatus.Open, MatchTeam.Neutral, Mods.NoMod, false, false));
		slots[0] = new MatchSlotView(0, new UserBrief(7, "Alice", Country.Us), SlotStatus.NotReady, MatchTeam.Red,
			Mods.NoMod, false, false);
		slots[1] = new MatchSlotView(1, new UserBrief(9, "Carol", Country.Ca), SlotStatus.Ready, MatchTeam.Blue,
			Mods.NoMod, true, false);
		slots[15] = new MatchSlotView(15, null, SlotStatus.Locked, MatchTeam.Neutral, Mods.NoMod, false, false);
		return new MatchSlotsView(slots);
	}

	/// <summary>
	///     Shared implementation for `PUT`/`PATCH /matches/{matchId}/slots`: validates slot indexes,
	///     converts the body to patch entries, and applies them under <see cref="MatchSession.Lock" />,
	///     mapping <see cref="MatchControlService.SetSlotsAsync" /> results onto 200/400/409 responses.
	/// </summary>
	private static async Task<IResult> HandleSlotsWrite(int matchId, IReadOnlyList<SlotAssignment> slots,
		bool isFullReplace, IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
		MatchControlService matchControl, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return Results.NotFound();

		foreach (var slot in slots)
			if (slot.Index is < 0 or > 15)
				return Results.BadRequest(new ErrorResponse($"Slot index {slot.Index} is out of range (0-15)."));

		var entries = ToPatchEntries(slots);

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var result = await matchControl.SetSlotsAsync(match, entries, isFullReplace, cancellationToken);
			return result switch
			{
				MatchControlService.SetSlotsResult.PlayerCountMismatch =>
					Results.Conflict(
						new ErrorResponse(
							"The payload's player set doesn't match this match's current occupants.")),
				MatchControlService.SetSlotsResult.UnknownUserId =>
					Results.Conflict(new ErrorResponse("A referenced userId is not currently seated in this match.")),
				MatchControlService.SetSlotsResult.SlotOccupiedAndLocked =>
					Results.BadRequest(new ErrorResponse("An entry cannot set both userId and locked: true.")),
				_ => Results.Json(
					await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry, users,
						cancellationToken))
			};
		}
		finally
		{
			match.Lock.Release();
		}
	}

	/// <summary>Registers the `/matches/{matchId}/timer` read, start, and abort routes.</summary>
	private static void MapTimer(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/timer", (int matchId, IMatchRegistry matchRegistry) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				return Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchTimer")
			.WithSummary("Get match timer.")
			.WithDescription("""
			                 Returns the match's countdown timer as `{ running, secondsRemaining, autoStart }`.

			                 For a live stream of the same data, use `GET /matches/{matchId}/timer/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.WithExample(StatusCodes.Status200OK, new MatchTimerView(true, 25, true))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:int}/timer/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return LiveSseRoutes.NotLive();

				return LiveSseRoutes.HandleTimer(context, match, events,
					() => match.TimerSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchTimerLive")
			.WithSummary("Stream match timer.")
			.WithDescription("""
			                 Server-Sent Events stream of the same data as `GET /matches/{matchId}/timer`.

			                 A change is pushed at each announcement checkpoint `!mp timer`/`!mp start` uses, plus once more when the countdown finishes or is aborted.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchTimerView(true, 25, true));

		group.MapPost("/matches/{matchId:int}/timer", async (int matchId, StartTimerRequest body,
				IMatchRegistry matchRegistry, MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					if (body.AutoStart)
					{
						var result = await matchControl.StartAsync(match, body.Seconds, cancellationToken);
						return result switch
						{
							MatchControlService.StartResult.AlreadyInProgress =>
								Results.Conflict(new ErrorResponse("Match is already in progress.")),
							MatchControlService.StartResult.BeatmapMissing =>
								Results.Conflict(new ErrorResponse(
									"Match cannot start because the beatmap does not exist on the server.")),
							_ => Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match))
						};
					}

					matchControl.Timer(match, body.Seconds > 0 ? body.Seconds : 30);
					return Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("startMatchTimer")
			.WithSummary("Start match timer.")
			.WithDescription("""
			                 Starts the match's countdown timer, from `{ seconds, autoStart }`.

			                 `autoStart: true` behaves like `!mp start [seconds]`: a positive `seconds` queues a countdown that starts the match when it finishes, while a non-positive value starts immediately. `autoStart: false` behaves like `!mp timer`: a countdown that never auto-starts (non-positive `seconds` defaults to 30).

			                 Returns `409 Conflict` if the match is already in progress or has no beatmap set, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchTimerView(true, 30, true))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is already in progress."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/timer", async (int matchId, IMatchRegistry matchRegistry,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = matchControl.AbortTimer(match);
					return result == MatchControlService.AbortTimerResult.NoTimerRunning
						? Results.Conflict(new ErrorResponse("No countdown is running."))
						: Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("abortMatchTimer")
			.WithSummary("Abort match timer.")
			.WithDescription("""
			                 Stops the running countdown.

			                 Returns `409 Conflict` if no countdown is running, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchTimerView(false, null, false))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("No countdown is running."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>Registers the `POST /matches/{matchId}/abort` route.</summary>
	private static void MapAbort(RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:int}/abort", async (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var abortedAt = DateTime.UtcNow;
					var result = await matchControl.AbortAsync(match, cancellationToken);
					if (result == MatchControlService.AbortResult.NotInProgress)
						return Results.Conflict(new ErrorResponse("Match is not in progress."));

					context.Items[EnvelopeMiddleware.EnvelopeMessageKey] = "Match aborted.";
					return Results.Json(new MatchAbortedView(matchId, abortedAt));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("abortMatch")
			.WithSummary("Abort a match in progress.")
			.WithDescription("""
			                 Aborts the match's current round and returns a confirmation body with the abort time.

			                 Players in the match are notified over both the multiplayer protocol and the match's chat channel.

			                 Returns `409 Conflict` if the match is not in progress, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Abort")
			.Produces<MatchAbortedView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchAbortedView(42, DateTime.Parse("2026-07-20T14:30:00Z")))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not in progress."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>Registers the `POST /matches/{matchId}/close` route.</summary>
	private static void MapClose(RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:int}/close", async (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var endedAt = DateTime.UtcNow;
					await matchControl.CloseAsync(null, null, match, cancellationToken);
					context.Items[EnvelopeMiddleware.EnvelopeMessageKey] = "Match closed.";
					return Results.Json(new MatchClosedView(matchId, endedAt));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("closeMatch")
			.WithSummary("Close a match.")
			.WithDescription("""
			                 Closes the match and returns a confirmation body with its end time.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Close")
			.Produces<MatchClosedView>()
			.WithExample(StatusCodes.Status200OK, new MatchClosedView(42, DateTime.Parse("2026-07-20T14:30:00Z")))
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

	/// <summary>Request body for `POST /matches/{matchId}/chat`.</summary>
	/// <param name="Text">The text BasilBot says in the room.</param>
	public sealed record SendMatchChatRequest(string Text);

	/// <summary>Confirmation body for `POST /matches/{matchId}/chat`.</summary>
	/// <param name="Sent">The number of chat messages the text became.</param>
	public sealed record MatchChatSentView(int Sent);

	/// <summary>Request body for `PUT /matches/{matchId}/hosts`.</summary>
	public sealed record SetHostRequest(int UserId);

	/// <summary>Request body for `PUT /matches/{matchId}/refs`: replaces the whole referee list.</summary>
	public sealed record ReplaceRefereesRequest(IReadOnlyList<int> UserIds);

	/// <summary>Request body for `PATCH /matches/{matchId}/refs`: adds to the referee list.</summary>
	public sealed record UpdateRefereesRequest(IReadOnlyList<int> UserIds);

	/// <summary>Request body for `PUT /matches/{matchId}/ban`: replaces the whole ban list.</summary>
	public sealed record ReplaceBansRequest(IReadOnlyList<int> UserIds);

	/// <summary>Request body for `PATCH /matches/{matchId}/ban`: adds to the ban list.</summary>
	public sealed record UpdateBansRequest(IReadOnlyList<int> UserIds);

	/// <summary>Request body for `DELETE /matches/{matchId}/slots`: kicks the seated player.</summary>
	public sealed record KickPlayerRequest(int UserId);

	/// <summary>
	///     Request body for `POST /matches/{matchId}/slots`: one target per id, optionally forced
	///     straight into the room.
	/// </summary>
	public sealed record InviteRequest(IReadOnlyList<int> UserIds, bool Force);

	/// <summary>Per-target outcome returned by `POST /matches/{matchId}/slots`.</summary>
	public sealed record InviteResult(int UserId, bool Ok, string? Error);

	/// <summary>Request body for `POST /matches/{matchId}/timer`.</summary>
	public sealed record StartTimerRequest(int Seconds, bool AutoStart);

	/// <summary>
	///     One per-slot entry shared by <see cref="ReplaceSlotsRequest" /> and <see cref="UpdateSlotsRequest" />:
	///     a partial shape for a single slot, either way.
	/// </summary>
	public sealed record SlotAssignment(int Index, int? UserId = null, MatchTeam? Team = null, bool? Locked = null);

	/// <summary>Request body for `PUT /matches/{matchId}/slots`: every seated player must appear exactly once.</summary>
	public sealed record ReplaceSlotsRequest(IReadOnlyList<SlotAssignment> Slots);

	/// <summary>Request body for `PATCH /matches/{matchId}/slots`: only the given slots are validated and touched.</summary>
	public sealed record UpdateSlotsRequest(IReadOnlyList<SlotAssignment> Slots);
}