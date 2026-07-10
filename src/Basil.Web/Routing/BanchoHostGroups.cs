using System.IO.Compression;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Anticheat;
using Basil.Application.UseCases.Authentication;
using Basil.Application.UseCases.Beatmaps;
using Basil.Application.UseCases.Mail;
using Basil.Application.UseCases.Multiplayer;
using Basil.Application.UseCases.Scores;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
                    var remoteIp = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
                    headers["X-Forwarded-For"] = remoteIp;
                    headers["X-Real-IP"] = remoteIp;
                }

                var ip = ClientIpResolver.Resolve(headers);
                var loginUseCase = context.RequestServices.GetRequiredService<OsuLoginUseCase>();
                var loginResult =
                    await loginUseCase.ExecuteAsync(new OsuLoginRequest(body, headers, ip), cancellationToken);
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

        // Serves ServerOptions.MenuIconPath as an image — the in-game main menu icon is
        // configured as a local file path (not a URL) so it doesn't depend on external hosting;
        // OsuLoginUseCase points the client at this endpoint instead of the file path itself.
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
        });

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
            if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

            if (!Enum.IsDefined(typeof(LeaderboardType), v)) return Results.StatusCode(StatusCodes.Status400BadRequest);

            var leaderboardService = context.RequestServices.GetRequiredService<BeatmapLeaderboardService>();
            var request = new BeatmapLeaderboardRequest(s != 0, (LeaderboardType)v, c, f, m, mods);
            var result = await leaderboardService.FetchLeaderboardAsync(player, request, cancellationToken);

            var body = result.Code switch
            {
                BeatmapLeaderboardResultCode.NotSubmitted => GetScoresResponseFormatter.NotSubmitted,
                BeatmapLeaderboardResultCode.NeedsUpdate => GetScoresResponseFormatter.NeedsUpdate,
                BeatmapLeaderboardResultCode.NoLeaderboard => GetScoresResponseFormatter.NoLeaderboard(
                    result.RankedStatus!.Value),
                _ => GetScoresResponseFormatter.Found(result)
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
                return Results.StatusCode(StatusCodes.Status401Unauthorized);

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
                return Results.StatusCode(StatusCodes.Status401Unauthorized);

            if (mapSetId is null && mapId is null && checksum is null)
                return Results.Text("", "text/html", Encoding.UTF8);

            var maps = context.RequestServices.GetRequiredService<IMapRepository>();
            var bmapSet =
                await maps.FetchOneAsync(mapId, checksum, setId: mapSetId, cancellationToken: cancellationToken);

            return Results.Text(DirectSearchResponseFormatter.FormatSet(bmapSet), "text/html", Encoding.UTF8);
        });

        // Ported from app/api/domains/osu.py's get_osz, extended for this server's fully-offline
        // scope: if any beatmap in the set was locally ingested (BeatmapIngestionService), a fresh
        // .osz is packaged on the fly from the stored "{id}.osu" files instead of reaching out
        // anywhere. Only falls back to a configured mirror (kept from the original port) when the
        // set has nothing local — this server never stores audio/background assets, so a
        // locally-packaged .osz only ever contains .osu difficulty files, not a full mapset.
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
                    var osz = await BuildOszArchiveAsync(maps, storage, setId, cancellationToken);
                    if (osz is not null)
                        return Results.File(osz, "application/x-osu-beatmap-archive", $"{setId}.osz");
                }

                var mirrorOptions = context.RequestServices.GetRequiredService<IOptions<MirrorOptions>>().Value;
                if (string.IsNullOrEmpty(mirrorOptions.DownloadEndpoint))
                    return Results.Text("Beatmap downloads are not available on this server.", "text/html",
                        Encoding.UTF8);

                const int noVideoQueryValue = 0;
                const int withVideoQueryValue = 1;
                var query = $"{rawSetId}?n={(noVideo ? noVideoQueryValue : withVideoQueryValue)}";

                return Results.Redirect($"{mirrorOptions.DownloadEndpoint}/{query}", true);
            });

        // Ported from app/api/domains/osu.py's get_updated_beatmap, replumbed to serve the locally
        // ingested file instead of redirecting to osu.ppy.sh (this server runs fully offline).
        group.MapGet("/web/maps/{mapFilename}", async (string mapFilename, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var maps = context.RequestServices.GetRequiredService<IMapRepository>();
            var bmap = await maps.FetchOneAsync(filename: mapFilename, cancellationToken: cancellationToken);
            if (bmap is null) return Results.NotFound();

            var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
            var osuPath = Path.Combine(storage.MapsetsPath, $"{bmap.Id}.osu");
            return File.Exists(osuPath) ? Results.File(osuPath, "text/plain") : Results.NotFound();
        });

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

                var useCase = context.RequestServices.GetRequiredService<ScoreSubmissionUseCase>();
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
            if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

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
            if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

            var beatmapInfoService = context.RequestServices.GetRequiredService<BeatmapInfoService>();
            var vanillaMode = (GameMode)player.Status.Mode.AsVanilla();
            var rows = await beatmapInfoService.FetchBeatmapInfoAsync(body.Filenames, player.Id, vanillaMode,
                cancellationToken);

            var lines = rows.Select(row =>
                $"{row.Index}|{row.Id}|{row.SetId}|{row.Md5}|{row.Status}|{string.Join('|', row.Grades)}");
            return Results.Text(string.Join('\n', lines), "text/html", Encoding.UTF8);
        });

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
            var authentication = context.RequestServices.GetRequiredService<BanchoAuthenticationService>();
            var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
            if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

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
            if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

            var mailReadService = context.RequestServices.GetRequiredService<MailReadService>();
            await mailReadService.MarkChannelAsReadAsync(player, channel, cancellationToken);

            return Results.Text("", "text/html", Encoding.UTF8);
        });

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
        });

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
        });

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
            Results.Text("Screenshots are not available on this server.", "text/html", Encoding.UTF8,
                StatusCodes.Status400BadRequest));

        group.MapGet("/web/osu-getfavourites.php", () => Results.Text("", "text/html", Encoding.UTF8));

        group.MapGet("/web/osu-addfavourite.php", () => Results.Text("", "text/html", Encoding.UTF8));

        // "not ranked" is a real response code the Python source itself sends (BeatmapRatingResultCode.NOT_RANKED) — reused here instead of an ad-hoc string.
        group.MapGet("/web/osu-rate.php", () => Results.Text("not ranked", "text/html", Encoding.UTF8));

        group.MapPost("/web/osu-comment.php", () => Results.Text("", "text/html", Encoding.UTF8));

        // In-game registration: the client sends user[username], user[user_email], user[password].
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

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return Results.Json(
                    new { form_error = new { user = new { email = new[] { "Username and password are required." } } } },
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

            var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
            var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
            var user = await users.CreateAsync(username, pwBcrypt, "xx", cancellationToken: cancellationToken);

            return Results.Json(new { id = user.Id, name = user.Name });
        });

        // Ported from app/api/domains/osu.py's difficultyRatingHandler — the Python source
        // unconditionally redirects to osu.ppy.sh's difficulty-rating webpage (opened in the
        // user's system browser, not parsed by the client itself); this server has no such
        // webpage, so it computes the star rating locally with IBeatmapDifficultyCalculator
        // instead and caches the NoMod result onto Beatmaps.Diff. `b` (beatmap id) and `mods`
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
            var osuPath = Path.Combine(storage.MapsetsPath, $"{bmap.Id}.osu");
            if (!File.Exists(osuPath))
                return Results.Text("Beatmap file not available locally.", "text/html", Encoding.UTF8);

            var requestedMods = (Mods)mods;
            double stars;
            if (requestedMods == Mods.NoMod && bmap.Diff > 0)
            {
                stars = bmap.Diff;
            }
            else
            {
                var calculator = context.RequestServices.GetRequiredService<IBeatmapDifficultyCalculator>();
                stars = calculator.CalculateStarRating(osuPath, bmap.Mode, requestedMods);
                if (requestedMods == Mods.NoMod)
                    await maps.UpdateDiffAsync(bmap.Id, stars, cancellationToken);
            }

            return Results.Json(new { beatmap_id = bmap.Id, mods = (int)requestedMods, rating = stars });
        });
    }

    /// <summary>
    ///     Packages every locally-ingested beatmap sharing <paramref name="setId" /> into a fresh
    ///     .osz on the fly (shared by /d/{setId} and the api. host's /beatmapsets/{setId}). Returns
    ///     null when no beatmap in the set has a local .osu file.
    /// </summary>
    private static async Task<byte[]?> BuildOszArchiveAsync(IMapRepository maps, StorageOptions storage, int setId,
        CancellationToken cancellationToken)
    {
        var beatmaps = await maps.FetchAllBySetIdAsync(setId, cancellationToken);
        if (beatmaps.Count == 0) return null;

        using var zipStream = new MemoryStream();
        var wroteAny = false;
        using (var archive = new ZipArchive(zipStream,
                   ZipArchiveMode.Create, true))
        {
            foreach (var beatmap in beatmaps)
            {
                var osuPath = Path.Combine(storage.MapsetsPath, $"{beatmap.Id}.osu");
                if (!File.Exists(osuPath)) continue;

                var entry = archive.CreateEntry(beatmap.Filename);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(osuPath);
                await fileStream.CopyToAsync(entryStream, cancellationToken);
                wroteAny = true;
            }
        }

        return wroteAny ? zipStream.ToArray() : null;
    }

    private static void MapBeatmapAssetGroup(this RouteGroupBuilder group)
    {
        // Ported from app/api/domains/map.py's everything — every request under the b.{domain}
        // host forwards straight through to osu!'s real static-asset CDN (thumbnails, previews).
        group.MapGet("/{**path}", (HttpContext context) =>
            Results.Redirect($"https://b.ppy.sh{context.Request.Path}", true));
    }

    // bancho.py has no local avatar storage (the a.{domain} host there always forwards to a real CDN).
    // Files are stored flat as "{userId}.{ext}" under StorageOptions.AvatarsPath.
    private static void MapAvatarGroup(this RouteGroupBuilder group)
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
        });
    }

    private static void TryWriteEmbeddedResource(string resourceName, string destinationPath)
    {
        using var stream = typeof(BanchoHostGroups).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return;
        using var fileStream = File.Create(destinationPath);
        stream.CopyTo(fileStream);
    }

    // api. host: TRT snapshot (GET+WS), file downloads, WS live channels, and admin-key-gated management CRUD.
    private static void MapApiGroup(this RouteGroupBuilder group)
    {
        group.MapGet("/", () => "api");

        // GET (JSON snapshot) and WS (live "main" channel — meta/map/state, no per-player data,
        // see MatchLiveSnapshotBuilder's doc comment) share this one path: a WS handshake is itself
        // an HTTP GET with an Upgrade header, so the branch happens inside the handler.
        group.MapGet("/multi/{id:int}", async (int id, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                await MatchWebSocketRoutes.HandleMainAsync(id, context, cancellationToken);
                return;
            }

            var reportService = context.RequestServices.GetRequiredService<MatchReportService>();
            var report = await reportService.BuildAsync(id, cancellationToken);
            if (report is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await context.Response.WriteAsJsonAsync(report, cancellationToken);
        });

        // WS-only — one player's live score, decoded from MatchScoreUpdate frames (see
        // MatchScoreUpdateHandler). Player name matches how the client's own name is used elsewhere
        // (e.g. #multi_ channel membership), not a numeric id.
        group.MapGet("/multi/{id:int}/{playerName}", async (int id, string playerName, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await MatchWebSocketRoutes.HandlePlayerAsync(id, playerName, context, cancellationToken);
        });

        // WS-only — raw spectator input frames for players in this match, tagged by player name.
        // Only carries data while at least one client is spectating a player in the match (see
        // SpectateFramesHandler).
        group.MapGet("/multi/{id:int}/input", async (int id, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await MatchWebSocketRoutes.HandleInputAsync(id, context, cancellationToken);
        });

        group.MapGet("/replays/{scoreId:long}", async (long scoreId, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var replayService = context.RequestServices.GetRequiredService<ReplayService>();
            // No authenticated viewer on this public host — 0 is never a real Users.Id (seeding
            // starts at 1), so ReplayService's "skip the view-count bump for the owner" check never
            // spuriously fires here.
            var result = await replayService.FetchReplayFileAsync(scoreId, 0, cancellationToken);
            return result.Code == ReplayFetchResultCode.NotFound
                ? Results.NotFound()
                : Results.Bytes(result.Data!, "application/octet-stream");
        });

        group.MapGet("/beatmaps/{beatmapId:int}", async (int beatmapId, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var maps = context.RequestServices.GetRequiredService<IMapRepository>();
            var bmap = await maps.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
            if (bmap is null) return Results.NotFound();

            var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
            var osuPath = Path.Combine(storage.MapsetsPath, $"{bmap.Id}.osu");
            return File.Exists(osuPath) ? Results.File(osuPath, "text/plain") : Results.NotFound();
        });

        group.MapGet("/beatmapsets/{setId:int}", async (int setId, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var maps = context.RequestServices.GetRequiredService<IMapRepository>();
            var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
            var osz = await BuildOszArchiveAsync(maps, storage, setId, cancellationToken);
            return osz is null
                ? Results.NotFound()
                : Results.File(osz, "application/x-osu-beatmap-archive", $"{setId}.osz");
        });

        group.MapAdminManagement();
    }
}

/// <summary>Ported from app/objects/models.py's OsuBeatmapRequestForm — osu-getbeatmapinfo.php's JSON request body.</summary>
public sealed record OsuBeatmapInfoRequest(List<string> Filenames, List<int> Ids);