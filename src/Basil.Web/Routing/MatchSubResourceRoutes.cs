using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Json;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web.Auth;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Web.Routing;

/// <summary>
///     `/matches/{matchId}/...` per-resource sub-routes replacing the old generic
///     `POST /matches/{matchId}/{action}` dispatch (see <see cref="MatchRoutes" />, which still owns
///     `/matches`, `/matches/{matchId}/settings`, and the merged main live SSE channel). Every resource
///     here follows the same JSON-vs-SSE path split as `/settings`: the bare path is always plain JSON
///     (404 if the match isn't currently live), and a `.../live` sibling is always SSE (409, enveloped,
///     if the match isn't currently live — never a stream that would never receive a frame). Reads are
///     public; every write is admin-key gated. Every write handler resolves the match, 404s if it isn't
///     currently live, then holds <see cref="MatchSession.Lock" /> across the whole
///     read-mutate-broadcast sequence, exactly like every other match write in this codebase.
/// </summary>
internal static class MatchSubResourceRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	public static void MapMatchSubResourceRoutes(this RouteGroupBuilder group)
	{
		MapHosts(group);
		MapRefs(group);
		MapBans(group);
		MapSlots(group);
		MapTimer(group);
		MapAbort(group);
		MapClose(group);
	}

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

	// ---- /hosts ----

	private static void MapHosts(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/hosts", async (int matchId, IMatchRegistry matchRegistry,
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, users, cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchHost")
			.WithSummary("Get Match Host")
			.WithDescription("Plain JSON `{ host }` (`host` null when the room has no host, else the full " +
			                 "`{ id, name, country }` embed). 404 if the match isn't currently live. For a live push " +
			                 "stream of the same shape, see `GET /matches/{matchId}/hosts/live`. Public, no authentication.")
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

				return LiveSseRoutes.HandleHost(context, matchId, events,
					() => match.HostSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithMetadata(SseEndpointMarker.Instance)
			.WithName("getMatchHostLive")
			.WithSummary("Get Match Host Live Stream")
			.WithDescription("Full-then-delta SSE stream (event name `hosts`) of the same shape as " +
			                 "`GET /matches/{matchId}/hosts`. 409 (enveloped) if the match isn't currently live. Public, " +
			                 "no authentication.")
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)));

		group.MapPut("/matches/{matchId:int}/hosts", async (int matchId, SetHostRequest body,
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var target = sessionRegistry.GetById(body.UserId);
				if (target is null)
					return Results.BadRequest(new ErrorResponse("userId is required and must be online."));

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.SetHostAsync(match, target, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, users, cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMatchHost")
			.WithSummary("Set Match Host")
			.WithDescription("Body: `{ userId }`. 404 if the match isn't currently live; 400 if `userId` " +
			                 "isn't online." + AdminKeyNote)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", Country.Us)))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is required and must be online."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/hosts", async (int matchId, IMatchRegistry matchRegistry,
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.ClearHostAsync(match, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, users, cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("clearMatchHost")
			.WithSummary("Clear Match Host")
			.WithDescription("Sets the host back to id 0. 404 if the match isn't currently live." + AdminKeyNote)
			.WithTags("Match Hosts")
			.Produces<MatchHostView>()
			.WithExample(StatusCodes.Status200OK, new MatchHostView(null))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	// ---- /refs ----

	private static void MapRefs(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/refs", async (int matchId, IMatchRegistry matchRegistry,
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users, cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("listMatchReferees")
			.WithSummary("List Match Referees")
			.WithDescription("Plain JSON `{ referees: [{ id, name, country }] }`. 404 if the match isn't " +
			                 "currently live. For a live push stream of the same shape, see " +
			                 "`GET /matches/{matchId}/refs/live`. Public, no authentication.")
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

				return LiveSseRoutes.HandleRefs(context, matchId, events,
					() => match.RefsSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithMetadata(SseEndpointMarker.Instance)
			.WithName("getMatchRefereesLive")
			.WithSummary("Get Match Referees Live Stream")
			.WithDescription("Full-then-delta SSE stream (event name `refs`) of the same shape as " +
			                 "`GET /matches/{matchId}/refs`. 409 (enveloped) if the match isn't currently live. Public, " +
			                 "no authentication.")
			.WithTags("Match Referees")
			.Produces<MatchRefereesView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchRefereesView([new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)]));

		group.MapPut("/matches/{matchId:int}/refs", async (int matchId, ReplaceRefereesRequest body,
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var (targets, error) = ResolveOnlineTargets(body.UserIds, sessionRegistry);
				if (error is not null) return error;

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = await matchControl.SetRefereesAsync(match, targets, cancellationToken);
					return result == MatchControlService.SetRefereesResult.WouldLeaveEmpty
						? Results.Conflict(new ErrorResponse("Refusing to leave the match with no referees."))
						: Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchReferees")
			.WithSummary("Replace Match Referees")
			.WithDescription("Body: `{ userIds: int[] }`. Full replace, every id must be online. 409 if the " +
			                 "result would leave the match with zero referees. 404 if the match isn't currently live; 400 " +
			                 "if any `userId` isn't online." + AdminKeyNote)
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
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var (targets, error) = ResolveOnlineTargets(body.UserIds, sessionRegistry);
				if (error is not null) return error;

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.AddRefereesAsync(match, targets, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users, cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("addMatchReferees")
			.WithSummary("Add Match Referees")
			.WithDescription("Body: `{ userIds: int[] }`. Adds to the existing referee list, every id must " +
			                 "be online. Never rejected for leaving the list empty (it only ever adds). 404 if the match " +
			                 "isn't currently live; 400 if any `userId` isn't online." + AdminKeyNote)
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
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				if (userId is not { } uid) return Results.BadRequest(new ErrorResponse("userId is required."));
				var target = sessionRegistry.GetById(uid);
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
						_ => Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users,
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
			.WithSummary("Remove Match Referee")
			.WithDescription("Query param `userId` (required, must be online). 409 if this would leave the " +
			                 "match with zero referees; 400 if `userId` isn't a referee or isn't online. 404 if the match " +
			                 "isn't currently live." + AdminKeyNote)
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

	// ---- /ban ----

	private static void MapBans(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/ban", async (int matchId, IMatchRegistry matchRegistry,
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("listMatchBans")
			.WithSummary("List Match Bans")
			.WithDescription("Plain JSON `{ bannedUsers: [{ id, name, country }] }` (a currently-offline " +
			                 "banned id that has no registered account is simply omitted). 404 if the match isn't " +
			                 "currently live. For a live push stream of the same shape, see " +
			                 "`GET /matches/{matchId}/ban/live`. Public, no authentication.")
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:int}/ban/live", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return LiveSseRoutes.NotLive();

				return LiveSseRoutes.HandleBans(context, matchId, events,
					() => match.BansSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithMetadata(SseEndpointMarker.Instance)
			.WithName("getMatchBansLive")
			.WithSummary("Get Match Bans Live Stream")
			.WithDescription("Full-then-delta SSE stream (event name `ban`) of the same shape as " +
			                 "`GET /matches/{matchId}/ban`. 409 (enveloped) if the match isn't currently live. Public, " +
			                 "no authentication.")
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]));

		group.MapPut("/matches/{matchId:int}/ban", async (int matchId, ReplaceBansRequest body,
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.SetBansAsync(match, body.UserIds, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchBans")
			.WithSummary("Replace Match Bans")
			.WithDescription("Body: `{ userIds: int[] }`. Full replace, ids need not be online. No empty " +
			                 "guard (banning down to zero is fine). Any newly-banned id currently seated is also kicked. " +
			                 "404 if the match isn't currently live." + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", Country.Ca)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/matches/{matchId:int}/ban", async (int matchId, UpdateBansRequest body,
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					await matchControl.AddBansAsync(match, body.UserIds, cancellationToken);
					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("addMatchBans")
			.WithSummary("Add Match Bans")
			.WithDescription("Body: `{ userIds: int[] }`. Adds to the existing ban list, ids need not be " +
			                 "online. Any newly-banned id currently seated is also kicked. 404 if the match isn't " +
			                 "currently live." + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.WithExample(StatusCodes.Status200OK,
				new MatchBansView([new UserBrief(21, "Mallory", Country.Ca), new UserBrief(22, "Trent", Country.Au)]))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:int}/ban", async (int matchId, int? userId, IMatchRegistry matchRegistry,
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
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
						: Results.Json(await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users,
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
			.WithSummary("Remove Match Ban")
			.WithDescription("Query param `userId` (required). 400 if `userId` isn't banned from this match. " +
			                 "404 if the match isn't currently live." + AdminKeyNote)
			.WithTags("Match Bans")
			.Produces<MatchBansView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MatchBansView([]))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not banned from this match."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	// ---- /slots (view/reassign + kick/invite) ----

	private static void MapSlots(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:int}/slots", async (int matchId, IMatchRegistry matchRegistry,
				IPlayerSessionRegistry sessionRegistry, IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry, users, cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchSlots")
			.WithSummary("Get Match Slots")
			.WithDescription("Plain JSON `{ slots: [{ index, user, status, team, mods, ready, loaded }, " +
			                 "...] }`. Always 16 entries (index 0-15), `user` a `{ id, name, country }` embed or null " +
			                 "when empty. 404 if the match isn't currently live. For a live push stream of the same " +
			                 "shape, see `GET /matches/{matchId}/slots/live`. Public, no authentication.")
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

				return LiveSseRoutes.HandleSlots(context, matchId, events,
					() => match.SlotsSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithMetadata(SseEndpointMarker.Instance)
			.WithName("getMatchSlotsLive")
			.WithSummary("Get Match Slots Live Stream")
			.WithDescription("Full-then-delta SSE stream (event name `slots`) of the same shape as " +
			                 "`GET /matches/{matchId}/slots`. 409 (enveloped) if the match isn't currently live. Public, " +
			                 "no authentication.")
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSlots());

		group.MapPut("/matches/{matchId:int}/slots", (int matchId, ReplaceSlotsRequest body,
					IMatchRegistry matchRegistry,
					IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
					CancellationToken cancellationToken) =>
				HandleSlotsWrite(matchId, body.Slots, true, matchRegistry, sessionRegistry, users,
					matchControl, cancellationToken))
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchSlots")
			.WithSummary("Replace Match Slots")
			.WithDescription("Body: `{ slots: [{ index, userId?, team?, locked? }, ...] }`. Every " +
			                 "currently-seated player's id must appear exactly once across the payload (reassignment/" +
			                 "team/lock only, nobody may be silently added or dropped). Omitted `team` leaves that " +
			                 "slot's existing team unchanged. 409 (`PlayerCountMismatch`) if the payload's player set " +
			                 "doesn't match the match's current occupants exactly, or (`UnknownUserId`) if any `userId` " +
			                 "isn't currently seated somewhere in this match; 400 (`SlotOccupiedAndLocked`) if an entry " +
			                 "sets both `userId` and `locked: true`. 404 if the match isn't currently live." +
			                 AdminKeyNote)
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
					IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
					CancellationToken cancellationToken) =>
				HandleSlotsWrite(matchId, body.Slots, false, matchRegistry, sessionRegistry, users,
					matchControl, cancellationToken))
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateMatchSlots")
			.WithSummary("Update Match Slots")
			.WithDescription("Same body/rules as `PUT`, but only validates/touches the slots actually given, and " +
			                 "does not require every current occupant to be listed." + AdminKeyNote)
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
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var userIds = body.UserIds;
				if (userIds.Count == 0) return Results.BadRequest(new ErrorResponse("userIds is required."));

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var results = new List<InviteResult>();
					var sender = sessionRegistry.GetById(match.HostId) ??
					             sessionRegistry.GetById(BotBootstrapService.BotId);

					foreach (var userId in userIds)
					{
						var target = sessionRegistry.GetById(userId);
						if (target is null)
						{
							results.Add(new InviteResult(userId, false, "Not online."));
							continue;
						}

						if (body.Force)
						{
							var forceResult = await matchControl.ForceInviteAsync(match, target, cancellationToken);
							results.Add(forceResult switch
							{
								MatchControlService.ForceInviteResult.Ok => new InviteResult(userId, true, null),
								MatchControlService.ForceInviteResult.TargetBanned =>
									new InviteResult(userId, false, "Banned from this match."),
								MatchControlService.ForceInviteResult.TargetInAnotherMatch =>
									new InviteResult(userId, false, "Already in another match."),
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

						var inviteResult = matchControl.Invite(sender, match, target);
						results.Add(inviteResult == MatchControlService.InviteResult.TargetAlreadyInRoom
							? new InviteResult(userId, false, "Already in the room.")
							: new InviteResult(userId, true, null));
					}

					return Results.Json(results);
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("inviteMatchPlayers")
			.WithSummary("Invite Match Players")
			.WithDescription("Body: `{ userIds: int[], force }`. Without `force`, sends a standing invite " +
			                 "(same as `!mp invite`): the target still needs to join themselves, subject to the room's " +
			                 "password/private/lock gating. With `force: true`, bypasses password/private/lock and seats " +
			                 "the target directly. A banned target is still rejected regardless of `force`. Partial-" +
			                 "failure-safe: returns one `{ userId, ok, error }` result per target, 200 even if some " +
			                 "targets failed. 404 if the match isn't currently live; 400 if `userIds` is empty." +
			                 AdminKeyNote)
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
				IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				var target = sessionRegistry.GetById(body.UserId);
				if (target is null)
					return Results.BadRequest(new ErrorResponse("userId is required and must be online."));

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = await matchControl.KickAsync(null, null, match, target, cancellationToken);
					return result == MatchControlService.KickResult.TargetNotInMatch
						? Results.BadRequest(new ErrorResponse("userId is not in this match."))
						: Results.Json(await MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry, users,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("kickMatchPlayer")
			.WithSummary("Kick Match Player")
			.WithDescription("Body: `{ userId }`. Returns the post-kick slot arrangement. 404 if the match " +
			                 "isn't currently live; 400 if `userId` is missing/not online, or isn't currently seated in " +
			                 "this match." + AdminKeyNote)
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

	private static async Task<IResult> HandleSlotsWrite(int matchId, IReadOnlyList<SlotAssignment> slots,
		bool isFullReplace, IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
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
						new ErrorResponse("The payload's player set doesn't match this match's current occupants.")),
				MatchControlService.SetSlotsResult.UnknownUserId =>
					Results.Conflict(new ErrorResponse("A referenced userId is not currently seated in this match.")),
				MatchControlService.SetSlotsResult.SlotOccupiedAndLocked =>
					Results.BadRequest(new ErrorResponse("An entry cannot set both userId and locked: true.")),
				_ => Results.Json(
					await MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry, users, cancellationToken))
			};
		}
		finally
		{
			match.Lock.Release();
		}
	}

	// ---- /timer ----

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
			.WithSummary("Get Match Timer")
			.WithDescription("Plain JSON `{ running, secondsRemaining, autoStart }`. 404 if the match isn't " +
			                 "currently live. For a live push stream of the same shape, see " +
			                 "`GET /matches/{matchId}/timer/live`. Public, no authentication.")
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

				return LiveSseRoutes.HandleTimer(context, matchId, events,
					() => match.TimerSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithMetadata(SseEndpointMarker.Instance)
			.WithName("getMatchTimerLive")
			.WithSummary("Get Match Timer Live Stream")
			.WithDescription("Full-then-delta SSE stream (event name `timer`) of the same shape as " +
			                 "`GET /matches/{matchId}/timer`. A delta fires at each of the same announcement " +
			                 "checkpoints `!mp timer`/`!mp start` chat announcements use, plus once more when the " +
			                 "countdown finishes or is aborted. 409 (enveloped) if the match isn't currently live. " +
			                 "Public, no authentication.")
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
			.WithSummary("Start Match Timer")
			.WithDescription("Body: `{ seconds, autoStart }`. `autoStart: true` forwards to the same logic " +
			                 "as `!mp start [seconds]`. Non-positive `seconds` starts immediately, a positive value " +
			                 "queues a countdown that starts the match when it finishes. `autoStart: false` forwards to " +
			                 "`!mp timer`, a countdown that never auto-starts (non-positive `seconds` defaults to 30). " +
			                 "409 if the match is already in progress or has no beatmap set. 404 if the match isn't " +
			                 "currently live." + AdminKeyNote)
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
			.WithSummary("Abort Match Timer")
			.WithDescription("409 if no countdown is running. 404 if the match isn't currently live." + AdminKeyNote)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchTimerView(false, null, false))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("No countdown is running."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	// ---- /abort ----

	private static void MapAbort(RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:int}/abort", async (int matchId, IMatchRegistry matchRegistry,
				MatchControlService matchControl, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
				IMapRepository maps, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var result = await matchControl.AbortAsync(match, cancellationToken);
					if (result == MatchControlService.AbortResult.NotInProgress)
						return Results.Conflict(new ErrorResponse("Match is not in progress."));

					return Results.Json(
						await MatchLiveSnapshotBuilder.BuildMain(match, sessionRegistry, users, maps,
							cancellationToken));
				}
				finally
				{
					match.Lock.Release();
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("abortMatch")
			.WithSummary("Abort Match")
			.WithDescription("Returns the post-abort live state. 409 if the match is not in progress. 404 " +
			                 "if the match isn't currently live." + AdminKeyNote)
			.WithTags("Match Abort")
			.Produces<MatchLiveSnapshot>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, MatchRoutes.SampleLiveSnapshot())
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not in progress."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	// ---- /close ----

	private static void MapClose(RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:int}/close", async (int matchId, IMatchRegistry matchRegistry,
				MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound();

				await match.Lock.WaitAsync(cancellationToken);
				try
				{
					var endedAt = DateTime.UtcNow;
					await matchControl.CloseAsync(null, null, match, cancellationToken);
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
			.WithSummary("Close Match")
			.WithDescription("Returns a confirmation body. 404 if the match isn't currently live." + AdminKeyNote)
			.WithTags("Match Close")
			.Produces<MatchClosedView>()
			.WithExample(StatusCodes.Status200OK, new MatchClosedView(42, DateTime.Parse("2026-07-20T14:30:00Z")))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static (IReadOnlyCollection<PlayerSession> Targets, IResult? Error) ResolveOnlineTargets(
		IReadOnlyList<int> userIds, IPlayerSessionRegistry sessionRegistry)
	{
		var targets = new List<PlayerSession>();
		foreach (var userId in userIds)
		{
			var target = sessionRegistry.GetById(userId);
			if (target is null)
				return (targets,
					Results.BadRequest(new ErrorResponse($"userId {userId} is required and must be online.")));

			targets.Add(target);
		}

		return (targets, null);
	}

	public sealed record SetHostRequest(int UserId);

	public sealed record ReplaceRefereesRequest(IReadOnlyList<int> UserIds);

	public sealed record UpdateRefereesRequest(IReadOnlyList<int> UserIds);

	public sealed record ReplaceBansRequest(IReadOnlyList<int> UserIds);

	public sealed record UpdateBansRequest(IReadOnlyList<int> UserIds);

	public sealed record KickPlayerRequest(int UserId);

	public sealed record InviteRequest(IReadOnlyList<int> UserIds, bool Force);

	public sealed record InviteResult(int UserId, bool Ok, string? Error);

	public sealed record StartTimerRequest(int Seconds, bool AutoStart);

	/// <summary>
	///     One per-slot entry shared by <see cref="ReplaceSlotsRequest" />/<see cref="UpdateSlotsRequest" /> — inherently
	///     a per-slot partial shape either way.
	/// </summary>
	public sealed record SlotAssignment(int Index, int? UserId = null, MatchTeam? Team = null, bool? Locked = null);

	public sealed record ReplaceSlotsRequest(IReadOnlyList<SlotAssignment> Slots);

	public sealed record UpdateSlotsRequest(IReadOnlyList<SlotAssignment> Slots);
}