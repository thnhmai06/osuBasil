using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Application.Configuration;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Scores;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Beatmaps;
using Basil.Protocol.Packets;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace Basil.Web.Routing;

/// <summary>
///     Ports bancho.py's app/api/init_api.py:init_routes — routes are selected by hostname, not
///     path prefix. For every domain in ("ppy.sh", configured DOMAIN): c./ce./c4./c5./c6.{domain}
///     serve the bancho realtime protocol, osu.{domain} serves the osu! web endpoints, b.{domain}
///     serves beatmap assets, and api.{domain} serves the developer-facing API.
/// </summary>
public static class BanchoHostGroups
{
    private static readonly string[] BanchoSubdomains = ["c", "ce", "c4", "c5", "c6"];

    private const string BanchoPacketCatalog = """
        The request body is a sequence of bancho packets (little-endian: uint16 packet id, uint8
        padding byte, uint32 payload length, then the payload). Multiple packets may be concatenated
        in one request. The packet id selects the operation:

        Core: Ping (keepalive), ChangeAction (status/mode/mods update), RequestStatusUpdate,
        UserStatsRequest, UserPresenceRequest, UserPresenceRequestAll, ReceiveUpdates (friends-only
        toggle), SetAwayMessage, Logout.

        Channels: ChannelJoin, ChannelPart, SendPublicMessage, SendPrivateMessage,
        ToggleBlockNonFriendDms.

        Spectating: StartSpectating, StopSpectating, SpectateFrames (raw replay-frame bytes — also
        published live to the Basil API's `GET /users/{id}/live` SSE channel), CantSpectate.

        Multiplayer: CreateMatch, JoinMatch, PartMatch, MatchChangeSlot, MatchChangeSettings,
        MatchChangePassword, MatchChangeMods, MatchChangeTeam, MatchLock, MatchTransferHost,
        MatchReady, MatchNotReady, MatchStart, MatchLoadComplete, MatchSkipRequest, MatchNoBeatmap,
        MatchHasBeatmap, MatchFailed, MatchScoreUpdate (also published live to the Basil API's
        `GET /matches/{id}/live/{slotIndex}` SSE channel), MatchComplete, MatchInvite,
        TournamentMatchInfoRequest, TournamentJoinMatchChannel, TournamentLeaveMatchChannel.

        The response body is the same wire format: zero or more queued packets addressed back to
        this client (chat messages, other players' presence/stats updates, match state changes,
        etc). An empty body is a normal, valid response meaning "nothing queued since last poll."
        """;

    public static void MapAll(WebApplication app, string configuredDomain)
    {
        var domains = new[] { "ppy.sh", configuredDomain }.Distinct().ToArray();

        var banchoHosts = domains
            .SelectMany(domain => BanchoSubdomains.Select(subdomain => $"{subdomain}.{domain}"))
            .ToArray();
        var osuWebHosts = domains.Select(domain => $"osu.{domain}").ToArray();
        var beatmapAssetHosts = domains.Select(domain => $"b.{domain}").ToArray();
        var avatarHosts = domains.Select(domain => $"a.{domain}").ToArray();
        var apiHosts = domains.Select(domain => $"api.{domain}").ToArray();

        app.MapGroup("/").RequireHost(banchoHosts).MapBanchoGroup();
        app.MapGroup("/").RequireHost(osuWebHosts).MapOsuWebGroup();
        app.MapGroup("/").RequireHost(beatmapAssetHosts).MapBeatmapAssetGroup();
        app.MapGroup("/").RequireHost(avatarHosts).MapAvatarGroup();
        app.MapGroup("/").RequireHost(apiHosts).MapApiGroup();
    }

    // NOTE: placeholder handlers still cover the other host groups — real endpoints are registered
    // in MapOsuWebGroup, MapBeatmapAssetGroup, and MapApiGroup respectively.
    extension(RouteGroupBuilder group)
    {
        private void MapBanchoGroup()
        {
            group.MapGet("/", () => "cho")
                .WithGroupName("bancho")
                .WithSummary("Health-check stub.")
                .WithDescription("Returns the literal string \"cho\". Not sent by the real osu! " +
                    "client — exists only as a trivial liveness probe for this host.")
                .WithTags("Bancho Protocol");

            // Ported from app/api/domains/cho.py's bancho_handler: no osu-token header means this is
            // a login request; a present-but-unknown token means the server restarted since the
            // client's last request, so it's told to reconnect. Services are resolved from
            // context.RequestServices rather than as delegate parameters so the (DB/Redis-backed)
            // login use case is only ever constructed on the branch that actually needs it.
            group.MapPost("/", async (HttpContext context, CancellationToken cancellationToken) =>
            {
                var request = context.Request;
                var response = context.Response;

                using var bodyStream = new MemoryStream();
                await request.Body.CopyToAsync(bodyStream, cancellationToken);
                var body = bodyStream.ToArray();

                var token = request.Headers["osu-token"].FirstOrDefault();
                byte[] responseBody;
                if (string.IsNullOrEmpty(token))
                {
                    var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
                    // In production a reverse proxy (nginx) always sets X-Forwarded-For; without one
                    // in front (e.g. local dev), synthesise it from the direct TCP peer so
                    // IpResolver — which intentionally mirrors bancho.py's proxy-only assumption
                    // — still has something to read.
                    if (!headers.ContainsKey("CF-Connecting-IP") && !headers.ContainsKey("X-Forwarded-For"))
                    {
                        // IpResolver falls back to X-Real-IP whenever X-Forwarded-For has a
                        // single entry (matching bancho.py's own assumption about nginx's config) —
                        // both need synthesising together, not just one.
                        var remoteIp = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
                        headers["X-Forwarded-For"] = remoteIp;
                        headers["X-Real-IP"] = remoteIp;
                    }

                    var ip = Geolocation.PhraseIpAddress(headers);
                    var loginUseCase = context.RequestServices.GetRequiredService<LoginService>();
                    var loginResult =
                        await loginUseCase.ExecuteAsync(new LoginRequest(body, headers, ip), cancellationToken);
                    response.Headers["cho-token"] = loginResult.OsuToken;
                    responseBody = loginResult.ResponseBody;
                }
                else
                {
                    var sessionRegistry = context.RequestServices.GetRequiredService<IPlayerSessionRegistry>();
                    var session = sessionRegistry.GetByToken(token);
                    if (session is null)
                    {
                        responseBody = ServerPacketWriter.Notification("Server has restarted.")
                            .Concat(ServerPacketWriter.RestartServer(0))
                            .ToArray();
                    }
                    else
                    {
                        var dispatcher = context.RequestServices.GetRequiredService<BanchoPacketDispatcher>();
                        await dispatcher.DispatchAsync(session, body);
                        session.LastRecvTime = DateTimeOffset.UtcNow;
                        responseBody = session.Dequeue();
                    }
                }

                response.ContentType = "text/html; charset=UTF-8";
                await response.Body.WriteAsync(responseBody, cancellationToken);
            })
                .WithGroupName("bancho")
                .WithSummary("Login (no osu-token header) or authenticated packet exchange (osu-token header present).")
                .WithDescription("Without an `osu-token` request header, the body is the client's login block " +
                    "(username/password/client info) and a successful response carries a `cho-token` response " +
                    "header the client must echo on every subsequent request. With a known `osu-token`, the body " +
                    "is one or more bancho packets to process, and the response is any packets queued for this " +
                    "client since its last poll (this is a long-poll style protocol — the client calls this " +
                    "endpoint repeatedly). An unrecognized `osu-token` (e.g. after a server restart) gets a " +
                    "\"Server has restarted\" notification plus a restart-client packet instead of processing " +
                    "the body.\n\n" + BanchoPacketCatalog)
                .WithTags("Bancho Protocol");
        }

        private void MapOsuWebGroup()
        {
            group.MapGet("/", () => "osu")
                .WithGroupName("osuweb")
                .WithSummary("Health-check stub.")
                .WithDescription("Returns the literal string \"osu\". Not called by the real osu! client.")
                .WithTags("Stubs");

            // Serves ServerOptions.MenuIconPath as an image — the in-game main menu icon is
            // configured as a local file path (not a URL) so it doesn't depend on external hosting;
            // LoginService points the client at this endpoint instead of the file path itself.
            // Unauthenticated by design — fetched by the client's own image loader, not through the
            // bancho session.
            group.MapGet("/web/menuicon", (HttpContext context) =>
            {
                var serverOptions = context.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value;
                var path = Path.IsPathRooted(serverOptions.MenuIconPath)
                    ? serverOptions.MenuIconPath
                    : Path.Combine(AppContext.BaseDirectory, serverOptions.MenuIconPath);
                if (!File.Exists(path)) return Results.NotFound();

                var contentType = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    _ => "application/octet-stream"
                };
                return Results.File(path, contentType);
            })
                .WithGroupName("osuweb")
                .WithSummary("The in-game main menu icon image.")
                .WithDescription("Serves the image configured as `Server:MenuIconPath` in Settings.toml. " +
                    "Unauthenticated — fetched directly by the client's own image loader, independent of the " +
                    "bancho session. 404 if the configured file doesn't exist on disk.")
                .WithTags("Menu");

            // Ported from app/api/domains/osu.py's getScores, reduced to a status-only reply — this
            // server doesn't support browsing a beatmap's leaderboard (out of scope), but it still
            // reports the map's real RankedStatus (matching osu-search.php/osu-search-set.php)
            // instead of the "-1|false" (NotSubmitted) stub bancho.py falls back to when there's no
            // leaderboard, so Song Select's map icon doesn't disagree with osu!Direct. Matches the
            // old BeatmapLeaderboardResultCode.NoLeaderboard wire format (see git history) minus the
            // leaderboard rows themselves. `c` (md5) is also the one request osu! sends on every
            // song-select map change carrying mode/mods (m/mods) — that side effect (mirrors
            // ChangeActionHandler/BeatmapLeaderboardService._update_player_status_if_needed) still
            // needs to run so other players see an accurate status.
            group.MapGet("/web/osu-osz2-getscores.php", async (
                [FromQuery(Name = "us")] string username,
                [FromQuery(Name = "ha")] string ha,
                [FromQuery(Name = "c")] string? checksum,
                [FromQuery(Name = "m")] int m,
                [FromQuery(Name = "mods")] int mods,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, ha, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                var mode = (GameMode)m;
                var requestedMods = (Mods)mods;
                if (mode != player.Status.Mode || requestedMods != player.Status.Mods)
                {
                    player.Status.Mode = mode;
                    player.Status.Mods = requestedMods;

                    if (!player.Restricted)
                    {
                        var sessionRegistry = context.RequestServices.GetRequiredService<IPlayerSessionRegistry>();
                        var statsPacket = PacketBuilders.BuildUserStats(player);
                        foreach (var other in sessionRegistry.All) other.Enqueue(statsPacket);
                    }
                }

                Beatmap? bmap = null;
                if (!string.IsNullOrEmpty(checksum))
                {
                    var maps = context.RequestServices.GetRequiredService<IMapRepository>();
                    bmap = await maps.FetchOneAsync(md5: checksum, cancellationToken: cancellationToken);
                }

                var status = bmap is null ? RankedStatus.NotSubmitted : bmap.Mapset.Status;

                return Results.Text($"{(int)status}|false", "text/html", Encoding.UTF8);
            })
                .WithGroupName("osuweb")
                .WithSummary("Song-select leaderboard status check (no leaderboard browsing).")
                .WithDescription("Called by the client on every song-select map change. Authenticates via `us`/`ha` " +
                    "(username/password MD5), updates and broadcasts the player's mode/mods status if `m`/`mods` " +
                    "changed, and replies with `{rankedStatus}|false` for the map identified by `c` (its MD5 " +
                    "checksum) — this server has no online leaderboard browsing, so the score-rows portion of " +
                    "the real osu! response is always empty.")
                .WithTags("Beatmaps");

            // Ported from app/api/domains/osu.py's osuSearchHandler, replumbed to query the local
            // maps table instead of proxying a mirror API — runs fully offline now.
            group.MapGet("/web/osu-search.php", async (
                [FromQuery(Name = "u")] string username,
                [FromQuery(Name = "h")] string passwordMd5,
                [FromQuery(Name = "q")] string query,
                [FromQuery(Name = "m")] int mode,
                [FromQuery(Name = "p")] int pageNum,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is null)
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);

                var searchService = context.RequestServices.GetRequiredService<DirectSearchService>();
                var request = new DirectSearchRequest(query, mode, pageNum);
                var beatmapSets = await searchService.SearchAsync(request, cancellationToken);

                return Results.Text(DirectSearchService.Format(beatmapSets), "text/html", Encoding.UTF8);
            })
                .WithGroupName("osuweb")
                .WithSummary("osu!direct beatmap search (local database only).")
                .WithDescription("Backs the in-game osu!direct search panel. Queries this server's own beatmap " +
                    "table — there is no external mirror, so results are limited to whatever has been ingested " +
                    "locally. `q` is a free-text query, `m` a game mode filter, `p` a zero-based page number. " +
                    "Response is osu!'s pipe/newline wire format, not JSON.")
                .WithTags("Beatmaps");

            // Ported from app/api/domains/osu.py's osuSearchSetHandler. "s"/"b"/"c" are all optional —
            // exactly one is expected per request, matching the Python source's if/elif/elif/else.
            group.MapGet("/web/osu-search-set.php", async (
                [FromQuery(Name = "u")] string username,
                [FromQuery(Name = "h")] string passwordMd5,
                [FromQuery(Name = "s")] int? mapSetId,
                [FromQuery(Name = "b")] int? mapId,
                [FromQuery(Name = "c")] string? checksum,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is null)
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);

                if (mapSetId is null && mapId is null && checksum is null)
                    return Results.Text("", "text/html", Encoding.UTF8);

                var maps = context.RequestServices.GetRequiredService<IMapRepository>();
                var bmapSet =
                    await maps.FetchOneAsync(mapId, checksum, setId: mapSetId, cancellationToken: cancellationToken);

                return Results.Text(DirectSearchService.FormatSet(bmapSet), "text/html", Encoding.UTF8);
            })
                .WithGroupName("osuweb")
                .WithSummary("Look up one beatmap set, by set id, map id, or checksum.")
                .WithDescription("Exactly one of `s` (mapset id), `b` (a beatmap id within the set), or `c` " +
                    "(a beatmap's MD5) is expected per request. Empty body if the set can't be resolved or none " +
                    "of the three parameters is present. Response is osu!'s pipe-delimited set-info line, not JSON.")
                .WithTags("Beatmaps");

            // Ported from app/api/domains/osu.py's get_osz, extended for this server's fully-offline
            // scope: if the set was locally ingested (BeatmapIngestionService/BeatmapWatcherService), a
            // fresh .osz is packaged on the fly from the mapset's own storage folder (the full original
            // archive contents — audio/images/video/.osu — not just difficulty files). Only falls back
            // to a configured mirror (kept from the original port) when the set has nothing local.
            group.MapGet("/d/{mapSetId}",
                async (string mapSetId, HttpContext context, CancellationToken cancellationToken) =>
                {
                    const char noVideoSuffix = 'n';
                    var noVideo = mapSetId.EndsWith(noVideoSuffix);
                    var rawSetId = noVideo ? mapSetId[..^1] : mapSetId;

                    if (int.TryParse(rawSetId, out var setId))
                    {
                        var maps = context.RequestServices.GetRequiredService<IMapRepository>();
                        var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
                        var osz = await BuildOszArchiveAsync(maps, storage, setId, noVideo, cancellationToken);
                        if (osz is not null)
                            return Results.File(osz.Value.Bytes, "application/x-osu-beatmap-archive", osz.Value.FileName);
                    }

                    var mirrorOptions = context.RequestServices.GetRequiredService<IOptions<MirrorOptions>>().Value;
                    if (string.IsNullOrEmpty(mirrorOptions.DownloadEndpoint))
                        return Results.Text("Beatmap downloads are not available on this server.", "text/html",
                            Encoding.UTF8);

                    const int noVideoQueryValue = 0;
                    const int withVideoQueryValue = 1;
                    var query = $"{rawSetId}?n={(noVideo ? noVideoQueryValue : withVideoQueryValue)}";

                    return Results.Redirect($"{mirrorOptions.DownloadEndpoint}/{query}", true);
                })
                .WithGroupName("osuweb")
                .WithSummary("osu!direct-style mapset download (the client's in-game download button).")
                .WithDescription("`{mapSetId}` may carry a trailing `n` to request a no-video archive. If the set " +
                    "was ingested locally, a fresh `.osz` (`application/x-osu-beatmap-archive`) is built on the fly " +
                    "from its storage folder. Otherwise, falls back to redirecting to a configured mirror " +
                    "(`Mirror:DownloadEndpoint`), or returns a plain-text \"not available\" message if no mirror " +
                    "is configured. Prefer `GET /beatmapsets/{id}/download` on the Basil API for external tooling — " +
                    "this route exists for the in-game client specifically.")
                .WithTags("Beatmaps");

            // Ported from app/api/domains/osu.py's get_updated_beatmap, replumbed to serve the locally
            // ingested file instead of redirecting to osu.ppy.sh (this server runs fully offline).
            group.MapGet("/web/maps/{mapFilename}", async (string mapFilename, HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var maps = context.RequestServices.GetRequiredService<IMapRepository>();
                var bmap = await maps.FetchOneAsync(filename: mapFilename, cancellationToken: cancellationToken);
                if (bmap is null) return Results.NotFound();

                var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
                var osuPath = BeatmapIngestionService.OsuFilePath(storage, bmap);
                return File.Exists(osuPath) ? Results.File(osuPath, "application/x-osu-beatmap") : Results.NotFound();
            })
                .WithGroupName("osuweb")
                .WithSummary("Fetch the current .osu file for one difficulty, by its stored filename.")
                .WithDescription("Called by the client when it detects its local copy of a difficulty is stale. " +
                    "`{mapFilename}` is the exact filename recorded in this server's beatmap table (not a beatmap " +
                    "id). 404 if no matching row exists, or the file is missing on disk. " +
                    "Content-Type `application/x-osu-beatmap`.")
                .WithTags("Beatmaps");

            // Ported from app/api/domains/osu.py's osuSubmitModularSelector. The "score" field name is
            // reused by the client for both the base64 score-data string and the replay file upload —
            // ASP.NET Core's multipart parser already separates text parts from file parts by name, so
            // (unlike Starlette/FastAPI) no manual form-field workaround is needed here. Unused fields
            // the Python source reads but never forwards into submission (fs/x/i) are not bound at all.
            group.MapPost("/web/osu-submit-modular-selector.php",
                async (HttpContext context, CancellationToken cancellationToken) =>
                {
                    if (!context.Request.HasFormContentType) return Results.Text("", "text/html", Encoding.UTF8);

                    var form = await context.Request.ReadFormAsync(cancellationToken);
                    var scoreDataB64 = form["score"].FirstOrDefault();
                    if (scoreDataB64 is null) return Results.Text("", "text/html", Encoding.UTF8);

                    byte[]? replayData = null;
                    var replayFile = form.Files.GetFile("score");
                    if (replayFile is not null)
                    {
                        using var replayStream = new MemoryStream();
                        await replayFile.CopyToAsync(replayStream, cancellationToken);
                        replayData = replayStream.ToArray();
                    }

                    var osuVersion = form["osuver"].FirstOrDefault() ?? "";
                    var decryptor = context.RequestServices.GetRequiredService<IScoreDecryptor>();
                    var (scoreDataFields, clientHash) = decryptor.Decrypt(
                        scoreDataB64, form["s"].FirstOrDefault() ?? "", form["iv"].FirstOrDefault() ?? "", osuVersion);

                    var useCase = context.RequestServices.GetRequiredService<ScoreSubmissionService>();
                    var outcome = await useCase.SubmitAsync(
                        new ScoreSubmissionRequest(
                            scoreDataFields,
                            form["pass"].FirstOrDefault() ?? "",
                            osuVersion,
                            clientHash,
                            form["c1"].FirstOrDefault() ?? "",
                            form["sbk"].FirstOrDefault(),
                            form["bmk"].FirstOrDefault() ?? "",
                            int.Parse(form["st"].FirstOrDefault() ?? "0"),
                            int.Parse(form["ft"].FirstOrDefault() ?? "0"),
                            replayData),
                        cancellationToken);

                    var domain = context.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value.Domain;
                    var body = outcome.Code == ScoreSubmissionResultCode.Success
                        ? ScoreSubmissionResponseBuilder.BuildSuccess(outcome.Result!, domain)
                        : ScoreSubmissionResponseBuilder.BuildError(outcome.Code);

                    return Results.Text(body, "text/html", Encoding.UTF8);
                })
                .WithGroupName("osuweb")
                .WithSummary("Submit a completed play (score + replay).")
                .WithDescription("Multipart form submission the client sends after a play completes. The `score` " +
                    "field carries both a base64-encoded, encrypted score payload and (as a file part of the same " +
                    "name) the replay data; `s`/`iv`/`osuver` are the decryption key material. On success, " +
                    "persists the score, updates the beatmap's play/pass counters, and returns the client's " +
                    "post-score screen data (rank, PP-less score charts, etc., in osu!'s wire format). Never " +
                    "returns JSON — always `text/html` in osu!'s own response grammar, even on failure.")
                .WithTags("Score Submission");

            // Ported from app/api/domains/osu.py's getReplay. `mode` is accepted by the client but
            // never actually used in the Python source's fetch_replay_file call, so it's not bound
            // here either.
            group.MapGet("/web/osu-getreplay.php", async (
                [FromQuery(Name = "u")] string username,
                [FromQuery(Name = "h")] string passwordMd5,
                [FromQuery(Name = "c")] long scoreId,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                var replayService = context.RequestServices.GetRequiredService<ReplayService>();
                var result = await replayService.FetchReplayFileAsync(scoreId, cancellationToken);

                return result.Code == ReplayFetchResultCode.NotFound
                    ? Results.NotFound()
                    : Results.Bytes(result.Data!, "application/x-osu-replay");
            })
                .WithGroupName("osuweb")
                .WithSummary("Download a replay by score id (in-game \"View Replay\").")
                .WithDescription("Authenticates via `u`/`h` (username/password MD5), then serves the `.osr` " +
                    "replay for the score identified by `c`. 404 if the score has no stored replay. " +
                    "Content-Type `application/x-osu-replay`. Prefer `GET /score/{scoreId}/replay` on the Basil API " +
                    "for external tooling — this route requires client-style authentication.")
                .WithTags("Replays");

            // Ported from app/api/domains/osu.py's osuGetBeatmapInfo, reduced to a stub — per-map grade
            // lookup is out of scope (this server doesn't support browsing beatmap leaderboards).
            group.MapPost("/web/osu-getbeatmapinfo.php", async (
                [FromQuery(Name = "u")] string username,
                [FromQuery(Name = "h")] string passwordMd5,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                return Results.Text("", "text/html", Encoding.UTF8);
            })
                .WithGroupName("osuweb")
                .WithSummary("Per-map grade lookup — stub.")
                .WithDescription("Authenticates the caller, then always returns an empty body. Per-map grade " +
                    "history (used by the real client's Song Select grade icons) is out of scope — this server " +
                    "doesn't track a leaderboard to grade against.")
                .WithTags("Stubs");

            // Ported from app/api/domains/osu.py's lastFM. Per explicit user decision, detected
            // cheat-tool flags are only logged (ClientIntegrityService) — no restrict/kick machinery
            // exists, so only logging for manual review.
            group.MapGet("/web/lastfm.php", async (
                [FromQuery(Name = "b")] string beatmapIdOrHiddenFlag,
                [FromQuery(Name = "us")] string username,
                [FromQuery(Name = "ha")] string passwordMd5,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                var clientIntegrity = context.RequestServices.GetRequiredService<ClientIntegrityService>();
                var result = await clientIntegrity.HandleLastFmFlagsAsync(player, beatmapIdOrHiddenFlag, cancellationToken);

                return result == ClientIntegrityResult.StopSending
                    ? Results.Text("-3", "text/html", Encoding.UTF8)
                    : Results.Text("", "text/html", Encoding.UTF8);
            })
                .WithGroupName("osuweb")
                .WithSummary("Client anticheat flag receiver (log-only).")
                .WithDescription("The osu! client's cheat-tool detection reports land here, keyed by `b` (a " +
                    "beatmap id, or a flag string when the client itself is flagging something rather than a " +
                    "beatmap). Flags are logged for manual review only — this server has no automatic " +
                    "restrict/kick pipeline for them. Returns `-3` to tell the client to stop sending further " +
                    "flags for this session, or an empty body otherwise.")
                .WithTags("Anticheat");

            // Ported from app/api/domains/osu.py's osuMarkAsRead. No offline-mail persistence exists here
            // (chat is online-only), so this is just an auth-gated no-op to keep the client happy.
            group.MapGet("/web/osu-markasread.php", async (
                [FromQuery(Name = "u")] string username,
                [FromQuery(Name = "h")] string passwordMd5,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                return Results.Text("", "text/html", Encoding.UTF8);
            })
                .WithGroupName("osuweb")
                .WithSummary("Mark offline mail as read — no-op.")
                .WithDescription("Authenticates the caller, then always returns an empty body. This server has " +
                    "no offline-mail persistence (chat is online-only), so there is nothing to mark as read; " +
                    "this route exists only so the client doesn't treat a missing endpoint as a connectivity " +
                    "failure.")
                .WithTags("Mail");

            // Ported from app/api/domains/osu.py's osuSeasonal, replumbed to list
            // StorageOptions.SeasonalsPath instead of the settings-configured SEASONAL_BGS the Python
            // source reads — this server has no config-file list, just a folder an admin drops images
            // into (served back by the /seasonal/{file} route below).
            group.MapGet("/web/osu-getseasonal.php", (HttpContext context) =>
            {
                var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
                var domain = context.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value.Domain;
                Directory.CreateDirectory(storage.SeasonalsPath);

                var files = Directory.EnumerateFiles(storage.SeasonalsPath)
                    .Select(path => $"https://osu.{domain}/seasonal/{Path.GetFileName(path)}")
                    .ToArray();
                return Results.Json(files);
            })
                .WithGroupName("osuweb")
                .WithSummary("List seasonal background image URLs shown on the login screen.")
                .WithDescription("Returns a JSON array of full URLs (each pointing at `GET /seasonal/{fileName}` " +
                    "on this same host), one per file an admin has dropped into the seasonal-backgrounds " +
                    "storage folder. Unlike most routes on this host, the response actually is JSON, matching " +
                    "the real osu! client's expectation for this specific endpoint.")
                .WithTags("Seasonal Backgrounds");

            // Serves the files listed by osu-getseasonal.php above.
            group.MapGet("/seasonal/{fileName}", (string fileName, HttpContext context) =>
            {
                var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
                // Path.GetFileName strips any directory component the client could smuggle in
                // (e.g. "../../appsettings.json") before it ever reaches Path.Combine.
                var path = Path.Combine(storage.SeasonalsPath, Path.GetFileName(fileName));
                if (!File.Exists(path)) return Results.NotFound();

                var contentType = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    _ => "application/octet-stream"
                };
                return Results.File(path, contentType);
            })
                .WithGroupName("osuweb")
                .WithSummary("Serve one seasonal background image file.")
                .WithDescription("`{fileName}` is one of the bare filenames returned by " +
                    "`GET /web/osu-getseasonal.php`. 404 if the file doesn't exist. Content-Type is inferred " +
                    "from the file extension (png/jpg/gif; anything else is served as " +
                    "`application/octet-stream`).")
                .WithTags("Seasonal Backgrounds");

            // Ported from app/api/domains/osu.py's banchoConnect — unauthenticated by design in the
            // Python source too (can be called before a session exists).
            group.MapGet("/web/bancho_connect.php", () => Results.Text("", "text/html", Encoding.UTF8))
                .WithGroupName("osuweb")
                .WithSummary("Client connectivity check — stub.")
                .WithDescription("Always returns an empty body. Called by the client before a bancho session " +
                    "exists, so it is deliberately unauthenticated.")
                .WithTags("Stubs");

            // Ported from app/api/domains/osu.py's checkUpdates (always an empty stub response there too).
            group.MapGet("/web/check-updates.php", () => Results.Text("", "text/html", Encoding.UTF8))
                .WithGroupName("osuweb")
                .WithSummary("Client update check — stub.")
                .WithDescription("Always returns an empty body — this server does not manage or distribute " +
                    "client updates.")
                .WithTags("Stubs");

            // Screenshots, favourites, ratings, comments, and in-game registration are dropped per the
            // multiplayer/tourney-only scope decision — these routes exist only so the client doesn't
            // treat a 404 as a connectivity failure, matching the "not available on this server"
            // messaging style already used by the map-download/beatmap-file-update stubs above.
            group.MapPost("/web/osu-screenshot.php", () =>
                Results.Text("Screenshots are not available on this server.", "text/html", Encoding.UTF8,
                    StatusCodes.Status400BadRequest))
                .WithGroupName("osuweb")
                .WithSummary("Screenshot upload — not supported.")
                .WithDescription("Always returns 400 with an explanatory message. Screenshot hosting is out of " +
                    "scope for this server.")
                .WithTags("Stubs");

            group.MapGet("/web/osu-getfavourites.php", () => Results.Text("", "text/html", Encoding.UTF8))
                .WithGroupName("osuweb")
                .WithSummary("List favourited beatmaps — stub.")
                .WithDescription("Always returns an empty body. Favourites are out of scope for this server.")
                .WithTags("Stubs");

            group.MapGet("/web/osu-addfavourite.php", () => Results.Text("", "text/html", Encoding.UTF8))
                .WithGroupName("osuweb")
                .WithSummary("Add a favourited beatmap — stub.")
                .WithDescription("Always returns an empty body. Favourites are out of scope for this server.")
                .WithTags("Stubs");

            // "not ranked" is a real response code the Python source itself sends (BeatmapRatingResultCode.NOT_RANKED) — reused here instead of an ad-hoc string.
            group.MapGet("/web/osu-rate.php", () => Results.Text("not ranked", "text/html", Encoding.UTF8))
                .WithGroupName("osuweb")
                .WithSummary("Rate a beatmap — always reports \"not ranked\".")
                .WithDescription("Always returns the literal `not ranked` response osu! itself uses for maps " +
                    "that can't be rated. Beatmap rating is out of scope for this server (every map here is " +
                    "always treated as Loved, never Ranked).")
                .WithTags("Stubs");

            group.MapPost("/web/osu-comment.php", () => Results.Text("", "text/html", Encoding.UTF8))
                .WithGroupName("osuweb")
                .WithSummary("Post a replay comment — stub.")
                .WithDescription("Always returns an empty body. In-replay comments are out of scope for this " +
                    "server.")
                .WithTags("Stubs");

            // In-game registration: the client sends user[username], user[user_email], user[password],
            // plus a `check` field — "0" is the real submit, any other value is a live-validation POST
            // fired while the user is still filling the form (one per field, tabbing through). Only
            // "0" may create the account; other values run every validation below and report errors
            // the same way, but stop short of CreateAsync so filling in earlier fields doesn't already
            // register the account before the user reaches submit.
            // The Email field must contain the AdminKey configured in [Server] section. If AdminKey is
            // unset, registration is disabled entirely. Registered users get default privileges
            // (Unrestricted | Verified | Supporter).
            group.MapPost("/users", async (HttpContext context, IUserRepository users,
                IPasswordHasher passwordHasher, IOptions<ServerOptions> serverOptions,
                CancellationToken cancellationToken) =>
            {
                var username = context.Request.Form["user[username]"].FirstOrDefault();
                var email = context.Request.Form["user[user_email]"].FirstOrDefault();
                var password = context.Request.Form["user[password]"].FirstOrDefault();
                var check = context.Request.Form["check"].FirstOrDefault();

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                    return Results.Json(
                        new { form_error = new { user = new { email = new[] { "Username and password are required." } } } },
                        statusCode: StatusCodes.Status400BadRequest);

                if (!User.ValidateUsername(username, out var usernameError))
                    return Results.Json(
                        new { form_error = new { user = new { username = new[] { usernameError } } } },
                        statusCode: StatusCodes.Status400BadRequest);

                var adminKey = serverOptions.Value.AdminKey;

                if (string.IsNullOrEmpty(adminKey))
                    return Results.Json(
                        new { form_error = new { user = new { email = new[] { "In-game registration is disabled." } } } },
                        statusCode: StatusCodes.Status400BadRequest);

                if (string.IsNullOrEmpty(email) || email != adminKey)
                    return Results.Json(
                        new
                        {
                            form_error = new
                            {
                                user = new
                                {
                                    email = new[]
                                    {
                                        "Invalid AdminKey. Please enter the AdminKey in the Email field to continue."
                                    }
                                }
                            }
                        },
                        statusCode: StatusCodes.Status400BadRequest);

                if (await users.FetchByNameAsync(username, cancellationToken) is not null)
                    return Results.Json(
                        new { form_error = new { user = new { username = new[] { "Username already taken." } } } },
                        statusCode: StatusCodes.Status409Conflict);

                if (check != "0")
                    return Results.Text("");

                var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
                var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
                var user = await users.CreateAsync(username, pwBcrypt, "xx", cancellationToken: cancellationToken);
                if (user is null)
                    return Results.Json(
                        new { form_error = new { user = new { username = new[] { "Username already taken." } } } },
                        statusCode: StatusCodes.Status409Conflict);

                return Results.Json(new { id = user.Id, name = user.Name });
            })
                .WithGroupName("osuweb")
                .WithSummary("In-game account registration, gated behind the server's admin key.")
                .WithDescription("Form fields: `user[username]`, `user[user_email]`, `user[password]`, and " +
                    "`check` (`\"0\"` for the real submit; any other value is a live per-field validation POST " +
                    "the client fires while the registration form is still being filled in, which runs every " +
                    "validation below but stops short of creating the account). The `user_email` field must " +
                    "exactly match the server's configured `Server:AdminKey` — this repurposes the real " +
                    "client's email field as a simple gate, since this server has no email infrastructure. " +
                    "Registration is disabled entirely if `Server:AdminKey` is unset. New accounts get default " +
                    "privileges (Unrestricted | Verified | Supporter).")
                .WithTags("Registration");

            // Ported from app/api/domains/osu.py's difficultyRatingHandler — the Python source
            // unconditionally redirects to osu.ppy.sh's difficulty-rating webpage (opened in the
            // user's system browser, not parsed by the client itself); this server has no such
            // webpage, so it computes the star rating locally with IDifficultyCalculator
            // instead and caches the NoMod result onto Beatmaps.Sr. `b` (beatmap id) and `mods`
            // (bitmask) are read from the query string since the real client sends neither a body nor
            // documented form fields for this endpoint.
            group.MapPost("/difficulty-rating", async (
                [FromQuery(Name = "b")] int? beatmapId,
                HttpContext context,
                CancellationToken cancellationToken,
                [FromQuery(Name = "mods")] int mods = 0) =>
            {
                if (beatmapId is null)
                    return Results.Text("Difficulty rating requires a beatmap id (?b=).", "text/html", Encoding.UTF8);

                var maps = context.RequestServices.GetRequiredService<IMapRepository>();
                var bmap = await maps.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
                if (bmap is null) return Results.NotFound();

                var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
                var osuPath = BeatmapIngestionService.OsuFilePath(storage, bmap);
                if (!File.Exists(osuPath))
                    return Results.Text("Beatmap file not available locally.", "text/html", Encoding.UTF8);

                var requestedMods = (Mods)mods;
                double stars;
                if (requestedMods == Mods.NoMod && bmap.Difficulty.Sr > 0)
                {
                    stars = bmap.Difficulty.Sr;
                }
                else
                {
                    var calculator = context.RequestServices.GetRequiredService<IDifficultyCalculator>();
                    stars = calculator.CalculateStarRating(osuPath, bmap.Difficulty.Mode, requestedMods);
                    if (requestedMods == Mods.NoMod)
                        await maps.UpdateDiffAsync(bmap.Id, stars, cancellationToken);
                }

                return Results.Json(new { beatmap_id = bmap.Id, mods = (int)requestedMods, rating = stars });
            })
                .WithGroupName("osuweb")
                .WithSummary("Compute (and cache) a beatmap's star rating for a given mod combination.")
                .WithDescription("The real osu! client normally opens a difficulty-rating webpage on osu.ppy.sh " +
                    "in the system browser for this action; this server has no such page, so it computes the " +
                    "star rating locally instead via ppy's own osu!lazer ruleset libraries (display only — see " +
                    "the project's no-pp-dependency policy). `b` (required) is the beatmap id, `mods` (default " +
                    "0, i.e. no mods) a mod bitmask. The unmodified (NoMod) result is cached onto the beatmap's " +
                    "`Sr` column; other mod combinations are computed fresh each call. Response is the one JSON " +
                    "endpoint in this whole host group: `{beatmap_id, mods, rating}` (snake_case, matching " +
                    "osu!'s own naming here rather than this server's usual camelCase).")
                .WithTags("Beatmaps");
        }
    }

    private static readonly string[] VideoExtensions = [".mp4", ".avi", ".flv", ".wmv", ".mkv", ".mov"];

    /// <summary>
    ///     Packages a locally-ingested mapset's whole storage folder (audio/images/video/.osu, as
    ///     extracted) into a fresh .osz on the fly (shared by /d/{setId} and the api. host's
    ///     /beatmapsets/{id}/download, see BeatmapsetRoutes.cs). <paramref name="noVideo" /> skips any
    ///     video file and appends " [no video]" to the returned filename. Returns null when the set
    ///     has no local folder or the folder is empty.
    /// </summary>
    internal static async Task<(byte[] Bytes, string FileName)?> BuildOszArchiveAsync(IMapRepository maps,
        StorageOptions storage, int setId, bool noVideo, CancellationToken cancellationToken)
    {
        var beatmaps = await maps.FetchAllBySetIdAsync(setId, cancellationToken: cancellationToken);
        if (beatmaps.Count == 0) return null;

        var mapset = beatmaps[0].Mapset;
        var folder = BeatmapIngestionService.FindMapsetFolder(storage, setId);
        if (folder is null) return null;

        using var zipStream = new MemoryStream();
        var wroteAny = false;
        var suffixed = false;
        await using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            foreach (var filePath in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var isVideo = VideoExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);
                if (noVideo && isVideo)
                {
                    suffixed = true;
                    continue;
                }

                var entry = archive.CreateEntry(Path.GetRelativePath(folder, filePath));
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(filePath);
                await fileStream.CopyToAsync(entryStream, cancellationToken);
                wroteAny = true;
            }
        }

        if (!wroteAny) return null;

        var name = $"{mapset.Id} {mapset.Artist} - {mapset.Title}{(suffixed ? " [no video]" : "")}.osz";
        return (zipStream.ToArray(), name);
    }

    extension(RouteGroupBuilder group)
    {
        private void MapBeatmapAssetGroup()
        {
            // Ported from app/api/domains/map.py's everything — every request under the b.{domain}
            // host forwards straight through to osu!'s real static-asset CDN (thumbnails, previews).
            group.MapGet("/{**path}", (HttpContext context) =>
                Results.Redirect($"https://b.ppy.sh{context.Request.Path}", true))
                .WithGroupName("beatmapassets")
                .WithSummary("Beatmap thumbnail/preview asset — 301 redirect to osu.ppy.sh's real CDN.")
                .WithDescription("Every request under this host, regardless of path, is redirected as-is to the " +
                    "matching path on `https://b.ppy.sh`. This server has no local mirror of osu!'s thumbnail/" +
                    "preview asset library — this exists purely so the client's asset requests (e.g. `/thumb/" +
                    "{setId}l.jpg`) resolve to real content instead of failing.")
                .WithTags("Beatmap Assets");
        }

        private void MapAvatarGroup()
        {
            group.MapGet("/{userId:int}", (int userId, HttpContext context) =>
            {
                var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
                Directory.CreateDirectory(storage.AvatarsPath);

                var match = Directory.EnumerateFiles(storage.AvatarsPath, $"{userId}.*").FirstOrDefault();
                if (match is not null)
                {
                    var contentType = Path.GetExtension(match).ToLowerInvariant() switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        _ => "application/octet-stream"
                    };
                    return Results.File(match, contentType);
                }

                // BasilBot (ID 0) fallback: extract embedded assets/avatars/basil.png to Avatars/0.png.
                if (userId == 0)
                {
                    var botPath = Path.Combine(storage.AvatarsPath, "0.png");
                    if (!File.Exists(botPath))
                        TryWriteEmbeddedResource("Basil.Web.Resources.icon.png", botPath);
                    if (File.Exists(botPath))
                        return Results.File(botPath, "image/png");
                }

                // Regular user fallback: extract embedded assets/avatars/default.png to Avatars/default.png.
                var defaultPath = Path.Combine(storage.AvatarsPath, "default.png");
                if (!File.Exists(defaultPath))
                    TryWriteEmbeddedResource("Basil.Web.Resources.default.png", defaultPath);
                if (File.Exists(defaultPath))
                    return Results.File(defaultPath, "image/png");

                return Results.NotFound();
            })
                .WithGroupName("avatar")
                .WithSummary("Serve one player's avatar image, by user id.")
                .WithDescription("`{userId}` is the numeric `Users.Id`. Serves a locally-uploaded avatar file if " +
                    "one exists (`{userId}.{ext}` under the avatars storage folder); otherwise falls back to a " +
                    "built-in image — BasilBot's own icon for user id 0, or a generic default avatar for every " +
                    "other id — materializing that fallback to disk on first request. Content-Type is inferred " +
                    "from the file extension. This server stores avatars locally rather than proxying " +
                    "osu.ppy.sh's CDN (unlike bancho.py).")
                .WithTags("Avatars");
        }
    }

    // bancho.py has no local avatar storage (the a.{domain} host there always forwards to a real CDN).
    // Files are stored flat as "{userId}.{ext}" under StorageOptions.AvatarsPath.

    // Writes to a per-call temp file then renames into place, so concurrent first-avatar-request
    // races (fresh deploy, several clients fetching the same not-yet-materialized fallback at once)
    // each finish with a complete file instead of racing File.Create on the same destination path.
    private static void TryWriteEmbeddedResource(string resourceName, string destinationPath)
    {
        using var stream = typeof(BanchoHostGroups).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return;

        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        using (var fileStream = File.Create(tempPath))
            stream.CopyTo(fileStream);
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    // api. host: TRT snapshot (GET+SSE), file downloads, SSE live channels, admin-key-gated management
    // CRUD, a trivial /health probe, and (below) the generated OpenAPI/Scalar documentation site —
    // same content whether this server is self-hosted or run locally; the GitHub Pages copy is a
    // separate, fully static build of the same 3 pages (see docs-site/ and the CI workflow) with
    // Try-it-out disabled, since it has no live backend behind it.
    private static void MapApiGroup(this RouteGroupBuilder group)
    {
        group.MapOpenApi().ExcludeFromDescription();

        group.MapGet("/", () => Results.Redirect("/docs/"))
            .WithGroupName("basilapi")
            .WithName("redirectToDocs")
            .WithSummary("Redirect To Docs")
            .WithDescription("302 redirect to `/docs/`.")
            .WithTags("Health")
            .Produces(StatusCodes.Status302Found);

        group.MapGet("/health", () => Results.Json(new HealthStatus("ok")))
            .WithGroupName("basilapi")
            .WithName("getHealth")
            .WithSummary("Get Health")
            .WithDescription("Trivial `{ status: \"ok\" }` — no dependency checks (the database is an " +
                "embedded SQLite file, always available once the process is up). Public, no authentication.")
            .WithTags("Health")
            .Produces<HealthStatus>()
            .WithExample(StatusCodes.Status200OK, new HealthStatus("ok"));

        var docsSiteRoot = Path.Combine(AppContext.BaseDirectory, "docs-site");
        var docs = group.MapGroup("/docs");

        docs.MapGet("/", () => Results.File(Path.Combine(docsSiteRoot, "index.html"), "text/html"))
            .ExcludeFromDescription();

        docs.MapGet("/basilbot/",
                () => Results.File(Path.Combine(docsSiteRoot, "basilbot", "index.html"), "text/html"))
            .ExcludeFromDescription();

        docs.MapScalarApiReference("/osu-client", options =>
        {
            options.Title = "osu! Client API";
            options.AddDocument("bancho", "Bancho Protocol");
            options.AddDocument("osuweb", "osu! Web");
            options.AddDocument("beatmapassets", "Beatmap Assets");
            options.AddDocument("avatar", "Avatar Files");
        }).ExcludeFromDescription();

        docs.MapScalarApiReference("/basil-api", options =>
        {
            options.Title = "Basil API";
            options.AddDocument("basilapi", "Basil API");
        }).ExcludeFromDescription();

        // GET (JSON snapshot) and SSE (live "main" channel — meta/map/state, no per-player data,
        // see MatchLiveSnapshotBuilder's doc comment) share this one path, branched on the client's
        // Accept header (EventSource always sends "text/event-stream"). A match that has actually
        // closed (persisted with EndedAt set) has nothing left to push, so an SSE request against
        // one falls back to the one-shot JSON report instead of opening a stream that would never
        // receive a frame — a match that's still live, or one that has simply never existed at all,
        // still gets a stream (a nonexistent id just never receives any frames).
        group.MapGet("/matches/{matchId:int}", async (int matchId, HttpContext context, MatchReportService reportService,
            IMatchRegistry matchRegistry, IMatchPersistenceRepository matchPersistence,
            IMatchLiveEvents events, CancellationToken cancellationToken) =>
        {
            if (context.Request.Headers.Accept.Any(a => a?.Contains("text/event-stream") == true))
            {
                var match = matchRegistry.GetByDbId(matchId);
                var isClosed = match is null &&
                    (await matchPersistence.FetchMatchAsync(matchId, cancellationToken))?.EndedAt is not null;

                if (!isClosed)
                    return LiveSseRoutes.HandleMain(context, matchId, events,
                        () => match?.MainSnapshot.Latest is { } snapshot
                            ? JsonSerializer.SerializeToUtf8Bytes(snapshot)
                            : null,
                        cancellationToken);
            }

            var report = await reportService.BuildAsync(matchId, cancellationToken);
            return report is null ? Results.NotFound() : Results.Json(report);
        })
            .WithGroupName("basilapi")
            .WithName("getMatchReport")
            .WithSummary("Get Match Report")
            .WithDescription("Content-negotiated on the `Accept` header: a plain `GET` (or any `Accept` not " +
                "containing `text/event-stream`) returns a full JSON snapshot built at read time — events, " +
                "rounds, per-round scores, and, if the match is still open, its live state (host, referees, " +
                "slots, current map, win condition, team type, mods, in-progress flag). Sending " +
                "`Accept: text/event-stream` instead opens a persistent Server-Sent Events stream on the same " +
                "path (event name `main`) — the first event is the full current state, every event after that " +
                "is an RFC 7396 JSON Merge Patch against the previous one — no per-player score data on this " +
                "channel, see `GET /matches/{matchId}/live/{slotIndex}` for that. 404 (one-shot mode only) if " +
                "no match with this id has ever existed. A closed match always falls back to the one-shot JSON " +
                "report, even when `Accept: text/event-stream` is sent — there's nothing left to push. Public, " +
                "no authentication.")
            .WithTags("Match Report")
            .Produces<MatchReport>()
            .WithExample(StatusCodes.Status200OK, SampleMatchReport())
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapMatchRoutes();

        group.MapUserRoutes();

        group.MapScoreRoutes();

        group.MapBeatmapsetRoutes();

        group.MapFaqRoutes();

        group.MapSeasonalRoutes();

        group.MapAbbreviationRedirects();
    }

    private sealed record HealthStatus(string Status);

    private static MatchReport SampleMatchReport()
    {
        var started = DateTime.Parse("2026-07-20T12:00:00Z");
        var ended = DateTime.Parse("2026-07-20T12:04:30Z");
        var live = new MatchReportLiveInfo(
            new UserBrief(7, "Alice"), [new UserBrief(8, "Bob")],
            new Dictionary<int, MatchLiveSlot>
            {
                [0] = new MatchLiveSlot(7, "Alice", "VN", "NotReady", "Red", 0),
                [1] = new MatchLiveSlot(9, "Carol", "US", "NotReady", "Blue", 0)
            },
            654, "d41d8cd98f00b204e9800998ecf8427e", GameMode.Standard, MatchWinCondition.ScoreV2,
            MatchTeamType.TeamVs, 0, false, false);

        var score = new MatchReportScore(7, "Alice", "Red", 0, 4_850_213, 98.42, 1234, 720, 45, 3, 2, 12, 5,
            "A", false, ended);
        var round = new MatchReportRound(0, 654, "d41d8cd98f00b204e9800998ecf8427e", "Camellia",
            "Exit This Earth's Atmosphere", "Extreme", "RLC", 0, 3, 2, 0, false, started, ended,
            7, "Alice", "Red", "score", 1_200_000, [score]);
        var evt = new MatchReportEvent(0, "Created", 7, "Alice", null, null, started, null);

        return new MatchReport(42, "Grand Finals: Alpha vs Bravo", started, null, live, [evt], [round]);
    }
}
