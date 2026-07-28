using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Json;
using Basil.Web.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Basil.Web.OpenApi;

/// <summary>
///     Attaches a fake-data example to a route's already-declared JSON response, keyed by status code —
///     lets every documented case (<c>.Produces&lt;T&gt;</c>/<c>.Produces&lt;ErrorResponse&gt;</c>) carry a
///     concrete illustration instead of just a schema. Must run after the status code's response entry
///     already exists (i.e. after the matching <c>.Produces</c> call in the same fluent chain) — a
///     no-op otherwise. On the <c>basilapi</c> document, the raw <paramref name="example" /> is wrapped
///     in the Enveloped Response Standard (see <see cref="Envelope{T}" />) to mirror what
///     <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> actually does to the response body at
///     runtime — every other document's examples pass through unwrapped, since only basilapi routes are
///     enveloped. A route carrying <see cref="SseEndpointMarker" /> is also left unwrapped even on
///     basilapi — its real payload is a raw, un-enveloped SSE event, matching
///     <see cref="EnvelopeSchemaTransformer" />'s same exception for the declared schema.
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
            if (operation.Responses.TryGetValue(statusCode.ToString(), out var response) &&
                response?.Content.TryGetValue("application/json", out var mediaType) == true)
            {
                var isSse = context.Description.ActionDescriptor.EndpointMetadata
                    .OfType<SseEndpointMarker>().Any();

                mediaType!.Example = context.DocumentName == "basilapi" && !isSse
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
}
