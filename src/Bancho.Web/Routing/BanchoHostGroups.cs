using System.Text;
using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Anticheat;
using Bancho.Application.UseCases.Authentication;
using Bancho.Application.UseCases.Beatmaps;
using Bancho.Application.UseCases.Mail;
using Bancho.Application.UseCases.Scores;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Bancho.Application.Abstractions.Beatmaps;
using Bancho.Application.Abstractions.Scores;
using Bancho.Application.Abstractions.Social;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Login;
using Bancho.Domain.Scores;
using Bancho.Protocol.Packets;

namespace Bancho.Web.Routing;

/// <summary>
/// Ports bancho.py's app/api/init_api.py:init_routes — routes are selected by hostname, not
/// path prefix. For every domain in ("ppy.sh", configured DOMAIN): c./ce./c4./c5./c6.{domain}
/// serve the bancho realtime protocol, osu.{domain} serves the osu! web endpoints, b.{domain}
/// serves beatmap assets, and api.{domain} serves the developer-facing API.
/// </summary>
public static class BanchoHostGroups
{
    private static readonly string[] BanchoSubdomains = ["c", "ce", "c4", "c5", "c6"];

    public static void MapAll(WebApplication app, string configuredDomain)
    {
        var domains = new[] { "ppy.sh", configuredDomain }.Distinct().ToArray();

        var banchoHosts = domains
            .SelectMany(domain => BanchoSubdomains.Select(subdomain => $"{subdomain}.{domain}"))
            .ToArray();
        var osuWebHosts = domains.Select(domain => $"osu.{domain}").ToArray();
        var beatmapAssetHosts = domains.Select(domain => $"b.{domain}").ToArray();
        var apiHosts = domains.Select(domain => $"api.{domain}").ToArray();

        app.MapGroup("/").RequireHost(banchoHosts).MapBanchoGroup();
        app.MapGroup("/").RequireHost(osuWebHosts).MapOsuWebGroup();
        app.MapGroup("/").RequireHost(beatmapAssetHosts).MapBeatmapAssetGroup();
        app.MapGroup("/").RequireHost(apiHosts).MapApiGroup();
    }

    // NOTE: placeholder handlers still cover the other host groups — real endpoints land in
    // Phase 5/8 (osu web), Phase 5 (map redirect), Phase 9 (api v1/v2).
    private static void MapBanchoGroup(this RouteGroupBuilder group)
    {
        group.MapGet("/", () => "cho");

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
                // in front (e.g. local dev), synthesize it from the direct TCP peer so
                // ClientIpResolver — which intentionally mirrors bancho.py's proxy-only assumption
                // — still has something to read.
                if (!headers.ContainsKey("CF-Connecting-IP") && !headers.ContainsKey("X-Forwarded-For"))
                {
                    // ClientIpResolver falls back to X-Real-IP whenever X-Forwarded-For has a
                    // single entry (matching bancho.py's own assumption about nginx's config) —
                    // both need synthesizing together, not just one.
                    var remoteIp = (context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback).ToString();
                    headers["X-Forwarded-For"] = remoteIp;
                    headers["X-Real-IP"] = remoteIp;
                }
                var ip = ClientIpResolver.Resolve(headers);
                var loginUseCase = context.RequestServices.GetRequiredService<OsuLoginUseCase>();
                var loginResult = await loginUseCase.ExecuteAsync(new OsuLoginRequest(body, headers, ip), cancellationToken);
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
                    var clock = context.RequestServices.GetRequiredService<IClock>();
                    await dispatcher.DispatchAsync(session, body);
                    session.LastRecvTime = clock.UtcNow.ToUnixTimeSeconds();
                    responseBody = session.Dequeue();
                }
            }

            response.ContentType = "text/html; charset=UTF-8";
            await response.Body.WriteAsync(responseBody, cancellationToken);
        });
    }

    private static void MapOsuWebGroup(this RouteGroupBuilder group)
    {
        group.MapGet("/", () => "osu");

        // Ported from app/api/domains/osu.py's getScores. "s" (requesting_from_editor_song_select)
        // is bound as int (client sends a plain 0/1 flag, not "true"/"false") to avoid ASP.NET
        // Core's stricter bool query-string parsing. map_set_id/aqn_files_found are intentionally
        // not bound — see BeatmapLeaderboardRequest's doc comment for why they're out of scope.
        group.MapGet("/web/osu-osz2-getscores.php", async (
            [FromQuery(Name = "us")] string username,
            [FromQuery(Name = "ha")] string ha,
            [FromQuery(Name = "s")] int s,
            [FromQuery(Name = "v")] int v,
            [FromQuery(Name = "c")] string c,
            [FromQuery(Name = "f")] string f,
            [FromQuery(Name = "m")] int m,
            [FromQuery(Name = "mods")] int mods,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            var player = await authentication.AuthenticateOnlinePlayerAsync(username, ha, cancellationToken);
            if (player is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var leaderboardService = context.RequestServices.GetRequiredService<BeatmapLeaderboardService>();
            var request = new BeatmapLeaderboardRequest(s != 0, (LeaderboardType)v, c, f, m, mods);
            var result = await leaderboardService.FetchLeaderboardAsync(player, request, cancellationToken);

            var body = result.Code switch
            {
                BeatmapLeaderboardResultCode.NotSubmitted => GetScoresResponseFormatter.NotSubmitted,
                BeatmapLeaderboardResultCode.NeedsUpdate => GetScoresResponseFormatter.NeedsUpdate,
                BeatmapLeaderboardResultCode.NoLeaderboard => GetScoresResponseFormatter.NoLeaderboard(result.RankedStatus!.Value),
                _ => GetScoresResponseFormatter.Found(result),
            };

            return Results.Text(body, "text/html", Encoding.UTF8);
        });

        // Ported from app/api/domains/osu.py's osuSearchHandler, replumbed to query the local
        // maps table instead of proxying a mirror API — runs fully offline now.
        group.MapGet("/web/osu-search.php", async (
            [FromQuery(Name = "u")] string username,
            [FromQuery(Name = "h")] string passwordMd5,
            [FromQuery(Name = "r")] int rankedStatus,
            [FromQuery(Name = "q")] string query,
            [FromQuery(Name = "m")] int mode,
            [FromQuery(Name = "p")] int pageNum,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var searchService = context.RequestServices.GetRequiredService<DirectSearchService>();
            var request = new DirectSearchRequest(query, mode, rankedStatus, pageNum);
            var beatmapSets = await searchService.SearchAsync(request, cancellationToken);

            return Results.Text(DirectSearchResponseFormatter.Format(beatmapSets), "text/html", Encoding.UTF8);
        });

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
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            if (mapSetId is null && mapId is null && checksum is null)
            {
                return Results.Text("", "text/html", Encoding.UTF8);
            }

            var maps = context.RequestServices.GetRequiredService<IMapRepository>();
            var bmapSet = await maps.FetchOneAsync(id: mapId, md5: checksum, setId: mapSetId, cancellationToken: cancellationToken);

            return Results.Text(DirectSearchResponseFormatter.FormatSet(bmapSet), "text/html", Encoding.UTF8);
        });

        // Ported from app/api/domains/osu.py's get_osz. bancho.py always redirects to
        // MIRROR_DOWNLOAD_ENDPOINT (a real internet host); this server has no local .osz storage
        // and no internet default, so an unconfigured DownloadEndpoint reports the download as
        // unavailable instead of reaching out anywhere.
        group.MapGet("/d/{mapSetId}", (string mapSetId, HttpContext context) =>
        {
            var mirrorOptions = context.RequestServices.GetRequiredService<IOptions<MirrorOptions>>().Value;
            if (string.IsNullOrEmpty(mirrorOptions.DownloadEndpoint))
            {
                return Results.Text("Beatmap downloads are not available on this server.", "text/html", Encoding.UTF8);
            }

            var noVideo = mapSetId.EndsWith('n');
            var setId = noVideo ? mapSetId[..^1] : mapSetId;
            var query = $"{setId}?n={(noVideo ? 0 : 1)}";

            return Results.Redirect($"{mirrorOptions.DownloadEndpoint}/{query}", permanent: true);
        });

        // Ported from app/api/domains/osu.py's get_updated_beatmap. bancho.py redirects to the
        // real osu.ppy.sh in the non-devserver-host case; this server has no local .osu file
        // storage and no internet default, so it always reports unavailability instead — even the
        // real-osu.ppy.sh redirect fallback is dropped, not just the devserver-host special case.
        group.MapGet("/web/maps/{mapFilename}", () =>
            Results.Text("Beatmap file updates are not available on this server.", "text/html", Encoding.UTF8));

        // Ported from app/api/domains/osu.py's osuSubmitModularSelector. The "score" field name is
        // reused by the client for both the base64 score-data string and the replay file upload —
        // ASP.NET Core's multipart parser already separates text parts from file parts by name, so
        // (unlike Starlette/FastAPI) no manual form-field workaround is needed here. Unused fields
        // the Python source reads but never forwards into submission (fs/x/i) are not bound at all.
        group.MapPost("/web/osu-submit-modular-selector.php", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!context.Request.HasFormContentType)
            {
                return Results.Text("", "text/html", Encoding.UTF8);
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var scoreDataB64 = form["score"].FirstOrDefault();
            if (scoreDataB64 is null)
            {
                return Results.Text("", "text/html", Encoding.UTF8);
            }

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

            var useCase = context.RequestServices.GetRequiredService<ScoreSubmissionUseCase>();
            var outcome = await useCase.SubmitAsync(
                new ScoreSubmissionRequest(
                    ScoreDataFields: scoreDataFields,
                    PasswordMd5: form["pass"].FirstOrDefault() ?? "",
                    OsuVersion: osuVersion,
                    ClientHash: clientHash,
                    UniqueIds: form["c1"].FirstOrDefault() ?? "",
                    StoryboardMd5: form["sbk"].FirstOrDefault(),
                    UpdatedBeatmapHash: form["bmk"].FirstOrDefault() ?? "",
                    ScoreTime: int.Parse(form["st"].FirstOrDefault() ?? "0"),
                    FailTime: int.Parse(form["ft"].FirstOrDefault() ?? "0"),
                    ReplayData: replayData),
                cancellationToken);

            var domain = context.RequestServices.GetRequiredService<IOptions<ServerBehaviorOptions>>().Value.Domain;
            var body = outcome.Code == ScoreSubmissionResultCode.Success
                ? ScoreSubmissionResponseBuilder.BuildSuccess(outcome.Result!, domain)
                : ScoreSubmissionResponseBuilder.BuildError(outcome.Code);

            return Results.Text(body, "text/html", Encoding.UTF8);
        });

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
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
            if (player is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var replayService = context.RequestServices.GetRequiredService<ReplayService>();
            var result = await replayService.FetchReplayFileAsync(scoreId, player.Id, cancellationToken);

            return result.Code == ReplayFetchResultCode.NotFound
                ? Results.NotFound()
                : Results.Bytes(result.Data!, "application/octet-stream");
        });

        // Ported from app/api/domains/osu.py's osuGetBeatmapInfo. Body is JSON {"Filenames": [...],
        // "Ids": [...]}; the "Ids" path is dead in the Python source too ("still have yet to see
        // this used" — only logged, no lookup), so it's read but never resolved here either.
        group.MapPost("/web/osu-getbeatmapinfo.php", async (
            [FromQuery(Name = "u")] string username,
            [FromQuery(Name = "h")] string passwordMd5,
            OsuBeatmapInfoRequest body,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
            if (player is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var beatmapInfoService = context.RequestServices.GetRequiredService<BeatmapInfoService>();
            var vanillaMode = (GameMode)player.Status.Mode.AsVanilla();
            var rows = await beatmapInfoService.FetchBeatmapInfoAsync(body.Filenames, player.Id, vanillaMode, cancellationToken);

            var lines = rows.Select(row => $"{row.Index}|{row.Id}|{row.SetId}|{row.Md5}|{row.Status}|{string.Join('|', row.Grades)}");
            return Results.Text(string.Join('\n', lines), "text/html", Encoding.UTF8);
        });

        // Ported from app/api/domains/osu.py's lastFM. Per explicit user decision, detected
        // cheat-tool flags are only logged (ClientIntegrityService) — no restrict/kick, since that
        // machinery doesn't exist yet (Phase 10, deferred with the chat command system).
        group.MapGet("/web/lastfm.php", async (
            [FromQuery(Name = "b")] string beatmapIdOrHiddenFlag,
            [FromQuery(Name = "us")] string username,
            [FromQuery(Name = "ha")] string passwordMd5,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
            if (player is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var clientIntegrity = context.RequestServices.GetRequiredService<ClientIntegrityService>();
            var result = await clientIntegrity.HandleLastFmFlagsAsync(player, beatmapIdOrHiddenFlag, cancellationToken);

            return result == ClientIntegrityResult.StopSending
                ? Results.Text("-3", "text/html", Encoding.UTF8)
                : Results.Text("", "text/html", Encoding.UTF8);
        });

        // Ported from app/api/domains/osu.py's osuMarkAsRead.
        group.MapGet("/web/osu-markasread.php", async (
            [FromQuery(Name = "u")] string username,
            [FromQuery(Name = "h")] string passwordMd5,
            [FromQuery(Name = "channel")] string channel,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
            if (player is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var mailReadService = context.RequestServices.GetRequiredService<MailReadService>();
            await mailReadService.MarkChannelAsReadAsync(player, channel, cancellationToken);

            return Results.Text("", "text/html", Encoding.UTF8);
        });

        // Ported from app/api/domains/osu.py's osuSeasonal. No seasonal background feature exists
        // on this server, so this always reports an empty list rather than the settings-configured
        // SEASONAL_BGS the Python source reads.
        group.MapGet("/web/osu-getseasonal.php", () => Results.Json(Array.Empty<string>()));

        // Ported from app/api/domains/osu.py's banchoConnect — unauthenticated by design in the
        // Python source too (can be called before a session exists).
        group.MapGet("/web/bancho_connect.php", () => Results.Text("", "text/html", Encoding.UTF8));

        // Ported from app/api/domains/osu.py's checkUpdates (always an empty stub response there too).
        group.MapGet("/web/check-updates.php", () => Results.Text("", "text/html", Encoding.UTF8));

        // Screenshots, favourites, ratings, comments, and in-game registration are dropped per the
        // multiplayer/tourney-only scope decision — these routes exist only so the client doesn't
        // treat a 404 as a connectivity failure, matching the "not available on this server"
        // messaging style already used by the map-download/beatmap-file-update stubs above.
        group.MapPost("/web/osu-screenshot.php", () =>
            Results.Text("Screenshots are not available on this server.", "text/html", Encoding.UTF8, StatusCodes.Status400BadRequest));

        group.MapGet("/web/osu-getfavourites.php", () => Results.Text("", "text/html", Encoding.UTF8));

        group.MapGet("/web/osu-addfavourite.php", () => Results.Text("", "text/html", Encoding.UTF8));

        // "not ranked" is a real response code the Python source itself sends (BeatmapRatingResultCode.NOT_RANKED) — reused here instead of an ad-hoc string.
        group.MapGet("/web/osu-rate.php", () => Results.Text("not ranked", "text/html", Encoding.UTF8));

        group.MapPost("/web/osu-comment.php", () => Results.Text("", "text/html", Encoding.UTF8));

        // Reuses the Python source's own "in-game registration disabled" response shape (a real,
        // client-understood code path — INGAME_REGISTRATION_DISALLOWED_ERROR) rather than an
        // ad-hoc stub, since registration through the client is genuinely unsupported here.
        group.MapPost("/users", () => Results.Json(
            new
            {
                form_error = new
                {
                    user = new
                    {
                        password = new[] { "In-game registration is disabled. Please register on the website." },
                    },
                },
            },
            statusCode: StatusCodes.Status400BadRequest));

        // Ported from app/api/domains/osu.py's difficultyRatingHandler — unconditional redirect in
        // the Python source too, not a divergence introduced here.
        group.MapPost("/difficulty-rating", (HttpContext context) =>
            Results.Redirect($"https://osu.ppy.sh{context.Request.Path}", permanent: false, preserveMethod: true));
    }

    private static void MapBeatmapAssetGroup(this RouteGroupBuilder group) =>
        // Ported from app/api/domains/map.py's everything — every request under the b.{domain}
        // host forwards straight through to osu!'s real static-asset CDN (thumbnails, previews).
        group.MapGet("/{**path}", (HttpContext context) =>
            Results.Redirect($"https://b.ppy.sh{context.Request.Path}", permanent: true));

    private static void MapApiGroup(this RouteGroupBuilder group) =>
        group.MapGet("/", () => "api");
}

/// <summary>Ported from app/objects/models.py's OsuBeatmapRequestForm — osu-getbeatmapinfo.php's JSON request body.</summary>
public sealed record OsuBeatmapInfoRequest(List<string> Filenames, List<int> Ids);
