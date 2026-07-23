using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Web.Auth;
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
        group.MapGet("/matches/{matchId:int}/hosts", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IMatchLiveEvents events, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleHost(context, matchId, events,
                        () => match.HostSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry));
            })
            .WithGroupName("basilapi")
            .WithSummary("Get the match's current host — one-shot JSON snapshot, or a live SSE stream.")
            .WithDescription("Content-negotiated on the `Accept` header, same convention as `GET /matches/" +
                "{matchId}`: a plain `GET` returns `{ hostId, hostName }` (both null when the room has no " +
                "host); `Accept: text/event-stream` opens a full-then-delta SSE stream (event name `hosts`) " +
                "instead. 404 if the match isn't currently live. Public, no authentication.")
            .WithTags("Match Actions", "Live Channels (SSE)");

        group.MapPut("/matches/{matchId:int}/hosts", async (int matchId, TargetUserIdBody body,
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
                    matchControl.SetHost(match, target);
                    return Results.Json(MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Set the match host.")
            .WithDescription("Body: `{ userId }`. 404 if the match isn't currently live; 400 if `userId` is " +
                "missing or not online." + AdminKeyNote)
            .WithTags("Match Actions");

        group.MapDelete("/matches/{matchId:int}/hosts", async (int matchId, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    matchControl.ClearHost(match);
                    return Results.Json(MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Clear the match host.")
            .WithDescription("Sets the host back to id 0. 404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");
    }

    // ---- /refs ----

    private static void MapRefs(RouteGroupBuilder group)
    {
        group.MapGet("/matches/{matchId:int}/refs", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IMatchLiveEvents events, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleRefs(context, matchId, events,
                        () => match.RefsSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry));
            })
            .WithGroupName("basilapi")
            .WithSummary("List the match's referees — one-shot JSON snapshot, or a live SSE stream.")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns `{ referees: " +
                "[{ userId, userName }] }`; `Accept: text/event-stream` opens a full-then-delta SSE stream " +
                "(event name `refs`) instead. 404 if the match isn't currently live. Public, no authentication.")
            .WithTags("Match Actions", "Live Channels (SSE)");

        group.MapPut("/matches/{matchId:int}/refs", async (int matchId, TargetUserIdsBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
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
                        : Results.Json(MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Replace the match's referee list.")
            .WithDescription("Body: `{ userIds: int[] }` — full replace, every id must be online. 409 if the " +
                "result would leave the match with zero referees. 404 if the match isn't currently live; 400 " +
                "if any `userId` isn't online." + AdminKeyNote)
            .WithTags("Match Actions");

        group.MapPatch("/matches/{matchId:int}/refs", async (int matchId, TargetUserIdsBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                var (targets, error) = ResolveOnlineTargets(body.UserIds, sessionRegistry);
                if (error is not null) return error;

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    await matchControl.AddRefereesAsync(match, targets, cancellationToken);
                    return Results.Json(MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Add a batch of referees.")
            .WithDescription("Body: `{ userIds: int[] }` — adds to the existing referee list, every id must " +
                "be online. Never rejected for leaving the list empty (it only ever adds). 404 if the match " +
                "isn't currently live; 400 if any `userId` isn't online." + AdminKeyNote)
            .WithTags("Match Actions");

        group.MapDelete("/matches/{matchId:int}/refs", async (int matchId, int? userId, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
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
                        _ => Results.Json(MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry))
                    };
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Remove one referee.")
            .WithDescription("Query param `userId` (required, must be online). 409 if this would leave the " +
                "match with zero referees; 400 if `userId` isn't a referee or isn't online. 404 if the match " +
                "isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");
    }

    // ---- /ban ----

    private static void MapBans(RouteGroupBuilder group)
    {
        group.MapGet("/matches/{matchId:int}/ban", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IMatchLiveEvents events, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleBans(context, matchId, events,
                        () => match.BansSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry));
            })
            .WithGroupName("basilapi")
            .WithSummary("List players banned from this match — one-shot JSON snapshot, or a live SSE stream.")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns " +
                "`{ bannedUsers: [{ userId, userName }] }` (`userName` null for a currently-offline banned " +
                "id); `Accept: text/event-stream` opens a full-then-delta SSE stream (event name `ban`) " +
                "instead. 404 if the match isn't currently live. Public, no authentication.")
            .WithTags("Match Actions", "Live Channels (SSE)");

        group.MapPut("/matches/{matchId:int}/ban", async (int matchId, TargetUserIdsBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    matchControl.SetBans(match, body.UserIds ?? []);
                    return Results.Json(MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Replace the match's ban list.")
            .WithDescription("Body: `{ userIds: int[] }` — full replace, ids need not be online. No empty " +
                "guard (banning down to zero is fine). Any newly-banned id currently seated is also kicked. " +
                "404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");

        group.MapPatch("/matches/{matchId:int}/ban", async (int matchId, TargetUserIdsBody body,
                IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    matchControl.AddBans(match, body.UserIds ?? []);
                    return Results.Json(MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Add a batch of bans.")
            .WithDescription("Body: `{ userIds: int[] }` — adds to the existing ban list, ids need not be " +
                "online. Any newly-banned id currently seated is also kicked. 404 if the match isn't " +
                "currently live." + AdminKeyNote)
            .WithTags("Match Actions");

        group.MapDelete("/matches/{matchId:int}/ban", async (int matchId, int? userId, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (userId is not { } uid) return Results.BadRequest(new ErrorResponse("userId is required."));

                await match.Lock.WaitAsync(cancellationToken);
                try
                {
                    var result = matchControl.Unban(match, uid);
                    return result == MatchControlService.UnbanResult.NotBanned
                        ? Results.BadRequest(new ErrorResponse("userId is not banned from this match."))
                        : Results.Json(MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry));
                }
                finally
                {
                    match.Lock.Release();
                }
            })
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Unban one player.")
            .WithDescription("Query param `userId` (required). 400 if `userId` isn't banned from this match. " +
                "404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");
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
            .WithSummary("Kick a player from the match.")
            .WithDescription("Body: `{ userId }`. 204 on success. 404 if the match isn't currently live; 400 " +
                "if `userId` is missing/not online, or isn't currently seated in this match." + AdminKeyNote)
            .WithTags("Match Actions");
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
                            var forceResult = matchControl.ForceInvite(match, target);
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
            .WithSummary("Invite one or more players.")
            .WithDescription("Body: `{ userIds: int[], force? }`. Without `force`, sends a standing invite " +
                "(same as `!mp invite`) — the target still needs to join themselves, subject to the room's " +
                "password/private/lock gating. With `force: true`, bypasses password/private/lock and seats " +
                "the target directly — a banned target is still rejected regardless of `force`. Partial-" +
                "failure-safe: returns one `{ userId, ok, error }` result per target, 200 even if some " +
                "targets failed. 404 if the match isn't currently live; 400 if `userIds` is empty." + AdminKeyNote)
            .WithTags("Match Actions");
    }

    // ---- /slots ----

    private static void MapSlots(RouteGroupBuilder group)
    {
        group.MapGet("/matches/{matchId:int}/slots", (int matchId, HttpContext context, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, IMatchLiveEvents events, CancellationToken cancellationToken) =>
            {
                var match = matchRegistry.GetByDbId(matchId);
                if (match is null) return Results.NotFound();

                if (WantsSse(context))
                    return LiveSseRoutes.HandleSlots(context, matchId, events,
                        () => match.SlotsSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                            : null,
                        cancellationToken);

                return Results.Json(MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry));
            })
            .WithGroupName("basilapi")
            .WithSummary("Get every slot's current occupant/team/lock state — one-shot JSON snapshot, or a live SSE stream.")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns `{ slots: " +
                "{ \"0\": { userId, userName, team, locked }, ..., \"15\": {...} } }` — every slot 0-15 " +
                "always present as a dict key; `Accept: text/event-stream` opens a full-then-delta SSE " +
                "stream (event name `slots`) instead. 404 if the match isn't currently live. Public, no " +
                "authentication.")
            .WithTags("Match Actions", "Live Channels (SSE)");

        group.MapPut("/matches/{matchId:int}/slots", (int matchId, SlotsBody body, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
                HandleSlotsWrite(matchId, body, isFullReplace: true, matchRegistry, sessionRegistry, matchControl,
                    cancellationToken))
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Replace the full slot arrangement.")
            .WithDescription("Body: `{ slots: { \"<index>\": { userId?, team?, locked? } } }` — every " +
                "currently-seated player's id must appear exactly once across the payload (reassignment/" +
                "team/lock only, nobody may be silently added or dropped). A `team` value other than the " +
                "literal `\"Red\"`/`\"Blue\"` (including omitted) leaves that slot's existing team unchanged. " +
                "409 (`PlayerCountMismatch`) if the payload's player set doesn't match the match's current " +
                "occupants exactly, or (`UnknownUserId`) if any `userId` isn't currently seated somewhere in " +
                "this match; 400 (`SlotOccupiedAndLocked`) if an entry sets both `userId` and `locked: true`. " +
                "404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");

        group.MapPatch("/matches/{matchId:int}/slots", (int matchId, SlotsBody body, IMatchRegistry matchRegistry,
                IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
                CancellationToken cancellationToken) =>
                HandleSlotsWrite(matchId, body, isFullReplace: false, matchRegistry, sessionRegistry, matchControl,
                    cancellationToken))
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Patch a subset of slots.")
            .WithDescription("Same body/rules as `PUT`, but only validates/touches the slots actually given — " +
                "does not require every current occupant to be listed." + AdminKeyNote)
            .WithTags("Match Actions");
    }

    private static async Task<IResult> HandleSlotsWrite(int matchId, SlotsBody body, bool isFullReplace,
        IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
        CancellationToken cancellationToken)
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
                _ => Results.Json(MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry))
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
            .WithSummary("Get the match's countdown timer state — one-shot JSON snapshot, or a live SSE stream.")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` returns `{ running, " +
                "secondsRemaining, autoStart }`; `Accept: text/event-stream` opens a full-then-delta SSE " +
                "stream (event name `timer`) instead — a delta fires at each of the same announcement " +
                "checkpoints `!mp timer`/`!mp start` chat announcements use, plus once more when the " +
                "countdown finishes or is aborted. 404 if the match isn't currently live. Public, no " +
                "authentication.")
            .WithTags("Match Actions", "Live Channels (SSE)");

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
            .WithSummary("Start a countdown, or start the match immediately.")
            .WithDescription("Body: `{ seconds?, start? }`. `start: true` forwards to the same logic as " +
                "`!mp start [seconds]` — no/non-positive `seconds` starts immediately, a positive value " +
                "queues a countdown that starts the match when it finishes. `start` false/omitted forwards " +
                "to `!mp timer` — a countdown that never auto-starts (`seconds` defaults to 30). 409 if the " +
                "match is already in progress or has no beatmap set. 404 if the match isn't currently live." +
                AdminKeyNote)
            .WithTags("Match Actions");

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
            .WithSummary("Abort a running countdown.")
            .WithDescription("409 if no countdown is running. 404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");
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
            .WithSummary("Abort the match in progress.")
            .WithDescription("204 on success. 409 if the match is not in progress. 404 if the match isn't " +
                "currently live." + AdminKeyNote)
            .WithTags("Match Actions");
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
            .WithSummary("Close the match immediately.")
            .WithDescription("204 on success. 404 if the match isn't currently live." + AdminKeyNote)
            .WithTags("Match Actions");
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
