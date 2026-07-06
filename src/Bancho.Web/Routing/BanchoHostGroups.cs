using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Authentication;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.DependencyInjection;

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
                    dispatcher.Dispatch(session, body);
                    session.LastRecvTime = clock.UtcNow.ToUnixTimeSeconds();
                    responseBody = session.Dequeue();
                }
            }

            response.ContentType = "text/html; charset=UTF-8";
            await response.Body.WriteAsync(responseBody, cancellationToken);
        });
    }

    private static void MapOsuWebGroup(this RouteGroupBuilder group) =>
        group.MapGet("/", () => "osu");

    private static void MapBeatmapAssetGroup(this RouteGroupBuilder group) =>
        group.MapGet("/", () => "map");

    private static void MapApiGroup(this RouteGroupBuilder group) =>
        group.MapGet("/", () => "api");
}
