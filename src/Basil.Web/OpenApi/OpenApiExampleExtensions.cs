using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Formats;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web.Routing.Api;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Attaches serialized examples to already-declared OpenAPI responses, optionally wrapping
///     them in the standard <see cref="Envelope{T}" /> shape so the documented payload matches
///     the runtime response body.
/// </summary>
/// <remarks>
///     <para>
///         The attached examples are what Scalar and generated client SDKs display as sample
///         request and response bodies for the route.
///     </para>
///     <para>
///         Each example is attached to an existing response entry identified by its HTTP status
///         code. The matching <c>.Produces(...)</c> call must therefore appear earlier in the
///         same endpoint configuration; otherwise this extension has nothing to modify.
///     </para>
///     <para>
///         In the <c>basilapi</c> document, examples are wrapped in the Enveloped Response
///         Standard (see <see cref="Envelope{T}" />) to mirror the output produced by
///         <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> at runtime. Examples in every
///         other OpenAPI document remain unchanged.
///     </para>
///     <para>
///         The only exception is a Server-Sent Events endpoint's successful 2xx response
///         (every route whose path contains a literal <c>live</c> segment), whose payload is
///         intentionally documented as the raw SSE event body rather than an envelope. Any
///         synchronous error returned before the stream opens is still wrapped like every other
///         JSON response, matching <see cref="EnvelopeSchemaTransformer" />.
///     </para>
/// </remarks>
internal static class OpenApiExampleExtensions
{
	private static readonly JsonSerializerOptions JsonWebOptions = BasilJsonOptions.Instance;

	/// <summary>
	///     Attaches a serialized example to the route's already-declared JSON response for
	///     <paramref name="statusCode" />.
	/// </summary>
	/// <param name="builder">The route to attach the example to.</param>
	/// <param name="statusCode">The response status the example illustrates.</param>
	/// <param name="example">The example payload to serialize.</param>
	/// <returns>The <paramref name="builder" /> for continued chaining.</returns>
	public static RouteHandlerBuilder WithExample(this RouteHandlerBuilder builder, int statusCode, object example)
	{
		return builder.AddOpenApiOperationTransformer((operation, context, _) =>
		{
			if (operation.Responses?.TryGetValue(statusCode.ToString(), out var response) == true &&
			    response.Content?.TryGetValue("application/json", out var mediaType) == true)
			{
				// Only an SSE route's own 2xx is the raw, unenveloped stream payload (see
				// EnvelopeSchemaTransformer's matching per-status check). Any other status on that same
				// route is a synchronous JSON error and still gets the envelope like every other
				// route's error response.
				var isSseSuccessPayload = statusCode < 400 &&
				                          LiveSseRoutes.IsSseRoute(context.Description.RelativePath);

				mediaType.Example = context.DocumentName == "basilapi" && !isSseSuccessPayload
					? BuildEnvelope(statusCode, context.Description.HttpMethod, example)
					: JsonSerializer.SerializeToNode(example, JsonWebOptions);
			}

			return Task.CompletedTask;
		});
	}

	/// <summary>
	///     Serializes the example and wraps it in the envelope shape.
	/// </summary>
	/// <param name="statusCode">The HTTP status code of the example's response.</param>
	/// <param name="httpMethod">The HTTP method of the operation.</param>
	/// <param name="example">The example payload to wrap.</param>
	/// <returns>The envelope-wrapped example as a JSON node.</returns>
	private static JsonObject BuildEnvelope(int statusCode, string? httpMethod, object example)
	{
		var body = JsonSerializer.SerializeToNode(example, JsonWebOptions);
		return EnvelopeBuilder.Build(statusCode, httpMethod, body, JsonWebOptions);
	}

	/// <summary>
	///     Documents the multiple event payloads produced by the slot-live SSE endpoint by replacing
	///     its single 200-response schema with a <c>oneOf</c> union and attaching one named example
	///     for each SSE event type.
	/// </summary>
	/// <remarks>
	///     <para>
	///         This customization is only required for
	///         <c>GET /matches/{matchId}/live/{slotIndex}</c>, whose successful response can carry
	///         three distinct JSON payloads depending on the emitted SSE <c>event:</c> name:
	///         <c>slot</c>, <c>score</c>, or <c>input</c>.
	///     </para>
	///     <para>
	///         The endpoint's existing <c>.Produces&lt;PlayerLiveScore&gt;()</c> declaration
	///         ensures the framework has already generated and registered the <c>PlayerLiveScore</c>
	///         component. This transformer therefore reuses that generated schema instead of rebuilding it.
	///     </para>
	///     <para>
	///         <c>MatchSlotView</c> and <c>SpectateFramesEvent</c> are likewise reused through their
	///         existing component registrations created by their own endpoints, so the resulting
	///         schema references shared components rather than duplicating object definitions.
	///     </para>
	/// </remarks>
	/// <param name="builder">The route whose 200 response gets the three named examples.</param>
	/// <returns>The <paramref name="builder" /> for continued chaining.</returns>
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
							[new ReplayFrame(Keys.Left1, TaikoByte.None, 100.5f, 200.25f, 1000)],
							new ScoreFrame(1000, 0, 10, 2, 1, 0, 0, 0, 123456, 50, 12, true, 100, 0, false)),
						JsonWebOptions)
				}
			};

			return Task.CompletedTask;
		});
	}
}