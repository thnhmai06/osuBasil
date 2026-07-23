using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace Basil.Web.OpenApi;

/// <summary>
///     Attaches a fake-data example to a route's already-declared JSON response, keyed by status code —
///     lets every documented case (<c>.Produces&lt;T&gt;</c>/<c>.Produces&lt;ErrorResponse&gt;</c>) carry a
///     concrete illustration instead of just a schema. Must run after the status code's response entry
///     already exists (i.e. after the matching <c>.Produces</c> call in the same fluent chain) — a
///     no-op otherwise.
/// </summary>
internal static class OpenApiExampleExtensions
{
    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder WithExample(this RouteHandlerBuilder builder, int statusCode, object example)
    {
        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.Responses.TryGetValue(statusCode.ToString(), out var response) &&
                response?.Content.TryGetValue("application/json", out var mediaType) == true)
                mediaType!.Example = JsonSerializer.SerializeToNode(example, JsonWebOptions);

            return Task.CompletedTask;
        });
    }
}
