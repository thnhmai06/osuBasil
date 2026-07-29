using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Json;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web.Auth;
using Basil.Web.Middleware;
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
    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

    /// <summary>
    ///     `!mp make`'s own default room size — applied when <see cref="CreateMatchRequest.Size" /> is omitted (JSON
    ///     default 0).
    /// </summary>
    private const int DefaultCreateSize = 16;

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
                             "carries a valid `X-Admin-Key`. Response: `{ page, pageSize, totalRecords, items }`, wrapped " +
                             "in the enveloped `meta` object at the top level like every other paginated route. Public.")
            .WithTags("Matches")
            .Produces<PagedResult<MatchListItem>>()
            .WithExample(StatusCodes.Status200OK, new PagedResult<MatchListItem>(1, 50, 1,
            [
                new MatchListItem(42, "Grand Finals: Alpha vs Bravo", DateTime.Parse("2026-07-20T12:00:00Z"), null,
                    SampleRoomLive())
            ]));

        group.MapPost("/matches", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("createMatch")
            .WithSummary("Create Match")
            .WithDescription("Body: `{ name, password, isPrivate, mapId, mods, freemod, teamType, " +
                             "winCondition, size }` — every field required except `password` (nullable). `mapId: -1` " +
                             "means no beatmap chosen yet. No chat \"sender\" exists over HTTP, so the new match starts " +
                             "with host id 0 and no referees — assign both via the `host`/`addref` actions afterward. " +
                             "Returns the full settings representation (not a bare id)." + AdminKeyNote)
            .WithTags("Matches")
            .Produces<MatchSettingsView>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithExample(StatusCodes.Status201Created, SampleSettings())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
            .WithLink(StatusCodes.Status201Created, "GetMatchReport", "getMatchReport",
                "Fetch the full TRT report for the newly created match.",
                ("matchId", "$response.body#/data/id"))
            .WithLink(StatusCodes.Status201Created, "GetMatchSettings", "getMatchSettings",
                "Read back the newly created match's room settings.",
                ("matchId", "$response.body#/data/id"))
            .WithLink(StatusCodes.Status201Created, "ReplaceMatchSettings", "replaceMatchSettings",
                "Replace the newly created match's room settings.",
                ("matchId", "$response.body#/data/id"))
            .WithLink(StatusCodes.Status201Created, "UpdateMatchSettings", "updateMatchSettings",
                "Partially update the newly created match's room settings.",
                ("matchId", "$response.body#/data/id"));

        group.MapGet("/matches/{matchId:int}/settings", HandleSettingsGet)
            .WithGroupName("basilapi")
            .WithName("getMatchSettings")
            .WithSummary("Get Match Settings")
            .WithDescription("Plain JSON snapshot of the room-configuration fields — never includes the raw " +
                             "password, only `hasPassword`. 404 if the match isn't currently live. For a live push stream " +
                             "of the same shape, see `GET /matches/{matchId}/settings/live`. Public, no authentication.")
            .WithTags("Match Settings")
            .Produces<MatchSettingsView>()
            .WithExample(StatusCodes.Status200OK, SampleSettings())
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/matches/{matchId:int}/settings/live", HandleSettingsStream)
            .WithGroupName("basilapi")
            .WithMetadata(SseEndpointMarker.Instance)
            .WithName("getMatchSettingsLive")
            .WithSummary("Get Match Settings Live Stream")
            .WithDescription("Server-Sent Events stream (event name `settings`) scoped to just the " +
                             "room-configuration fields — first event is the full current settings, every event after " +
                             "is an RFC 7396 JSON Merge Patch against the previous one. Never includes the raw " +
                             "password, only `hasPassword`. 409 (enveloped) if the match isn't currently live — never " +
                             "opens a stream that would never receive a frame. Public, no authentication.")
            .WithTags("Match Settings")
            .Produces<MatchSettingsView>()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status200OK, SampleSettings());

        group.MapPut("/matches/{matchId:int}/settings", HandleSettingsReplace)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("replaceMatchSettings")
            .WithSummary("Replace Match Settings")
            .WithDescription("Body: `{ name, password, isPrivate, isLocked, size, mapId, mods, freemod, " +
                             "teamType, winCondition }` — every field required except `password` (nullable). `mapId: -1` " +
                             "clears/skips the beatmap selection. `freemod: true` enables FreeMod (ignoring `mods` for " +
                             "that call); `mods` alone (no `freemod`) sets the room's fixed mod set. Maps to `!mp " +
                             "name/password/private/lock+unlock/size/map/mods/set`. 404 if the match isn't currently " +
                             "live; 400 if `mapId` doesn't resolve to a known beatmap. Returns the updated settings " +
                             "representation." + AdminKeyNote)
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
            .WithDescription("Body: any subset of `{ name, password, isPrivate, isLocked, size, mapId, " +
                             "mods, freemod, teamType, winCondition }` — only present fields are touched." +
                             AdminKeyNote)
            .WithTags("Match Settings")
            .Produces<MatchSettingsView>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, SampleSettings())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("No beatmap with id 654 found locally."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/matches/{matchId:int}/live", HandleMainLiveStream)
            .WithGroupName("basilapi")
            .WithMetadata(SseEndpointMarker.Instance)
            .WithName("getMatchLive")
            .WithSummary("Get Match Live Stream")
            .WithDescription("Server-Sent Events stream (event name `main`) of the full live snapshot — " +
                             "room config, host, referees, current beatmap, in-progress flag, and every slot. First " +
                             "event is the full current state, every event after is an RFC 7396 JSON Merge Patch against " +
                             "the previous one — no per-player score data on this channel, see " +
                             "`GET /matches/{matchId}/live/{slotIndex}` for that. 409 (enveloped) if the match isn't " +
                             "currently live — never opens a stream that would never receive a frame. For a one-shot " +
                             "JSON snapshot including historical rounds/events, see `GET /matches/{matchId}`. Public, no " +
                             "authentication.")
            .WithTags("Match Live")
            .Produces<MatchLiveSnapshot>()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status200OK, SampleLiveSnapshot());

        group.MapGet("/matches/{matchId:int}/live/{slotIndex:int}", HandleLiveSlotStream)
            .WithGroupName("basilapi")
            .WithMetadata(SseEndpointMarker.Instance)
            .WithName("getMatchSlotLiveStream")
            .WithSummary("Get Match Slot Live Stream")
            .WithDescription("`{slotIndex}` is 1-16 (matching `!mp move`'s convention). One SSE stream " +
                             "tagging three feeds by event name: `slot` (that slot's membership/status/team/mods, " +
                             "full-then-delta, `MatchSlotView`), `score` (the current occupant's live score frames during " +
                             "a round, forwarded as-is, `PlayerLiveScore`), and `input` (the current occupant's raw " +
                             "spectator-input frames, forwarded as-is, `SpectateFramesEvent` — same shape as " +
                             "`GET /users/{idOrName}/live`'s single event). Follows whoever currently occupies the slot — " +
                             "if the occupant changes, the next `slot` event reflects that, and `score`/`input` start " +
                             "matching the new occupant automatically. 404 if the match isn't currently live or " +
                             "`slotIndex` is out of range. The declared schema below is `oneOf` the three shapes — which " +
                             "one a given message is is carried by the SSE-protocol `event:` field itself (not a JSON " +
                             "discriminator property inside the body), so pick the shape by that field, not by inspecting " +
                             "the JSON's own properties. Public, no authentication.")
            .WithTags("Match Live")
            .Produces<PlayerLiveScore>()
            .WithSlotLiveExamples()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapMatchSubResourceRoutes();
    }

    private static async Task<IResult> HandleList(
        [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize,
        HttpContext context, IMatchRegistry matchRegistry, IMatchPersistenceRepository matchPersistence,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var (p, ps) = Pagination.Normalize(page, pageSize);
        var mode = (status ?? "online").ToLowerInvariant();
        if (mode is not ("online" or "offline" or "all")) mode = "online";
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);

        var rows = await matchPersistence.FetchAllMatchesAsync(cancellationToken);
        var filtered = rows
            .Select(row => (Row: row, Live: matchRegistry.GetByDbId(row.Id)))
            .Where(t =>
            {
                var isOpen = t.Live is not null;
                if (mode == "online" && !isOpen) return false;
                if (mode == "offline" && isOpen) return false;
                return !isOpen || !t.Live!.IsPrivate || isAdmin;
            })
            .OrderByDescending(t => t.Row.Id)
            .ToList();

        var items = new List<MatchListItem>(filtered.Count);
        foreach (var t in filtered)
        {
            var live = t.Live is not null
                ? await MatchLiveSnapshotBuilder.BuildRoomLive(t.Live, maps, cancellationToken)
                : null;
            items.Add(new MatchListItem(t.Row.Id, t.Row.Name, t.Row.CreatedAt, t.Row.EndedAt, live));
        }

        var overqueried = items.Skip((p - 1) * ps).Take(ps + 1).ToList();
        return Results.Json(Pagination.Trim(overqueried, p, ps, items.Count));
    }

    private static async Task<IResult> HandleCreate(CreateMatchRequest body, MatchMembershipService matchMembership,
        MatchControlService matchControl, IPlayerSessionRegistry sessionRegistry, IUserRepository users,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrEmpty(body.Name) ? "New match" : body.Name;
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
            await matchControl.SetPrivateAsync(match, body.IsPrivate, cancellationToken);
            await matchControl.SetSizeAsync(match, body.Size > 0 ? body.Size : DefaultCreateSize, cancellationToken);

            var mapError = await ApplyFullMapAsync(match, body.MapId, matchControl, cancellationToken);
            if (mapError is not null) return mapError;

            await ApplyFullModsAsync(match, body.Mods, body.Freemod, matchControl, cancellationToken);
            await matchControl.SetTeamTypeWinConditionAndSizeAsync(match, body.TeamType, body.WinCondition, null,
                cancellationToken);
        }
        finally
        {
            match.Lock.Release();
        }

        var settings =
            await MatchLiveSnapshotBuilder.BuildSettings(match, sessionRegistry, users, maps, cancellationToken);
        return Results.Created($"/matches/{match.DbId}/settings", settings);
    }

    private static async Task<IResult> HandleSettingsGet(int matchId, IMatchRegistry matchRegistry,
        IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMapRepository maps,
        CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null) return Results.NotFound();

        return Results.Json(
            await MatchLiveSnapshotBuilder.BuildSettings(match, sessionRegistry, users, maps, cancellationToken));
    }

    private static IResult HandleSettingsStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
        IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null) return LiveSseRoutes.NotLive();

        return LiveSseRoutes.HandleSettings(context, matchId, events,
            () => match.SettingsSnapshot.Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
                : null,
            cancellationToken);
    }

    private static IResult HandleMainLiveStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
        IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null) return LiveSseRoutes.NotLive();

        return LiveSseRoutes.HandleMain(context, matchId, events,
            () => match.MainSnapshot.Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
                : null,
            cancellationToken);
    }

    private static IResult HandleLiveSlotStream(int matchId, int slotIndex, HttpContext context,
        IMatchRegistry matchRegistry, IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents,
        IPlayerSessionRegistry sessionRegistry, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null || slotIndex is < 1 or > 16)
            return LiveSseRoutes.SseError(StatusCodes.Status404NotFound,
                "Match is not currently live, or slotIndex is out of range.");

        var index = slotIndex - 1;
        return LiveSseRoutes.HandleLiveSlot(context, match, index, matchEvents, inputEvents, sessionRegistry,
            () => match.SlotSnapshots[index].Latest is { } snapshot
                ? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
                : null,
            cancellationToken);
    }

    private static async Task<IResult> HandleSettingsReplace(int matchId, ReplaceMatchSettingsRequest body,
        IMatchRegistry matchRegistry, MatchControlService matchControl, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, IMapRepository maps, CancellationToken cancellationToken)
    {
        var match = matchRegistry.GetByDbId(matchId);
        if (match is null) return Results.NotFound();

        await match.Lock.WaitAsync(cancellationToken);
        try
        {
            await matchControl.SetNameAsync(match, body.Name, cancellationToken);
            await matchControl.SetPasswordAsync(match, body.Password ?? "", cancellationToken);
            await matchControl.SetPrivateAsync(match, body.IsPrivate, cancellationToken);
            matchControl.SetLocked(match, body.IsLocked);
            await matchControl.SetSizeAsync(match, body.Size, cancellationToken);

            var mapError = await ApplyFullMapAsync(match, body.MapId, matchControl, cancellationToken);
            if (mapError is not null) return mapError;

            await ApplyFullModsAsync(match, body.Mods, body.Freemod, matchControl, cancellationToken);
            await matchControl.SetTeamTypeWinConditionAndSizeAsync(match, body.TeamType, body.WinCondition, null,
                cancellationToken);
        }
        finally
        {
            match.Lock.Release();
        }

        return Results.Json(
            await MatchLiveSnapshotBuilder.BuildSettings(match, sessionRegistry, users, maps, cancellationToken));
    }

    private static async Task<IResult> HandleSettingsUpdate(int matchId, UpdateMatchSettingsRequest body,
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

        return Results.Json(
            await MatchLiveSnapshotBuilder.BuildSettings(match, sessionRegistry, users, maps, cancellationToken));
    }

    /// <summary>
    ///     Shared by <see cref="HandleCreate" />/<see cref="HandleSettingsReplace" /> — both apply every
    ///     field unconditionally (PUT-style, no "was this given" check), unlike
    ///     <see cref="ApplySettingsAsync" />'s PATCH-style partial application. `-1` is the established
    ///     "no beatmap chosen" sentinel (see <see cref="MatchRoomCore" />'s doc comment); ids auto-
    ///     increment from 1 in this schema (see CLAUDE.md), so 0 (JSON's default for an omitted field)
    ///     can never be a real beatmap either — both are skipped entirely rather than attempted as a
    ///     lookup (which would otherwise fail with a confusing "beatmap not found" error for a caller
    ///     correctly signalling "no map").
    /// </summary>
    private static async Task<IResult?> ApplyFullMapAsync(MatchSession match, int mapId,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        if (mapId <= 0) return null;

        var (result, _) = await matchControl.SetMapAsync(match, mapId, cancellationToken);
        return result == MatchControlService.SetMapResult.BeatmapNotFound
            ? Results.BadRequest(new ErrorResponse($"No beatmap with id {mapId} found locally."))
            : null;
    }

    /// <summary>
    ///     Shared by <see cref="HandleCreate" />/<see cref="HandleSettingsReplace" /> — `freemod: true` ignores `mods`
    ///     for that call, matching real Bancho.
    /// </summary>
    private static async Task ApplyFullModsAsync(MatchSession match, Mods mods, bool freemod,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        if (freemod)
            await matchControl.SetModsAsync(match, Mods.NoMod, true, cancellationToken);
        else
            await matchControl.SetModsAsync(match, mods, false, cancellationToken);
    }

    /// <summary>Caller must hold <paramref name="match" />'s Lock. Returns a non-null error IResult on failure.</summary>
    private static async Task<IResult?> ApplySettingsAsync(MatchSession match, UpdateMatchSettingsRequest body,
        MatchControlService matchControl, CancellationToken cancellationToken)
    {
        if (body.Name is not null) await matchControl.SetNameAsync(match, body.Name, cancellationToken);
        if (body.Password is not null) await matchControl.SetPasswordAsync(match, body.Password, cancellationToken);
        if (body.IsPrivate is not null)
            await matchControl.SetPrivateAsync(match, body.IsPrivate.Value, cancellationToken);
        if (body.IsLocked is not null) matchControl.SetLocked(match, body.IsLocked.Value);
        if (body.Size is not null) await matchControl.SetSizeAsync(match, body.Size.Value, cancellationToken);

        if (body.MapId is not null)
        {
            var (result, _) = await matchControl.SetMapAsync(match, body.MapId.Value, cancellationToken);
            if (result == MatchControlService.SetMapResult.BeatmapNotFound)
                return Results.BadRequest(new ErrorResponse($"No beatmap with id {body.MapId.Value} found locally."));
        }

        if (body.Freemod == true)
            await matchControl.SetModsAsync(match, Mods.NoMod, true, cancellationToken);
        else if (body.Mods is not null)
            await matchControl.SetModsAsync(match, body.Mods.Value, false, cancellationToken);

        if (body.TeamType is not null || body.WinCondition is not null)
            await matchControl.SetTeamTypeWinConditionAndSizeAsync(match,
                body.TeamType ?? match.TeamType, body.WinCondition, null, cancellationToken);

        return null;
    }

    private static MatchSettingsView SampleSettings()
    {
        return new MatchSettingsView(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
            Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard,
            new UserBrief(7, "Alice", Country.Us),
            [new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)],
            SampleBeatmap());
    }

    private static MatchRoomLive SampleRoomLive()
    {
        return new MatchRoomLive(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
            Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard,
            true, SampleBeatmap());
    }

    internal static MatchLiveSnapshot SampleLiveSnapshot()
    {
        var slots = new List<MatchSlotView>(16);
        for (var i = 0; i < 16; i++)
            slots.Add(new MatchSlotView(i, null, SlotStatus.Open, MatchTeam.Neutral, Mods.NoMod, false, false));
        slots[0] = new MatchSlotView(0, new UserBrief(7, "Alice", Country.Us), SlotStatus.Playing, MatchTeam.Red,
            Mods.NoMod, true, true);

        return new MatchLiveSnapshot(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
            Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard, true,
            new UserBrief(7, "Alice", Country.Us),
            [new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)],
            SampleBeatmap(), slots);
    }

    private static BeatmapDetail SampleBeatmap()
    {
        var created = DateTime.Parse("2026-06-01T10:00:00Z");
        var beatmapset = new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created,
            created, false, false, RankedStatus.Loved, 1);
        var difficulty = new Difficulty(GameMode.Standard, 174, 4, 9, 8, 6, 6.42);
        return new BeatmapDetail("d41d8cd98f00b204e9800998ecf8427e", 654, "Extreme", TimeSpan.FromSeconds(225), 1234,
            difficulty, new Dictionary<string, int> { ["circle"] = 620, ["slider"] = 210, ["spinner"] = 2 }, false,
            beatmapset);
    }
}

/// <summary>Body for `POST /matches` — every field required except `password`. `mapId: -1` means no beatmap chosen.</summary>
public sealed record CreateMatchRequest(
    string Name,
    string? Password,
    bool IsPrivate,
    int MapId,
    Mods Mods,
    bool Freemod,
    MatchTeamType TeamType,
    MatchWinCondition WinCondition,
    int Size);

/// <summary>
///     Body for `PUT /matches/{matchId}/settings` — full replace, every field required except `password`. `mapId: -1`
///     means no beatmap chosen.
/// </summary>
public sealed record ReplaceMatchSettingsRequest(
    string Name,
    string? Password,
    bool IsPrivate,
    bool IsLocked,
    int Size,
    int MapId,
    Mods Mods,
    bool Freemod,
    MatchTeamType TeamType,
    MatchWinCondition WinCondition);

/// <summary>Body for `PATCH /matches/{matchId}/settings` — every field optional, only present ones are applied.</summary>
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