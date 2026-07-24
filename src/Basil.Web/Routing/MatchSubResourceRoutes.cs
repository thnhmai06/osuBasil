using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Web.Auth;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;

namespace Basil.Web.Routing;

/// <summary>
///     `/matches/{matchId}/...` per-resource sub-routes replacing the old generic
///     `POST /matches/{matchId}/{action}` dispatch (see <see cref="MatchRoutes" />, which still owns
///     `/matches`, `/matches/{matchId}/settings`, and the live SSE channels). Reads are public; every
///     write is admin-key gated. Every write handler resolves the match, 404s if it isn't currently
///     live, then holds <see cref="MatchSession.Lock" /> across the whole read-mutate-broadcast
///     sequence, exactly like every other match write in this codebase.
/// </summary>
internal static class MatchSubResourceRoutes
{
    private const string AdminKeyNote = RouteDocs.AdminKeyNote;
    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>EventSource always sends this — the same content-negotiation convention `GET /matches/{matchId}` itself uses.</summary>
    private static bool WantsSse(HttpContext context)
    {
        return context.Request.Headers.Accept.Any(a => a?.Contains("text/event-stream") == true);
    }

    public static void MapMatchSubResourceRoutes(this RouteGroupBuilder group)
    {
        MapHosts(group);
        MapRefs(group);
        MapBans(group);
        MapKick(group);
        MapInvite(group);
        MapSlots(group);
        MapTimer(group);
        MapAbort(group);
        MapClose(group);
    }

    public sealed record TargetUserIdBody(int? UserId);

    public sealed record TargetUserIdsBody(IReadOnlyList<int>? UserIds);

    public sealed record InviteBody(IReadOnlyList<int>? UserIds, bool? Force);

    public sealed record InviteTargetResult(int UserId, bool Ok, string? Error);

    public sealed record TimerBody(int? Seconds, bool? Start);

    public sealed record SlotsBody(IReadOnlyDictionary<int, MatchControlService.SlotPatchEntry>? Slots);

    // ---- /hosts ----

    private static void MapHosts(RouteGroupBuilder group)
    {
        group.MapGet("/matches/{matchId:int}/hosts", async (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMatchLiveEvents events,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleHost(context, matchId, events,
                        () => match.HostSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, users, cancellationToken));
            })
            .WithGroupName("basilapi")
            .WithName("getMatchHost")
            .WithSummary("Get Match Host")
            .WithDescription("Content-negotiated on the `Accept` header, same convention as `GET /matches/" +
                "{matchId}`: a plain `GET` returns `{ host }` (`host` null when the room has no host, else " +
                "the full `{ id, name, country }` embed); `Accept: text/event-stream` opens a full-then-delta " +
                "SSE stream (event name `hosts`) instead. 404 if the match isn't currently live. Public, no " +
                "authentication.")
            .WithTags("Match Hosts")
            .Produces<MatchHostView>()
            .WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", "us")))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/matches/{matchId:int}/hosts", async (int matchId, TargetUserIdBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
                MatchControlService matchControl, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (body.UserId is not { } userId) return Results.BadRequest(new ErrorResponse("userId is required."));
                var target = sessionRegistry.GetById(userId);
                if (target is null) return Results.BadRequest(new ErrorResponse("userId is required and must be online."));

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    await matchControl.SetHost(match, target);
                    return Results.Json(await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, users, cancellationToken));
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
            .WithDescription("Body: `{ userId }`. 404 if the match isn't currently live; 400 if `userId` is " +
                "missing or not online." + AdminKeyNote)
            .WithTags("Match Hosts")
            .Produces<MatchHostView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, new MatchHostView(new UserBrief(7, "Alice", "us")))
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
                    await matchControl.ClearHost(match);
                    return Results.Json(await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, users, cancellationToken));
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
        group.MapGet("/matches/{matchId:int}/refs", async (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMatchLiveEvents events,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleRefs(context, matchId, events,
                        () => match.RefsSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users, cancellationToken));
            })
            .WithGroupName("basilapi")
            .WithName("listMatchReferees")
            .WithSummary("List Match Referees")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns `{ referees: " +
                "[{ id, name, country }] }`; `Accept: text/event-stream` opens a full-then-delta SSE stream " +
                "(event name `refs`) instead. 404 if the match isn't currently live. Public, no authentication.")
            .WithTags("Match Referees")
            .Produces<MatchRefereesView>()
            .WithExample(StatusCodes.Status200OK, new MatchRefereesView([new UserBrief(8, "Bob", "gb"), new UserBrief(13, "Erin", "ie")]))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/matches/{matchId:int}/refs", async (int matchId, TargetUserIdsBody body,
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
                        : Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users, cancellationToken));
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
            .WithDescription("Body: `{ userIds: int[] }` — full replace, every id must be online. 409 if the " +
                "result would leave the match with zero referees. 404 if the match isn't currently live; 400 " +
                "if any `userId` isn't online." + AdminKeyNote)
            .WithTags("Match Referees")
            .Produces<MatchRefereesView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status200OK, new MatchRefereesView([new UserBrief(8, "Bob", "gb"), new UserBrief(13, "Erin", "ie")]))
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId 21 is required and must be online."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Refusing to leave the match with no referees."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/matches/{matchId:int}/refs", async (int matchId, TargetUserIdsBody body,
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
                    return Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users, cancellationToken));
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
            .WithDescription("Body: `{ userIds: int[] }` — adds to the existing referee list, every id must " +
                "be online. Never rejected for leaving the list empty (it only ever adds). 404 if the match " +
                "isn't currently live; 400 if any `userId` isn't online." + AdminKeyNote)
            .WithTags("Match Referees")
            .Produces<MatchRefereesView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, new MatchRefereesView([new UserBrief(8, "Bob", "gb"), new UserBrief(13, "Erin", "ie"), new UserBrief(9, "Carol", "us")]))
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId 21 is required and must be online."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/matches/{matchId:int}/refs", async (int matchId, int? userId, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (userId is not { } uid) return Results.BadRequest(new ErrorResponse("userId is required."));
                var target = sessionRegistry.GetById(uid);
                if (target is null) return Results.BadRequest(new ErrorResponse("userId is required and must be online."));

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
                        _ => Results.Json(await MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry, users, cancellationToken))
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
            .WithExample(StatusCodes.Status200OK, new MatchRefereesView([new UserBrief(13, "Erin", "ie")]))
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not a referee of this match."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Refusing to leave the match with no referees."))
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---- /ban ----

    private static void MapBans(RouteGroupBuilder group)
    {
        group.MapGet("/matches/{matchId:int}/ban", async (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMatchLiveEvents events,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleBans(context, matchId, events,
                        () => match.BansSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
            })
            .WithGroupName("basilapi")
            .WithName("listMatchBans")
            .WithSummary("List Match Bans")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns " +
                "`{ bannedUsers: [{ id, name, country }] }` (a currently-offline banned id that has no " +
                "registered account is simply omitted); `Accept: text/event-stream` opens a full-then-delta " +
                "SSE stream (event name `ban`) instead. 404 if the match isn't currently live. Public, no " +
                "authentication.")
            .WithTags("Match Bans")
            .Produces<MatchBansView>()
            .WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", "ca")]))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/matches/{matchId:int}/ban", async (int matchId, TargetUserIdsBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
                MatchControlService matchControl, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    await matchControl.SetBans(match, body.UserIds ?? []);
                    return Results.Json(await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
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
            .WithDescription("Body: `{ userIds: int[] }` — full replace, ids need not be online. No empty " +
                "guard (banning down to zero is fine). Any newly-banned id currently seated is also kicked. " +
                "404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Bans")
            .Produces<MatchBansView>()
            .WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", "ca")]))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/matches/{matchId:int}/ban", async (int matchId, TargetUserIdsBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
                MatchControlService matchControl, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    await matchControl.AddBans(match, body.UserIds ?? []);
                    return Results.Json(await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
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
            .WithDescription("Body: `{ userIds: int[] }` — adds to the existing ban list, ids need not be " +
                "online. Any newly-banned id currently seated is also kicked. 404 if the match isn't " +
                "currently live." + AdminKeyNote)
            .WithTags("Match Bans")
            .Produces<MatchBansView>()
            .WithExample(StatusCodes.Status200OK, new MatchBansView([new UserBrief(21, "Mallory", "ca"), new UserBrief(22, "Trent", "au")]))
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
                    var result = await matchControl.Unban(match, uid);
                    return result == MatchControlService.UnbanResult.NotBanned
                        ? Results.BadRequest(new ErrorResponse("userId is not banned from this match."))
                        : Results.Json(await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, users, cancellationToken));
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

    // ---- /kick ----

    private static void MapKick(RouteGroupBuilder group)
    {
        group.MapPost("/matches/{matchId:int}/kick", async (int matchId, TargetUserIdBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (body.UserId is not { } userId) return Results.BadRequest(new ErrorResponse("userId is required."));
                var target = sessionRegistry.GetById(userId);
                if (target is null) return Results.BadRequest(new ErrorResponse("userId is required and must be online."));

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    var result = await matchControl.KickAsync(null, null, match, target, cancellationToken);
                    return result == MatchControlService.KickResult.TargetNotInMatch
                        ? Results.BadRequest(new ErrorResponse("userId is not in this match."))
                        : Results.NoContent();
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
            .WithDescription("Body: `{ userId }`. 204 on success. 404 if the match isn't currently live; 400 " +
                "if `userId` is missing/not online, or isn't currently seated in this match." + AdminKeyNote)
            .WithTags("Match Kick")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not in this match."))
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---- /invite ----

    private static void MapInvite(RouteGroupBuilder group)
    {
        group.MapPost("/matches/{matchId:int}/invite", async (int matchId, InviteBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                var userIds = body.UserIds ?? [];
                if (userIds.Count == 0) return Results.BadRequest(new ErrorResponse("userIds is required."));

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    var results = new List<InviteTargetResult>();
                    var sender = sessionRegistry.GetById(match.HostId) ?? sessionRegistry.GetById(BotBootstrapService.BotId);

                    foreach (var userId in userIds)
                    {
                        var target = sessionRegistry.GetById(userId);
                        if (target is null)
                        {
                            results.Add(new InviteTargetResult(userId, false, "Not online."));
                            continue;
                        }

                        if (body.Force == true)
                        {
                            var forceResult = await matchControl.ForceInvite(match, target);
                            results.Add(forceResult switch
                            {
                                MatchControlService.ForceInviteResult.Ok => new InviteTargetResult(userId, true, null),
                                MatchControlService.ForceInviteResult.TargetBanned =>
                                    new InviteTargetResult(userId, false, "Banned from this match."),
                                MatchControlService.ForceInviteResult.TargetInAnotherMatch =>
                                    new InviteTargetResult(userId, false, "Already in another match."),
                                _ => new InviteTargetResult(userId, false, "No free slot.")
                            });
                            continue;
                        }

                        if (sender is null)
                        {
                            results.Add(new InviteTargetResult(userId, false, "No session available to send the invite from."));
                            continue;
                        }

                        var inviteResult = matchControl.Invite(sender, match, target);
                        results.Add(inviteResult == MatchControlService.InviteResult.TargetAlreadyInRoom
                            ? new InviteTargetResult(userId, false, "Already in the room.")
                            : new InviteTargetResult(userId, true, null));
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
            .WithDescription("Body: `{ userIds: int[], force? }`. Without `force`, sends a standing invite " +
                "(same as `!mp invite`) — the target still needs to join themselves, subject to the room's " +
                "password/private/lock gating. With `force: true`, bypasses password/private/lock and seats " +
                "the target directly — a banned target is still rejected regardless of `force`. Partial-" +
                "failure-safe: returns one `{ userId, ok, error }` result per target, 200 even if some " +
                "targets failed. 404 if the match isn't currently live; 400 if `userIds` is empty." + AdminKeyNote)
            .WithTags("Match Invites")
            .Produces<IReadOnlyList<InviteTargetResult>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, new List<InviteTargetResult>
            {
                new(9, true, null),
                new(21, false, "Banned from this match.")
            })
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userIds is required."))
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---- /slots ----

    private static void MapSlots(RouteGroupBuilder group)
    {
        group.MapGet("/matches/{matchId:int}/slots", async (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMatchLiveEvents events,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleSlots(context, matchId, events,
                        () => match.SlotsSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(await MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry, users, cancellationToken));
            })
            .WithGroupName("basilapi")
            .WithName("getMatchSlots")
            .WithSummary("Get Match Slots")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns `{ slots: " +
                "{ \"0\": { user, team, locked }, ..., \"15\": {...} } }` — every slot 0-15 always present " +
                "as a dict key, `user` a `{ id, name, country }` embed or null when empty; `Accept: " +
                "text/event-stream` opens a full-then-delta SSE stream (event name `slots`) instead. 404 if " +
                "the match isn't currently live. Public, no authentication.")
            .WithTags("Match Slots")
            .Produces<MatchSlotsView>()
            .WithExample(StatusCodes.Status200OK, SampleSlots())
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/matches/{matchId:int}/slots", (int matchId, SlotsBody body, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
                HandleSlotsWrite(matchId, body, isFullReplace: true, matchRegistry, sessionRegistry, users, matchControl,
                    cancellationToken))
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("replaceMatchSlots")
            .WithSummary("Replace Match Slots")
            .WithDescription("Body: `{ slots: { \"<index>\": { userId?, team?, locked? } } }` — every " +
                "currently-seated player's id must appear exactly once across the payload (reassignment/" +
                "team/lock only, nobody may be silently added or dropped). A `team` value other than the " +
                "literal `\"Red\"`/`\"Blue\"` (including omitted) leaves that slot's existing team unchanged. " +
                "409 (`PlayerCountMismatch`) if the payload's player set doesn't match the match's current " +
                "occupants exactly, or (`UnknownUserId`) if any `userId` isn't currently seated somewhere in " +
                "this match; 400 (`SlotOccupiedAndLocked`) if an entry sets both `userId` and `locked: true`. " +
                "404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Slots")
            .Produces<MatchSlotsView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status200OK, SampleSlots())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("An entry cannot set both userId and locked: true."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("The payload's player set doesn't match this match's current occupants."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/matches/{matchId:int}/slots", (int matchId, SlotsBody body, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IUserRepository users, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
                HandleSlotsWrite(matchId, body, isFullReplace: false, matchRegistry, sessionRegistry, users, matchControl,
                    cancellationToken))
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("updateMatchSlots")
            .WithSummary("Update Match Slots")
            .WithDescription("Same body/rules as `PUT`, but only validates/touches the slots actually given — " +
                "does not require every current occupant to be listed." + AdminKeyNote)
            .WithTags("Match Slots")
            .Produces<MatchSlotsView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status200OK, SampleSlots())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("An entry cannot set both userId and locked: true."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("A referenced userId is not currently seated in this match."))
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static MatchSlotsView SampleSlots()
    {
        var views = new Dictionary<int, SlotView>();
        for (var i = 0; i < 16; i++) views[i] = new SlotView(null, null, false);
        views[0] = new SlotView(new UserBrief(7, "Alice", "us"), "Red", false);
        views[1] = new SlotView(new UserBrief(9, "Carol", "ca"), "Blue", false);
        views[15] = new SlotView(null, null, true);
        return new MatchSlotsView(views);
    }

    private static async Task<IResult> HandleSlotsWrite(int matchId, SlotsBody body, bool isFullReplace,
        IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null) return Results.NotFound();

        var entries = body.Slots ?? new Dictionary<int, MatchControlService.SlotPatchEntry>();
        foreach (var index in entries.Keys)
            if (index is < 0 or > 15)
                return Results.BadRequest(new ErrorResponse($"Slot index {index} is out of range (0-15)."));

        await match.Lock.WaitAsync(cancellationToken);
        try
        {
            var result = await matchControl.SetSlotsAsync(match, entries, isFullReplace, cancellationToken);
            return result switch
            {
                MatchControlService.SetSlotsResult.PlayerCountMismatch =>
                    Results.Conflict(new ErrorResponse("The payload's player set doesn't match this match's current occupants.")),
                MatchControlService.SetSlotsResult.UnknownUserId =>
                    Results.Conflict(new ErrorResponse("A referenced userId is not currently seated in this match.")),
                MatchControlService.SetSlotsResult.SlotOccupiedAndLocked =>
                    Results.BadRequest(new ErrorResponse("An entry cannot set both userId and locked: true.")),
                _ => Results.Json(await MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry, users, cancellationToken))
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
        group.MapGet("/matches/{matchId:int}/timer", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IMatchLiveEvents events, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleTimer(context, matchId, events,
                        () => match.TimerSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
            })
            .WithGroupName("basilapi")
            .WithName("getMatchTimer")
            .WithSummary("Get Match Timer")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns `{ running, " +
                "secondsRemaining, autoStart }`; `Accept: text/event-stream` opens a full-then-delta SSE " +
                "stream (event name `timer`) instead — a delta fires at each of the same announcement " +
                "checkpoints `!mp timer`/`!mp start` chat announcements use, plus once more when the " +
                "countdown finishes or is aborted. 404 if the match isn't currently live. Public, no " +
                "authentication.")
            .WithTags("Match Timer")
            .Produces<MatchTimerView>()
            .WithExample(StatusCodes.Status200OK, new MatchTimerView(true, 25, true))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/matches/{matchId:int}/timer", async (int matchId, TimerBody body,
                IMatchRegistry matchRegistry, MatchControlService matchControl, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    if (body.Start == true)
                    {
                        var result = await matchControl.StartAsync(match, body.Seconds, cancellationToken);
                        return result switch
                        {
                            MatchControlService.StartResult.AlreadyInProgress =>
                                Results.Conflict(new ErrorResponse("Match is already in progress.")),
                            MatchControlService.StartResult.BeatmapMissing =>
                                Results.Conflict(new ErrorResponse("Match cannot start because the beatmap does not exist on the server.")),
                            _ => Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match))
                        };
                    }

                    matchControl.Timer(match, body.Seconds is > 0 ? body.Seconds.Value : 30);
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
            .WithDescription("Body: `{ seconds?, start? }`. `start: true` forwards to the same logic as " +
                "`!mp start [seconds]` — no/non-positive `seconds` starts immediately, a positive value " +
                "queues a countdown that starts the match when it finishes. `start` false/omitted forwards " +
                "to `!mp timer` — a countdown that never auto-starts (`seconds` defaults to 30). 409 if the " +
                "match is already in progress or has no beatmap set. 404 if the match isn't currently live." +
                AdminKeyNote)
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
                MatchControlService matchControl, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    var result = await matchControl.AbortAsync(match, cancellationToken);
                    return result == MatchControlService.AbortResult.NotInProgress
                        ? Results.Conflict(new ErrorResponse("Match is not in progress."))
                        : Results.NoContent();
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
            .WithDescription("204 on success. 409 if the match is not in progress. 404 if the match isn't " +
                "currently live." + AdminKeyNote)
            .WithTags("Match Abort")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
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
                    await matchControl.CloseAsync(null, null, match, cancellationToken);
                    return Results.NoContent();
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
            .WithDescription("204 on success. 404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Close")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static (IReadOnlyCollection<PlayerSession> Targets, IResult? Error) ResolveOnlineTargets(
        IReadOnlyList<int>? userIds, IPlayerSessionRegistry sessionRegistry)
    {
        var targets = new List<PlayerSession>();
        foreach (var userId in userIds ?? [])
        {
            var target = sessionRegistry.GetById(userId);
            if (target is null)
                return (targets, Results.BadRequest(new ErrorResponse($"userId {userId} is required and must be online.")));

            targets.Add(target);
        }

        return (targets, null);
    }
}
