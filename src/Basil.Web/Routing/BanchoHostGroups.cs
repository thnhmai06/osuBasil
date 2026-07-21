using System.IO.Compression;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions.Multiplayer;
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
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Beatmaps;
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
    extension(RouteGroupBuilder group)
    {
        private void MapBanchoGroup()
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
            });
        }

        private void MapOsuWebGroup()
        {
            group.MapGet("/", () => "osu");

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
            });

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
            });

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
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is null)
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);

                if (mapSetId is null && mapId is null && checksum is null)
                    return Results.Text("", "text/html", Encoding.UTF8);

                var maps = context.RequestServices.GetRequiredService<IMapRepository>();
                var bmapSet =
                    await maps.FetchOneAsync(mapId, checksum, setId: mapSetId, cancellationToken: cancellationToken);

                return Results.Text(DirectSearchService.FormatSet(bmapSet), "text/html", Encoding.UTF8);
            });

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
                var osuPath = BeatmapIngestionService.OsuFilePath(storage, bmap);
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
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                var replayService = context.RequestServices.GetRequiredService<ReplayService>();
                var result = await replayService.FetchReplayFileAsync(scoreId, cancellationToken);

                return result.Code == ReplayFetchResultCode.NotFound
                    ? Results.NotFound()
                    : Results.Bytes(result.Data!, "application/octet-stream");
            });

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
                var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
                var player = await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
                if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

                var clientIntegrity = context.RequestServices.GetRequiredService<ClientIntegrityService>();
                var result = await clientIntegrity.HandleLastFmFlagsAsync(player, beatmapIdOrHiddenFlag, cancellationToken);

                return result == ClientIntegrityResult.StopSending
                    ? Results.Text("-3", "text/html", Encoding.UTF8)
                    : Results.Text("", "text/html", Encoding.UTF8);
            });

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
            });

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
            });
        }
    }

    private static readonly string[] VideoExtensions = [".mp4", ".avi", ".flv", ".wmv", ".mkv", ".mov"];

    /// <summary>
    ///     Packages a locally-ingested mapset's whole storage folder (audio/images/video/.osu, as
    ///     extracted) into a fresh .osz on the fly (shared by /d/{setId} and the api. host's
    ///     /beatmapsets/{setId}). <paramref name="noVideo" /> skips any video file and appends
    ///     " [no video]" to the returned filename. Returns null when the set has no local folder or
    ///     the folder is empty.
    /// </summary>
    private static async Task<(byte[] Bytes, string FileName)?> BuildOszArchiveAsync(IMapRepository maps,
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
                Results.Redirect($"https://b.ppy.sh{context.Request.Path}", true));
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
            });
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

        // GET — read a match's privacy status. Public (no auth). Returns the current runtime
        // IsPrivate flag for live matches; closed matches default to false.
        group.MapGet("/multi/{id:int}/privacy", async (int id, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var matchRegistry = context.RequestServices.GetRequiredService<IMatchRegistry>();
            var match = matchRegistry.GetByDbId(id);
            if (match is not null)
                return Results.Json(new { isPrivate = match.IsPrivate });

            var matchPersistence = context.RequestServices.GetRequiredService<IMatchPersistenceRepository>();
            var row = await matchPersistence.FetchMatchAsync(id, cancellationToken);
            return row is not null
                ? Results.Json(new { isPrivate = false })
                : Results.NotFound();
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
            var result = await replayService.FetchReplayFileAsync(scoreId, cancellationToken);
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
            var osuPath = BeatmapIngestionService.OsuFilePath(storage, bmap);
            return File.Exists(osuPath) ? Results.File(osuPath, "text/plain") : Results.NotFound();
        });

        group.MapGet("/beatmapsets/{setId:int}", async (int setId, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var maps = context.RequestServices.GetRequiredService<IMapRepository>();
            var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
            var osz = await BuildOszArchiveAsync(maps, storage, setId, false, cancellationToken);
            return osz is null
                ? Results.NotFound()
                : Results.File(osz.Value.Bytes, "application/x-osu-beatmap-archive", osz.Value.FileName);
        });

        group.MapAdminManagement();
    }
}
