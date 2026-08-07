using Basil.Application.Services.Bot;
using Basil.Application.Services.Content;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="AnnounceRoutes" /> class.</summary>
internal sealed class AnnounceRoutesLog;

/// <summary>
///     Registers the endpoint for pushing an in-game notification popup to online players.
/// </summary>
internal static class AnnounceRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/announce` route on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapAnnounceRoutes(this RouteGroupBuilder group)
	{
		group.MapPost("/announce", HandleAnnounce)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("announce")
			.WithSummary("Send an in-game notification to online players.")
			.WithDescription("""
			                 Pushes a notification popup (the same mechanism the login MOTD uses) to a set of
			                 currently online players. BasilBot never receives it, even if explicitly listed.

			                 `userIds` selects the recipients; omit it (or send `null`) to notify everyone
			                 currently online. Ids that are offline, unknown, or BasilBot's are silently skipped.

			                 Nothing is persisted — this is a one-time push at the moment of the call, not a
			                 standing server-wide message shown to players who log in afterward.
			                 """ + AdminKeyNote)
			.WithTags("Announce")
			.Produces<AnnounceResultView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new AnnounceResultView(true, 3))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Message must not be empty."));

		group.MapGet("/announce/motd", async (MotdService motd, CancellationToken cancellationToken) =>
			{
				var text = await motd.GetTextAsync(cancellationToken);
				return Results.Json(new MotdView(text));
			})
			.WithGroupName("basilapi")
			.WithName("getMotd")
			.WithSummary("Get the message-of-the-day.")
			.WithDescription("Returns the message currently shown to a player on login, or `null` if none is set.")
			.WithTags("Motd")
			.Produces<MotdView>()
			.WithExample(StatusCodes.Status200OK, new MotdView("Welcome to Basil!"));

		group.MapPut("/announce/motd", async (MotdBody body, MotdService motd, ILogger<AnnounceRoutesLog> logger,
				CancellationToken cancellationToken) =>
			{
				await motd.SetTextAsync(body.Text, cancellationToken);
				logger.LogInformation("MOTD updated via admin API");
				return Results.Json(new MotdChangedView(true, "MOTD updated."));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMotd")
			.WithSummary("Set the message-of-the-day.")
			.WithDescription("""
			                 Body: `{ text }`. Replaces whatever message is currently set. A null, empty, or
			                 blank `text` clears it, so no notification is shown on login.

			                 Takes effect for players who log in after this change.
			                 """ + AdminKeyNote)
			.WithTags("Motd")
			.Produces<MotdChangedView>()
			.WithExample(StatusCodes.Status200OK, new MotdChangedView(true, "MOTD updated."));
	}

	private static IResult HandleAnnounce(AnnounceBody body, ISessionRegistry<GameSession> sessionRegistry,
		ILogger<AnnounceRoutesLog> logger)
	{
		if (string.IsNullOrWhiteSpace(body.Message))
			return Results.BadRequest(new ErrorResponse("Message must not be empty."));

		var targets = body.UserIds is null
			? sessionRegistry.All.Where(s => s.Id != BotBootstrapService.BotId)
			: body.UserIds.Where(id => id != BotBootstrapService.BotId)
				.Select(sessionRegistry.GetByUserId)
				.Where(s => s is not null);

		var packet = ServerPacketWriter.Notification(body.Message);
		var deliveredCount = 0;
		foreach (var session in targets)
		{
			session!.Enqueue(packet);
			deliveredCount++;
		}

		logger.LogInformation("Announcement sent via admin API: DeliveredCount={DeliveredCount}", deliveredCount);
		return Results.Json(new AnnounceResultView(true, deliveredCount));
	}

	/// <summary>Request body for `POST /announce`.</summary>
	public sealed record AnnounceBody(string Message, int[]? UserIds = null);

	/// <summary>Confirmation body for `POST /announce`.</summary>
	public sealed record AnnounceResultView(bool Success, int DeliveredCount);

	/// <summary>Response body for `GET /announce/motd`.</summary>
	public sealed record MotdView(string? Text);

	/// <summary>Request body for `PUT /announce/motd`.</summary>
	public sealed record MotdBody(string? Text);

	/// <summary>Confirmation body for `PUT /announce/motd`.</summary>
	public sealed record MotdChangedView(bool Success, string Message);
}