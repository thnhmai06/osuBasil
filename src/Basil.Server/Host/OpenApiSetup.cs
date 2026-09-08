using System.Text.Json.Nodes;
using Basil.Server.Shared.Http.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Server.Host;

/// <summary>Registers the host's OpenAPI documents.</summary>
internal static class OpenApiSetup
{
	/// <summary>
	///     Defines the tags used by the Basil API documentation, grouped by resource.
	/// </summary>
	/// <remarks>
	///     The declaration order determines how tag groups appear in the generated API documentation.
	/// </remarks>
	private static readonly (string Group, (string Tag, string Description)[] Tags)[] BasilApiTagGroups =
	[
		("Matches",
		[
			("Matches", "Match listing and creation."),
			("Match Report", "Tournament match reports (TRT)."),
			("Match Settings", "Match room configuration."),
			("Match Live", "Room-wide realtime playing status and merged per-slot live streams."),
			("Match Hosts", "Match host management."),
			("Match Referees", "Match referee management."),
			("Match Bans", "Management of players banned from a match."),
			("Match Slots",
				"Management of the match's 16 slots, including assignments, teams, locking, invitations, and kicking players."),
			("Match Timer", "Match countdown timer."),
			("Match Abort", "Abort the match currently in progress."),
			("Match Close", "Close the match immediately."),
			("Match Chat", "The match room's live chat stream, and saying something in it as BasilBot.")
		]),
		("Users",
		[
			("Users", "User CRUD, avatar management, and live spectator input streams.")
		]),
		("Beatmapsets",
		[
			("Beatmapsets", "Beatmapset CRUD, archive and storyboard downloads, and freeze/private management."),
			("Beatmaps", "Individual beatmap lookups and beatmap file/background downloads.")
		]),
		("Scores",
		[
			("Scores", "Score lookups, replay downloads, and paginated score listings.")
		]),
		("FAQ",
		[
			("FAQ", "Public FAQ entries.")
		]),
		("Seasonal Backgrounds",
		[
			("Seasonal Backgrounds", "Public seasonal background images.")
		]),
		("Menu",
		[
			("Menu Banners", "Main-menu promotional banners (assets.<domain>/menu-content.json)."),
			("Menu Icon", "The in-game main menu icon image and its click-through URL.")
		]),
		("Settings",
		[
			("Admin Key", "The server's admin key: status, rotation, and bypass mode."),
			("Mirror", "The server's beatmap mirror endpoints."),
			("MOTD", "The message shown to a player on login.")
		]),
		("Announce",
		[
			("Announce", "Pushing an in-game notification to online players.")
		]),
		("Abbreviation Redirects",
		[
			("Abbreviation Redirects", "302 redirects from abbreviated paths to their canonical resource paths.")
		]),
		("Health",
		[
			("Health", "Health and liveness checks.")
		])
	];

	/// <summary>
	///     Adds one OpenAPI document per host group (bancho/osuweb/beatmapasets/avatar/assets/basilapi).
	/// </summary>
	/// <param name="builder">The web application builder whose OpenAPI documents are added.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		// One document per group rather than one for the whole app: several groups register the
		// same literal path template (both the bancho and osu-web groups have their own GET /),
		// which OpenAPI cannot represent twice in one document.
		AddOpenApiDocument(
			builder,
			"bancho",
			"osu! Client API: Bancho Protocol",
			"The osu! stable client's binary Bancho protocol, including login and the packet-based session that follows.");
		AddOpenApiDocument(
			builder,
			"osuweb",
			"osu! Client API: osu! Web",
			"The osu! stable client's HTTP `web/*.php` endpoints, along with beatmap and replay downloads and in-game account registration.");
		AddOpenApiDocument(
			builder,
			"beatmapassets",
			"osu! Client API: Beatmap Assets",
			"Beatmapset thumbnail and preview image requests, served from locally stored assets with no dependency on osu.ppy.sh.");
		AddOpenApiDocument(
			builder,
			"avatar",
			"osu! Client API: Avatar Files",
			"Locally hosted user avatar images.");
		AddOpenApiDocument(
			builder,
			"assets",
			"Basil Assets",
			"Menu banner/icon/seasonal images and beatmapset covers, served through ImageSharp.Web.");
		AddOpenApiDocument(
			builder,
			"basilapi",
			"Basil API",
			"Basil's tournament-facing HTTP API, including tournament match reports, live SSE streams, beatmap and replay downloads, and admin-key-protected management endpoints.",
			BasilApiTagGroups);
	}

	/// <summary>
	///     Adds a single OpenAPI document for the given host group with the given title and description.
	/// </summary>
	/// <remarks>
	///     When <paramref name="tagGroups" /> is provided, the document's sidebar is grouped by tag
	///     (the shortest/most general route first per group), and the document is enveloped and
	///     admin-key-gated; every other document describes the raw osu! client protocol as-is.
	/// </remarks>
	/// <param name="builder">The web application builder whose OpenAPI options are configured.</param>
	/// <param name="documentName">The document name, matching routes' <c>.WithGroupName(...)</c> values.</param>
	/// <param name="title">The document title shown in the generated UI.</param>
	/// <param name="description">The document description shown in the generated UI.</param>
	/// <param name="tagGroups">Optional tag groups driving the Scalar sidebar grouping for the basilapi document.</param>
	private static void AddOpenApiDocument(WebApplicationBuilder builder, string documentName, string title,
		string description, (string Group, (string Tag, string Description)[] Tags)[]? tagGroups = null)
	{
		builder.Services.AddOpenApi(documentName, options =>
		{
			options.AddDocumentTransformer((document, _, _) =>
			{
				document.Info.Title = title;
				document.Info.Description = description;
				document.Info.Version = "v1";

				if (tagGroups is null) return Task.CompletedTask;

				document.Tags = tagGroups
					.SelectMany(g => g.Tags)
					.Select(t => new OpenApiTag { Name = t.Tag, Description = t.Description })
					.ToHashSet();

				var tagGroupsJson = new JsonArray(tagGroups.Select(JsonNode (g) =>
					new JsonObject
					{
						["name"] = g.Group,
						["tags"] = new JsonArray(
							g.Tags.Select(t => (JsonNode)t.Tag).ToArray())
					}).ToArray());
				document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
				document.Extensions["x-tagGroups"] = new JsonNodeExtension(tagGroupsJson);

				// Scalar's sidebar buckets each tag's operations by their position in the document,
				// not alphabetically. Reorder Paths (the shortest/most general route first per tag
				// section) without touching the actual C# route-registration order in Routing/*.cs.
				var reordered = new OpenApiPaths();
				foreach (var (path, item) in document.Paths
					         .OrderBy(kvp => kvp.Key.Count(c => c == '/'))
					         .ThenBy(kvp => kvp.Key.Length)
					         .ThenBy(kvp => kvp.Key, StringComparer.Ordinal))
					reordered[path] = item;
				document.Paths = reordered;

				return Task.CompletedTask;
			});

			// Applies to every document: a generator-default artifact of how .NET's OpenAPI schema
			// builder represents integers/numbers, not specific to basilapi's own types.
			options.AddNumericSchemaSimplificationTransformer();

			// Only the basilapi document is enveloped/admin-key-gated. Every other document (bancho/
			// osu-web/beatmap-assets/avatar) documents the raw osu! client protocol as-is.
			if (tagGroups is null) return;

			options.AddAdminKeyDocumentTransformer();
			options.AddCustomConverterSchemaTransformer();
			options.AddEnumValuesSchemaTransformer();
			options.AddPolymorphicOneOfSchemaTransformer();
			options.AddEnvelopeSchemaTransformer();
		});
	}
}
