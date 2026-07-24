using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Web.Routing;

/// <summary>
///     `/matches` — resource-oriented routes replacing the old admin-key-only `/matches` listing plus
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
        group.MapGet("/matches", HandleList)
            .WithGroupName("basilapi")
            .WithName("listMatches")
            .WithSummary("List Matches")
            .WithDescription("Query params: `status` (`online` (default) | `offline` | `all`), `page` " +
                "(default 1), `pageSize` (default 50). `online` is currently-live matches (tracked in " +
                "memory); `offline` is closed matches (persisted with `endedAt` set); `all` is both, " +
                "newest first. A private live match is excluded from the list entirely unless the caller " +
                "carries a valid `X-Admin-Key`. Response: `{ page, pageSize, count, hasMore, items }` — " +
                "no `total`/`totalPages`; `hasMore` just reports whether another page exists. Public.")
            .WithTags("Matches")
            .Produces<PagedResult<MatchListItem>>()
            .WithExample(StatusCodes.Status200OK, new PagedResult<MatchListItem>(1, 50, 1, false,
                [new MatchListItem(42, "Grand Finals: Alpha vs Bravo", DateTime.Parse("2026-07-20T12:00:00Z"), null, true, false)]));

        group.MapPost("/matches", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("createMatch")
            .WithSummary("Create Match")
            .WithDescription("Body: any subset of `{ name, password, isPrivate, isLocked, size, mapId, " +
                "mods, freemod, teamType, winCondition }` — same shape `PATCH /matches/{matchId}/settings` " +
                "accepts, all optional (each defaults to `!mp make`'s own defaults when omitted). No chat " +
                "\"sender\" exists over HTTP, so the new match starts with host id 0 and no referees — " +
                "assign both via the `host`/`addref` actions afterward. Returns the full settings " +
                "representation (not a bare id)." + AdminKeyNote)
            .WithTags("Matches")
            .Produces<MatchSettingsView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithExample(StatusCodes.Status200OK, SampleSettings())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."));

        group.MapGet("/matches/{matchId:int}/settings", HandleSettingsStream)
            .WithGroupName("basilapi")
            .WithName("getMatchSettings")
            .WithSummary("Get Match Settings")
            .WithDescription("Server-Sent Events stream (event name `settings`) scoped to just the " +
                "room-configuration fields — first event is the full current settings, every event after " +
                "is an RFC 7396 JSON Merge Patch against the previous one. Never includes the raw " +
                "password, only `hasPassword`. Public, no authentication.")
            .WithTags("Match Settings")
            .Produces<MatchSettingsView>()
            .WithExample(StatusCodes.Status200OK, SampleSettings());

        group.MapPut("/matches/{matchId:int}/settings", HandleSettingsUpdate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("replaceMatchSettings")
            .WithSummary("Replace Match Settings")
            .WithDescription("Body: any subset of `{ name, password, isPrivate, isLocked, size, mapId, " +
                "mods, freemod, teamType, winCondition }` — only present fields are touched, matching " +
                "this host's usual partial-update convention. `freemod: true` enables FreeMod (ignoring " +
                "`mods` for that call); `mods` alone (no `freemod`) sets the room's fixed mod set. Maps to " +
                "`!mp name/password/private/lock+unlock/size/map/mods/set`. 404 if the match isn't " +
                "currently live; 400 if `mapId` doesn't resolve to a known beatmap. Returns the updated " +
                "settings representation." + AdminKeyNote)
            .WithTags("Match Settings")
            .Produces<MatchSettingsView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, SampleSettings())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/matches/{matchId:int}/settings", HandleSettingsUpdate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("updateMatchSettings")
            .WithSummary("Update Match Settings")
            .WithDescription("Identical semantics to `PUT` on this same path (every field here is always " +
                "applied only if present) — offered under both verbs since callers reasonably expect " +
                "either for a partial update." + AdminKeyNote)
            .WithTags("Match Settings")
            .Produces<MatchSettingsView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, SampleSettings())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/matches/{matchId:int}/live", HandleLiveStream)
            .WithGroupName("basilapi")
            .WithName("getMatchLiveStatus")
            .WithSummary("Get Match Live Status")
            .WithDescription("Server-Sent Events stream (event name `live`) of `{ inProgress, " +
                "currentRoundId, mapId, mode }` — no per-player data, see " +
                "`GET /matches/{matchId}/live/{slotIndex}` for that. First event is the full current " +
                "status, every event after is an RFC 7396 JSON Merge Patch. Idle (no events) outside of " +
                "an active round — that's expected. Public, no authentication.")
            .WithTags("Match Live")
            .Produces<MatchLiveStatus>()
            .WithExample(StatusCodes.Status200OK, new MatchLiveStatus(true, 3, 654, GameMode.Standard));

        group.MapGet("/matches/{matchId:int}/live/{slotIndex:int}", HandleLiveSlotStream)
            .WithGroupName("basilapi")
            .WithName("getMatchSlotLiveStream")
            .WithSummary("Get Match Slot Live Stream")
            .WithDescription("`{slotIndex}` is 1-16 (matching `!mp move`'s convention). One SSE stream " +
                "tagging three feeds by event name: `slot` (that slot's membership/status/team/mods, " +
                "full-then-delta), `score` (the current occupant's live score frames during a round, " +
                "forwarded as-is), and `input` (the current occupant's raw spectator-input frames, " +
                "forwarded as-is). Follows whoever currently occupies the slot — if the occupant changes, " +
                "the next `slot` event reflects that, and `score`/`input` start matching the new occupant " +
                "automatically. 404 if the match isn't currently live or `slotIndex` is out of range. " +
                "Public, no authentication.")
            .WithTags("Match Live")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapMatchSubResourceRoutes();
    }

    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

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
        MatchControlService matchControl, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
        IMapRepository maps, CancellationToken cancellationToken)
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

        return Results.Json(await MatchLiveSnapshotBuilder.BuildSettings(match, sessionRegistry, users, maps, cancellationToken));
    }

    private static IResult HandleSettingsStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
        IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        return LiveSseRoutes.HandleSettings(context, matchId, events,
            () => match?.SettingsSnapshot.Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                : null,
            cancellationToken);
    }

    private static IResult HandleLiveStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
        IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        return LiveSseRoutes.HandleLive(context, matchId, events,
            () => match?.LiveSnapshot.Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                : null,
            cancellationToken);
    }

    private static IResult HandleLiveSlotStream(int matchId, int slotIndex, HttpContext context,
        IMatchRegistry matchRegistry, IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents,
        IPlayerSessionRegistry sessionRegistry, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null || slotIndex is < 1 or > 16) return Results.NotFound();

        var index = slotIndex - 1;
        return LiveSseRoutes.HandleLiveSlot(context, match, index, matchEvents, inputEvents, sessionRegistry,
            () => match.SlotSnapshots[index].Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonWebOptions)
                : null,
            cancellationToken);
    }

    private static async Task<IResult> HandleSettingsUpdate(int matchId, MatchSettingsBody body,
        IMatchRegistry matchRegistry, MatchControlService matchControl, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, IMapRepository maps, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
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

        return Results.Json(await MatchLiveSnapshotBuilder.BuildSettings(match, sessionRegistry, users, maps, cancellationToken));
    }

    /// <summary>Caller must hold <paramref name="match" />'s Lock. Returns a non-null error IResult on failure.</summary>
    private static async Task<IResult?> ApplySettingsAsync(MatchSession match, MatchSettingsBody body,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        if (body.Name is not null) await matchControl.SetName(match, body.Name);
        if (body.Password is not null) await matchControl.SetPassword(match, body.Password);
        if (body.IsPrivate is not null) await matchControl.SetPrivate(match, body.IsPrivate.Value);
        if (body.IsLocked is not null) matchControl.SetLocked(match, body.IsLocked.Value);
        if (body.Size is not null) await matchControl.SetSize(match, body.Size.Value);

        if (body.MapId is not null)
        {
            var (result, _) = await matchControl.SetMapAsync(match, body.MapId.Value, cancellationToken);
            if (result == MatchControlService.SetMapResult.BeatmapNotFound)
                return Results.BadRequest(new ErrorResponse($"No beatmap with id {body.MapId.Value} found locally."));
        }

        if (body.Freemod == true)
            await matchControl.SetMods(match, Mods.NoMod, true);
        else if (body.Mods is not null)
            await matchControl.SetMods(match, (Mods)body.Mods.Value, false);

        if (body.TeamType is not null || body.WinCondition is not null)
            await matchControl.SetTeamTypeWinConditionAndSize(match,
                body.TeamType is not null ? (MatchTeamType)body.TeamType.Value : match.TeamType,
                body.WinCondition is not null ? (MatchWinCondition)body.WinCondition.Value : null, null);

        return null;
    }

    private static MatchSettingsView SampleSettings()
    {
        return new MatchSettingsView(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
            "Camellia - Exit This Earth's Atmosphere [Extreme]", 0, false,
            MatchTeamType.TeamVs, MatchWinCondition.ScoreV2,
            new UserBrief(7, "Alice", "us"), [new UserBrief(8, "Bob", "gb"), new UserBrief(13, "Erin", "ie")], null);
    }
}

/// <summary>Body for `POST /matches` and `PUT`/`PATCH /matches/{matchId}/settings` — every field optional, only present ones are applied.</summary>
public sealed record MatchSettingsBody(
    string? Name, string? Password, bool? IsPrivate, bool? IsLocked, int? Size,
    int? MapId, int? Mods, bool? Freemod, int? TeamType, int? WinCondition);
