using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Web.Routing;

/// <summary>
///     `/match` — resource-oriented routes replacing the old admin-key-only `/matches` listing plus
///     the bare TRT report/SSE routes. Reads (list/report/live channels) are public, with a soft
///     admin-only elevation for private-match visibility; every write (create/settings/actions) is
///     admin-key gated. Settings/action mutation logic lives in <see cref="MatchControlService" />,
///     shared with `!mp`'s chat commands — this file only resolves HTTP-specific input (numeric
///     `userId` targets, JSON bodies) and maps results to HTTP responses.
/// </summary>
internal static class MatchRoutes
{
    public static void MapMatchRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/match", HandleList)
            .WithGroupName("basilapi")
            .WithSummary("List matches, paged.")
            .WithDescription("Query params: `status` (`online` (default) | `offline` | `all`), `page` " +
                "(default 1), `pageSize` (default 50). `online` is currently-live matches (tracked in " +
                "memory); `offline` is closed matches (persisted with `endedAt` set); `all` is both, " +
                "newest first. A private live match is excluded from the list entirely unless the caller " +
                "carries a valid `X-Admin-Key`. Response: `{ page, pageSize, count, hasMore, items }` — " +
                "no `total`/`totalPages`; `hasMore` just reports whether another page exists. Public.")
            .WithTags("Match Reports");

        group.MapPost("/match", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Create a match (`!mp make` equivalent).")
            .WithDescription("Body: any subset of `{ name, password, isPrivate, isLocked, size, mapId, " +
                "mods, freemod, teamType, winCondition }` — same shape `PATCH /match/{id}/settings` " +
                "accepts, all optional (each defaults to `!mp make`'s own defaults when omitted). No chat " +
                "\"sender\" exists over HTTP, so the new match starts with host id 0 and no referees — " +
                "assign both via the `host`/`addref` actions afterward. Returns the full settings " +
                "representation (not a bare id)." + AdminKeyNote)
            .WithTags("Match Reports");

        group.MapGet("/match/{id:int}/settings", HandleSettingsStream)
            .WithGroupName("basilapi")
            .WithSummary("Live match settings (SSE only).")
            .WithDescription("Server-Sent Events stream (event name `settings`) scoped to just the " +
                "room-configuration fields — first event is the full current settings, every event after " +
                "is an RFC 7396 JSON Merge Patch against the previous one. Never includes the raw " +
                "password, only `hasPassword`. Public, no authentication.")
            .WithTags("Live Channels (SSE)");

        group.MapMethods("/match/{id:int}/settings", ["PUT", "PATCH"], HandleSettingsUpdate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Update a match's settings (partial).")
            .WithDescription("Body: any subset of `{ name, password, isPrivate, isLocked, size, mapId, " +
                "mods, freemod, teamType, winCondition }` — only present fields are touched, matching " +
                "this host's usual partial-update convention. `freemod: true` enables FreeMod (ignoring " +
                "`mods` for that call); `mods` alone (no `freemod`) sets the room's fixed mod set. Maps to " +
                "`!mp name/password/private/lock+unlock/size/map/mods/set`. 404 if the match isn't " +
                "currently live; 400 if `mapId` doesn't resolve to a known beatmap. Returns the updated " +
                "settings representation." + AdminKeyNote)
            .WithTags("Match Reports");

        group.MapGet("/match/{id:int}/live", HandleLiveStream)
            .WithGroupName("basilapi")
            .WithSummary("Live room-wide \"currently playing\" status (SSE only).")
            .WithDescription("Server-Sent Events stream (event name `live`) of `{ inProgress, " +
                "currentRoundId, mapId, mode }` — no per-player data, see `GET /match/{id}/live/{slotIndex}` " +
                "for that. First event is the full current status, every event after is an RFC 7396 JSON " +
                "Merge Patch. Idle (no events) outside of an active round — that's expected. Public, no " +
                "authentication.")
            .WithTags("Live Channels (SSE)");

        group.MapGet("/match/{id:int}/live/{slotIndex:int}", HandleLiveSlotStream)
            .WithGroupName("basilapi")
            .WithSummary("Merged live slot/score/spectator-input stream for one slot (SSE only).")
            .WithDescription("`{slotIndex}` is 1-16 (matching `!mp move`'s convention). One SSE stream " +
                "tagging three feeds by event name: `slot` (that slot's membership/status/team/mods, " +
                "full-then-delta), `score` (the current occupant's live score frames during a round, " +
                "forwarded as-is), and `input` (the current occupant's raw spectator-input frames, " +
                "forwarded as-is). Follows whoever currently occupies the slot — if the occupant changes, " +
                "the next `slot` event reflects that, and `score`/`input` start matching the new occupant " +
                "automatically. 404 if the match isn't currently live or `slotIndex` is out of range. " +
                "Public, no authentication.")
            .WithTags("Live Channels (SSE)");

        group.MapPost("/match/{id:int}/{action}", HandleAction)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Perform a one-shot room action.")
            .WithDescription("`{action}` is one of `host`, `clearhost`, `move`, `team`, `invite`, " +
                "`addref`, `removeref`, `kick`, `ban`, `unban`, `start`, `timer`, `aborttimer`, `abort`, " +
                "`close` — every `!mp` subcommand not covered by `settings`. Body: `{ userId? }` for " +
                "player-targeted actions (host/clearhost takes no target for clearhost; move also takes " +
                "`slotIndex` 1-16; team also takes `team` 0=Neutral/1=Blue/2=Red; start/timer take " +
                "`seconds`). Player targets are always numeric ids — no name resolution exists over " +
                "HTTP. 404 if the match isn't live or `{action}` is unrecognized; 400 if a required " +
                "target/field is missing or the target isn't currently in the match. Returns the updated " +
                "settings representation." + AdminKeyNote)
            .WithTags("Match Reports");
    }

    private const string AdminKeyNote = " Requires a valid `X-Admin-Key` request header matching the " +
        "server's configured `Server:AdminKey`.";

    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    private sealed record MatchListItem(int Id, string Name, DateTime CreatedAt, DateTime? EndedAt, bool IsOpen,
        bool IsPrivate);

    private static async Task<IResult> HandleList(
        [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize,
        HttpContext context, IMatchRegistry matchRegistry, IMatchPersistenceRepository matchPersistence,
        CancellationToken cancellationToken)
    {
        var (p, ps) = Pagination.Normalize(page, pageSize);
        var mode = (status ?? "online").ToLowerInvariant();
        if (mode is not ("online" or "offline" or "all")) mode = "online";
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);

        var rows = await matchPersistence.FetchAllMatchesAsync(cancellationToken);
        var items = rows
            .Select(row => (Row: row, Live: matchRegistry.GetByDbId(row.Id)))
            .Where(t =>
            {
                var isOpen = t.Live is not null;
                if (mode == "online" && !isOpen) return false;
                if (mode == "offline" && isOpen) return false;
                return !isOpen || !t.Live!.IsPrivate || isAdmin;
            })
            .OrderByDescending(t => t.Row.Id)
            .Select(t => new MatchListItem(t.Row.Id, t.Row.Name, t.Row.CreatedAt, t.Row.EndedAt,
                t.Live is not null, t.Live?.IsPrivate ?? false))
            .ToList();

        var overqueried = items.Skip((p - 1) * ps).Take(ps + 1).ToList();
        return Results.Json(Pagination.Trim(overqueried, p, ps));
    }

    private static async Task<IResult> HandleCreate(MatchSettingsBody body, MatchMembershipService matchMembership,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        var name = body.Name ?? "New match";
        if (name.Length > MatchControlService.MaxMatchNameLength) name = name[..MatchControlService.MaxMatchNameLength];

        var data = new ReadMatchResult(
            0, false, 0, 0, name, body.Password ?? "",
            "", 0, "",
            [], [], [], 0, 0,
            0, 0, false, [], 0);

        var match = await matchMembership.CreateEmptyAsync(data, cancellationToken);
        if (match is null) return Results.Problem("Couldn't create the match — server is full.", statusCode: 503);

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

        return Results.Json(MatchLiveSnapshotBuilder.BuildSettings(match));
    }

    private static IResult HandleSettingsStream(int id, HttpContext context, IMatchRegistry matchRegistry,
        IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(id);
        return LiveSseRoutes.HandleSettings(context, id, events,
            () => match?.SettingsSnapshot.Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                : null,
            cancellationToken);
    }

    private static IResult HandleLiveStream(int id, HttpContext context, IMatchRegistry matchRegistry,
        IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(id);
        return LiveSseRoutes.HandleLive(context, id, events,
            () => match?.LiveSnapshot.Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                : null,
            cancellationToken);
    }

    private static IResult HandleLiveSlotStream(int id, int slotIndex, HttpContext context,
        IMatchRegistry matchRegistry, IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents,
        IPlayerSessionRegistry sessionRegistry, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(id);
        if (match is null || slotIndex is < 1 or > 16) return Results.NotFound();

        var index = slotIndex - 1;
        return LiveSseRoutes.HandleLiveSlot(context, match, index, matchEvents, inputEvents, sessionRegistry,
            () => match.SlotSnapshots[index].Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                : null,
            cancellationToken);
    }

    private static async Task<IResult> HandleSettingsUpdate(int id, MatchSettingsBody body,
        IMatchRegistry matchRegistry, MatchControlService matchControl, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(id);
        if (match is null) return Results.NotFound();

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

        return Results.Json(MatchLiveSnapshotBuilder.BuildSettings(match));
    }

    /// <summary>Caller must hold <paramref name="match" />'s Lock. Returns a non-null error IResult on failure.</summary>
    private static async Task<IResult?> ApplySettingsAsync(MatchSession match, MatchSettingsBody body,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        if (body.Name is not null) matchControl.SetName(match, body.Name);
        if (body.Password is not null) matchControl.SetPassword(match, body.Password);
        if (body.IsPrivate is not null) matchControl.SetPrivate(match, body.IsPrivate.Value);
        if (body.IsLocked is not null) matchControl.SetLocked(match, body.IsLocked.Value);
        if (body.Size is not null) matchControl.SetSize(match, body.Size.Value);

        if (body.MapId is not null)
        {
            var (result, _) = await matchControl.SetMapAsync(match, body.MapId.Value, cancellationToken);
            if (result == MatchControlService.SetMapResult.BeatmapNotFound)
                return Results.BadRequest(new { error = $"No beatmap with id {body.MapId.Value} found locally." });
        }

        if (body.Freemod == true)
            matchControl.SetMods(match, Mods.NoMod, true);
        else if (body.Mods is not null)
            matchControl.SetMods(match, (Mods)body.Mods.Value, false);

        if (body.TeamType is not null || body.WinCondition is not null)
            matchControl.SetTeamTypeWinConditionAndSize(match,
                body.TeamType is not null ? (MatchTeamType)body.TeamType.Value : match.TeamType,
                body.WinCondition is not null ? (MatchWinCondition)body.WinCondition.Value : null, null);

        return null;
    }

    private static async Task<IResult> HandleAction(int id, string action, MatchActionBody body,
        IMatchRegistry matchRegistry, IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl,
        CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(id);
        if (match is null) return Results.NotFound();

        await match.Lock.WaitAsync(cancellationToken);
        try
        {
            var result = await RunActionAsync(match, action, body, sessionRegistry, matchControl, cancellationToken);
            if (result is not null) return result;
        }
        finally
        {
            match.Lock.Release();
        }

        return Results.Json(MatchLiveSnapshotBuilder.BuildSettings(match));
    }

    private static PlayerSession? ResolveTarget(MatchActionBody body, IPlayerSessionRegistry sessionRegistry)
    {
        return body.UserId is { } id ? sessionRegistry.GetById(id) : null;
    }

    /// <summary>Caller must hold <paramref name="match" />'s Lock. Returns a non-null error IResult on failure/unknown action.</summary>
    private static async Task<IResult?> RunActionAsync(MatchSession match, string action, MatchActionBody body,
        IPlayerSessionRegistry sessionRegistry, MatchControlService matchControl, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "host":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null) return Results.BadRequest(new { error = "userId is required and must be online." });
                matchControl.SetHost(match, target);
                return null;
            }
            case "clearhost":
                matchControl.ClearHost(match);
                return null;
            case "move":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null || body.SlotIndex is null)
                    return Results.BadRequest(new { error = "userId and slotIndex (1-16) are required." });

                var slotIndex = Math.Clamp(body.SlotIndex.Value, 1, 16) - 1;
                var result = matchControl.MoveSlot(match, target, slotIndex);
                return result switch
                {
                    MatchControlService.MoveResult.DestinationNotOpen => Results.Conflict(new { error = "Destination slot is not open." }),
                    MatchControlService.MoveResult.TargetNotInMatch => Results.BadRequest(new { error = "userId is not in this match." }),
                    _ => null
                };
            }
            case "team":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null || body.Team is null)
                    return Results.BadRequest(new { error = "userId and team (0=Neutral, 1=Blue, 2=Red) are required." });

                var result = matchControl.SetTeam(match, target, (MatchTeam)body.Team.Value);
                return result == MatchControlService.TeamResult.TargetNotInMatch
                    ? Results.BadRequest(new { error = "userId is not in this match." })
                    : null;
            }
            case "invite":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null) return Results.BadRequest(new { error = "userId is required and must be online." });

                var sender = sessionRegistry.GetById(match.HostId) ?? sessionRegistry.GetById(BotBootstrapService.BotId);
                if (sender is null) return Results.Problem("No session available to send the invite from.", statusCode: 500);

                var result = matchControl.Invite(sender, match, target);
                return result == MatchControlService.InviteResult.TargetAlreadyInRoom
                    ? Results.Conflict(new { error = "userId is already in the room." })
                    : null;
            }
            case "addref":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null) return Results.BadRequest(new { error = "userId is required and must be online." });
                await matchControl.AddRefereeAsync(null, null, match, target, cancellationToken);
                return null;
            }
            case "removeref":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null) return Results.BadRequest(new { error = "userId is required and must be online." });
                await matchControl.RemoveRefereeAsync(null, null, match, target, cancellationToken);
                return null;
            }
            case "kick":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null) return Results.BadRequest(new { error = "userId is required and must be online." });
                var result = await matchControl.KickAsync(null, null, match, target, cancellationToken);
                return result == MatchControlService.KickResult.TargetNotInMatch
                    ? Results.BadRequest(new { error = "userId is not in this match." })
                    : null;
            }
            case "ban":
            {
                var target = ResolveTarget(body, sessionRegistry);
                if (target is null) return Results.BadRequest(new { error = "userId is required and must be online." });
                var result = await matchControl.BanAsync(null, null, match, target, cancellationToken);
                return result == MatchControlService.KickResult.TargetNotInMatch
                    ? Results.BadRequest(new { error = "userId is not in this match." })
                    : null;
            }
            case "unban":
            {
                if (body.UserId is null) return Results.BadRequest(new { error = "userId is required." });
                var result = matchControl.Unban(match, body.UserId.Value);
                return result == MatchControlService.UnbanResult.NotBanned
                    ? Results.BadRequest(new { error = "userId is not banned from this match." })
                    : null;
            }
            case "start":
            {
                var result = await matchControl.StartAsync(match, body.Seconds, cancellationToken);
                return result switch
                {
                    MatchControlService.StartResult.AlreadyInProgress => Results.Conflict(new { error = "Match is already in progress." }),
                    MatchControlService.StartResult.BeatmapMissing => Results.Conflict(new { error = "Match cannot start because the beatmap does not exist on the server." }),
                    _ => null
                };
            }
            case "timer":
                matchControl.Timer(match, body.Seconds is > 0 ? body.Seconds.Value : 30);
                return null;
            case "aborttimer":
            {
                var result = matchControl.AbortTimer(match);
                return result == MatchControlService.AbortTimerResult.NoTimerRunning
                    ? Results.Conflict(new { error = "No countdown is running." })
                    : null;
            }
            case "abort":
            {
                var result = await matchControl.AbortAsync(match, cancellationToken);
                return result == MatchControlService.AbortResult.NotInProgress
                    ? Results.Conflict(new { error = "Match is not in progress." })
                    : null;
            }
            case "close":
                await matchControl.CloseAsync(null, null, match, cancellationToken);
                return null;
            default:
                return Results.NotFound(new { error = $"Unknown action '{action}'." });
        }
    }
}

/// <summary>Body for `POST /match` and `PUT`/`PATCH /match/{id}/settings` — every field optional, only present ones are applied.</summary>
public sealed record MatchSettingsBody(
    string? Name, string? Password, bool? IsPrivate, bool? IsLocked, int? Size,
    int? MapId, int? Mods, bool? Freemod, int? TeamType, int? WinCondition);

/// <summary>Body for `POST /match/{id}/{action}` — a superset covering every action's own fields; unused ones are ignored.</summary>
public sealed record MatchActionBody(int? UserId, int? SlotIndex, int? Team, int? Seconds);
