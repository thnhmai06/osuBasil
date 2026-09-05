using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints for reading and managing the server's message-of-the-day.
/// </summary>
internal static class MotdSettingsRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/settings/motd` read/write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host `/settings` route group.</param>
	public static void MapMotdSettingsRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/motd", async (MotdService motd, CancellationToken cancellationToken) =>
			{
				var text = await motd.GetTextAsync(cancellationToken);
				return Results.Json(new MotdSettingsView(text));
			})
			.WithGroupName("basilapi")
			.WithName("getMotd")
			.WithSummary("Get the message-of-the-day.")
			.WithDescription("""
			                 Returns the message currently shown to a player as a login notification, and by the
			                 IRC gateway's `/MOTD` command. `null` when none is set.
			                 """)
			.WithTags("MOTD")
			.Produces<MotdSettingsView>()
			.WithExample(StatusCodes.Status200OK, new MotdSettingsView("Welcome to Basil!"));

		group.MapPut("/motd", async (MotdSettingsBody body, MotdService motd, CancellationToken cancellationToken) =>
			{
				await motd.SetTextAsync(body.Text, cancellationToken);
				var stored = await motd.GetTextAsync(cancellationToken);
				return Results.Json(new MotdSettingsView(stored));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMotd")
			.WithSummary("Set the message-of-the-day.")
			.WithDescription("""
			                 Body: `{ text }`. Replaces the message shown on login and via the IRC gateway's
			                 `/MOTD`. A null, empty, or blank `text` clears it, so nothing is shown.

			                 Takes effect for players who log in after this change, no restart required.
			                 """ + AdminKeyNote)
			.WithTags("MOTD")
			.Produces<MotdSettingsView>()
			.WithExample(StatusCodes.Status200OK, new MotdSettingsView(null));
	}

	/// <summary>Response body for `GET /settings/motd` and confirmation body for `PUT /settings/motd`.</summary>
	public sealed record MotdSettingsView(string? Text);

	/// <summary>Request body for `PUT /settings/motd`.</summary>
	public sealed record MotdSettingsBody(string? Text);
}
