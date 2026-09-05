using Basil.Application.Services.Beatmaps;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints for reading and managing the server's beatmap mirror endpoints.
/// </summary>
internal static class MirrorSettingsRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/settings/mirror` read/write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host `/settings` route group.</param>
	public static void MapMirrorSettingsRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/mirror", async (MirrorService mirror, CancellationToken cancellationToken) =>
			{
				var endpoints = await mirror.GetAsync(cancellationToken);
				return Results.Json(new MirrorSettingsView(endpoints.DownloadEndpoint, endpoints.SearchEndpoint));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("getMirrorSettings")
			.WithSummary("Get the beatmap mirror endpoints.")
			.WithDescription("""
			                 Returns the download and search mirror endpoints currently in effect. A `null` value
			                 means beatmaps or search fall back to local storage only for that path.
			                 """ + AdminKeyNote)
			.WithTags("Mirror")
			.Produces<MirrorSettingsView>()
			.WithExample(StatusCodes.Status200OK,
				new MirrorSettingsView("https://catboy.best/d", "https://catboy.best/api/v2/search"));

		group.MapPut("/mirror",
				async (MirrorSettingsBody body, MirrorService mirror, CancellationToken cancellationToken) =>
				{
					await mirror.SetAsync(body.DownloadEndpoint, body.SearchEndpoint, cancellationToken);
					return Results.Json(new MirrorSettingsView(body.DownloadEndpoint, body.SearchEndpoint));
				})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMirrorSettings")
			.WithSummary("Set the beatmap mirror endpoints.")
			.WithDescription("""
			                 Body: `{ downloadEndpoint?, searchEndpoint? }`. Replaces both endpoints; omit a field
			                 (or send it as `null`) to clear that endpoint and fall back to local storage only for
			                 that path.

			                 Takes effect immediately for every subsequent request, no restart required.
			                 """ + AdminKeyNote)
			.WithTags("Mirror")
			.Produces<MirrorSettingsView>()
			.WithExample(StatusCodes.Status200OK, new MirrorSettingsView("https://catboy.best/d", null));
	}

	/// <summary>Response body for `GET /settings/mirror` and confirmation body for `PUT /settings/mirror`.</summary>
	public sealed record MirrorSettingsView(string? DownloadEndpoint, string? SearchEndpoint);

	/// <summary>Request body for `PUT /settings/mirror`.</summary>
	public sealed record MirrorSettingsBody(string? DownloadEndpoint, string? SearchEndpoint);
}
