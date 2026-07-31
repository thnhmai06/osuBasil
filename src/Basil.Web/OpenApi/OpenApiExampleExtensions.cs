using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Json;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web.Middleware;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Attaches a fake-data example to a route's already-declared JSON response, keyed by status code —
///     lets every documented case (<c>.Produces&lt;T&gt;</c>/<c>.Produces&lt;ErrorResponse&gt;</c>) carry a
///     concrete illustration instead of just a schema. Must run after the status code's response entry
///     already exists (i.e. after the matching <c>.Produces</c> call in the same fluent chain) — a
///     no-op otherwise. On the <c>basilapi</c> document, the raw <c>example</c> is wrapped
///     in the Enveloped Response Standard (see <see cref="Envelope{T}" />) to mirror what
///     <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> actually does to the response body at
///     runtime — every other document's examples pass through unwrapped, since only basilapi routes are
///     enveloped. A route carrying <see cref="SseEndpointMarker" /> is also left unwrapped for its own
///     2xx status only — that's its real raw, un-enveloped SSE event payload — matching
///     <see cref="EnvelopeSchemaTransformer" />'s same per-status exception for the declared schema; any
///     other status on that same route (a synchronous pre-stream error) is still wrapped like everywhere
///     else.
/// </summary>
internal static class OpenApiExampleExtensions
{
	private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web)
	{
		Converters = { new CountryJsonConverter() }
	};

	public static RouteHandlerBuilder WithExample(this RouteHandlerBuilder builder, int statusCode, object example)
	{
		return builder.AddOpenApiOperationTransformer((operation, context, _) =>
		{
			if (operation.Responses?.TryGetValue(statusCode.ToString(), out var response) == true &&
			    response?.Content?.TryGetValue("application/json", out var mediaType) == true)
			{
				// Only an SSE route's own 2xx is the raw, un-enveloped stream payload (see
				// EnvelopeSchemaTransformer's matching per-status check) — any other status on that
				// same route is a synchronous JSON error and still gets the envelope like every other
				// route's error response.
				var isSseSuccessPayload = statusCode < 400 && context.Description.ActionDescriptor.EndpointMetadata
					.OfType<SseEndpointMarker>().Any();

				mediaType.Example = context.DocumentName == "basilapi" && !isSseSuccessPayload
					? BuildEnvelope(statusCode, context.Description.HttpMethod, example)
					: JsonSerializer.SerializeToNode(example, JsonWebOptions);
			}

			return Task.CompletedTask;
		});
	}

	private static JsonNode BuildEnvelope(int statusCode, string? httpMethod, object example)
	{
		var body = JsonSerializer.SerializeToNode(example, JsonWebOptions);
		return EnvelopeBuilder.Build(statusCode, httpMethod, body, JsonWebOptions);
	}

	/// <summary>
	///     Only `GET /matches/{matchId}/live/{slotIndex}` needs this — its single 200 status genuinely
	///     carries three different JSON shapes (one per SSE `event:` name), so a single `.WithExample`
	///     doesn't fit. Rewrites the 200 schema to `oneOf` the three shapes and attaches one named example
	///     per shape. The route's own `.Produces&lt;PlayerLiveScore&gt;()` is what makes the framework's
	///     default per-operation schema generation register <c>PlayerLiveScore</c> as a named component in
	///     the first place (this transformer runs after that, so it just re-wraps the already-registered
	///     `$ref` rather than building `PlayerLiveScore`'s schema by hand) — <c>MatchSlotView</c> and
	///     <c>SpectateFramesEvent</c> are already registered components via their own routes
	///     (`GET /matches/{matchId}/slots`, `GET /users/{idOrName}/live`), referenced here by name.
	/// </summary>
	public static RouteHandlerBuilder WithSlotLiveExamples(this RouteHandlerBuilder builder)
	{
		return builder.AddOpenApiOperationTransformer((operation, context, _) =>
		{
			if (operation.Responses?.TryGetValue("200", out var response) != true ||
			    response?.Content?.TryGetValue("application/json", out var mediaType) != true)
				return Task.CompletedTask;

			var playerLiveScoreSchema = mediaType!.Schema!;
			mediaType.Schema = new OpenApiSchema
			{
				OneOf =
				[
					new OpenApiSchemaReference("MatchSlotView", context.Document),
					playerLiveScoreSchema,
					new OpenApiSchemaReference("SpectateFramesEvent", context.Document)
				]
			};

			mediaType.Example = null;
			mediaType.Examples = new Dictionary<string, IOpenApiExample>
			{
				["slot"] = new OpenApiExample
				{
					Summary = "event: slot",
					Value = JsonSerializer.SerializeToNode(
						new MatchSlotView(0, new UserBrief(7, "Alice", Country.Us), SlotStatus.Playing,
							MatchTeam.Red, Mods.NoMod, false, true), JsonWebOptions)
				},
				["score"] = new OpenApiExample
				{
					Summary = "event: score",
					Value = JsonSerializer.SerializeToNode(
						new PlayerLiveScore(new UserBrief(7, "Alice", Country.Us), 45_000, 620, 40, 5, 0, 3, 1,
							4_213_567, 812, 812, false, 98, false), JsonWebOptions)
				},
				["input"] = new OpenApiExample
				{
					Summary = "event: input",
					Value = JsonSerializer.SerializeToNode(
						new SpectateFramesEvent(new UserBrief(7, "Alice", Country.Us), ReplayAction.Standard, 0,
							[new ReplayFrameData(Keys.Left1, TaikoByte.None, 100.5f, 200.25f, 1000)],
							new ScoreFrameData(1000, 0, 10, 2, 1, 0, 0, 0, 123456, 50, 12, true, 100, 0, false)),
						JsonWebOptions)
				}
			};

			return Task.CompletedTask;
		});
	}
}