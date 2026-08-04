using System.Net;
using Basil.Application.Packets;
using Basil.Application.Services.Authentication;
using Basil.Application.Sessions;
using Basil.Domain.Login;
using Basil.Protocol.Packets;
using LoginRequest = Basil.Application.Services.Authentication.LoginRequest;

namespace Basil.Web.Routing.Bancho;

/// <summary>
///     Registers the bancho protocol endpoints served by the classic osu! client host
///     (<c>c.</c>, <c>ce.</c>, <c>c4.</c>, <c>c5.</c>, and <c>c6.</c>).
/// </summary>
/// <remarks>
///     These endpoints implement the legacy bancho binary protocol used by the osu! client for
///     authentication and all real-time gameplay communication.
/// </remarks>
internal static class BanchoProtocolRoutes
{
	private const string BanchoPacketCatalog =
		""""
		private const string BanchoPacketCatalog = """
		The request body consists of one or more bancho packets encoded as:

		- `uint16` packet id
		- `uint8` reserved byte
		- `uint32` payload length
		- payload bytes

		Multiple packets may be concatenated into a single request.

		Supported packet categories include:

		### Core

		- Ping
		- ChangeAction
		- RequestStatusUpdate
		- UserStatsRequest
		- UserPresenceRequest
		- UserPresenceRequestAll
		- ReceiveUpdates
		- SetAwayMessage
		- Logout

		### Channels

		- ChannelJoin
		- ChannelPart
		- SendPublicMessage
		- SendPrivateMessage
		- ToggleBlockNonFriendDms

		### Spectating

		- StartSpectating
		- StopSpectating
		- SpectateFrames
		- CantSpectate

		Live replay frames are also published through the Basil API's
		`GET /users/{idOrName}/live` Server-Sent Events endpoint.

		### Multiplayer

		- CreateMatch
		- JoinMatch
		- PartMatch
		- MatchChangeSlot
		- MatchChangeSettings
		- MatchChangePassword
		- MatchChangeMods
		- MatchChangeTeam
		- MatchLock
		- MatchTransferHost
		- MatchReady
		- MatchNotReady
		- MatchStart
		- MatchLoadComplete
		- MatchSkipRequest
		- MatchNoBeatmap
		- MatchHasBeatmap
		- MatchFailed
		- MatchScoreUpdate
		- MatchComplete
		- MatchInvite
		- TournamentMatchInfoRequest
		- TournamentJoinMatchChannel
		- TournamentLeaveMatchChannel

		Live multiplayer score updates are also published through the Basil API's
		`GET /matches/{matchId}/live/{slotIndex}` Server-Sent Events endpoint.

		The response body uses the same packet format and contains zero or more packets
		queued for the client. An empty response body simply means no packets are
		currently pending.
		""";
		"""";

	/// <summary>
	///     Registers the bancho host group's routes: a `GET /` liveness stub plus the `POST /`
	///     login or authenticated packet-exchange endpoint.
	/// </summary>
	/// <param name="group">The bancho host route group.</param>
	public static void MapBanchoGroup(this RouteGroupBuilder group)
	{
		group.MapGet("/", () => "cho")
			.WithGroupName("bancho")
			.WithSummary("Check server availability")
			.WithDescription(
				"Returns the literal string `cho`. This endpoint exists solely as a liveness check and is " +
				"not used during normal client communication.")
			.WithTags("Bancho Protocol");

		// No osu-token header means this is a login request; a present-but-unknown token
		// means the server restarted since the client's last request, so it's told to
		// reconnect. Services are resolved from context.RequestServices rather than as
		// delegate parameters, so the DB-backed login use case is only ever constructed on
		// the branch that actually needs it.
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
					// in front (e.g., local dev), synthesize it from the direct TCP peer so
					// IpResolver only ever trusts the forwarded headers, never the direct peer
					// address, so it still has something to read.
					if (!headers.ContainsKey("CF-Connecting-IP") && !headers.ContainsKey("X-Forwarded-For"))
					{
						// IpResolver falls back to X-Real-IP whenever X-Forwarded-For has a
						// single entry (the proxy config is assumed to always set both), so both
						// need synthesizing together, not just one.
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
					var sessionRegistry = context.RequestServices.GetRequiredService<IUserSessionRegistry>();
					var session = sessionRegistry.GetByToken(token);
					if (session is null)
					{
						responseBody = [.. ServerPacketWriter.RestartServer(0)];
					}
					else
					{
						var dispatcher = context.RequestServices.GetRequiredService<PacketDispatcher>();
						await dispatcher.DispatchAsync(session, body, cancellationToken);
						session.LastRecvTime = DateTimeOffset.UtcNow;
						responseBody = session.Dequeue();
					}
				}

				response.ContentType = "text/html; charset=UTF-8";
				await response.Body.WriteAsync(responseBody, cancellationToken);
			})
			.WithGroupName("bancho")
			.WithSummary("Authenticate or exchange bancho packets")
			.WithDescription("""
			                 Handles both login and authenticated bancho communication.

			                 Without an `osu-token` request header, the request body is interpreted as a login request. A successful login returns a `cho-token` response header, which must be included as the `osu-token` header on all subsequent requests.
			                 With a valid `osu-token`, the request body contains one or more bancho packets. The response contains any queued packets waiting for the client, or an empty body if no packets are available.
			                 If the supplied `osu-token` is no longer valid, the client is instructed to reconnect.

			                 Request and response bodies use the native bancho binary packet format.
			                 """ + "\n\n" + BanchoPacketCatalog)
			.WithTags("Bancho Protocol");
	}
}