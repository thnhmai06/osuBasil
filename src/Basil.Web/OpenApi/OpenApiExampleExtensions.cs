using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;

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
///     enveloped.
/// </summary>
internal static class OpenApiExampleExtensions
{
    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder WithExample(this RouteHandlerBuilder builder, int statusCode, object example)
    {
        return builder.AddOpenApiOperationTransformer((operation, context, _) =>
        {
            if (operation.Responses.TryGetValue(statusCode.ToString(), out var response) &&
                response?.Content.TryGetValue("application/json", out var mediaType) == true)
            {
                mediaType!.Example = context.DocumentName == "basilapi"
                    ? BuildEnvelope(statusCode, context.Description.HttpMethod, example)
                    : JsonSerializer.SerializeToNode(example, JsonWebOptions);
            }

            return Task.CompletedTask;
        });
    }

    private static JsonNode BuildEnvelope(int statusCode, string? httpMethod, object example)
    {
        var success = statusCode < 400;
        var body = JsonSerializer.SerializeToNode(example, JsonWebOptions);

        string message;
        JsonNode? data = null;
        JsonNode? meta = null;

        if (success)
        {
            message = DescribeSuccess(httpMethod);
            if (IsPagedShape(body, out var paged))
            {
                meta = JsonSerializer.SerializeToNode(BuildMeta(paged!), JsonWebOptions);
                data = paged!["items"];
                paged.Remove("items");
            }
            else
            {
                data = body;
            }
        }
        else
        {
            message = DescribeError(body, statusCode);
        }

        return new JsonObject
        {
            ["success"] = success,
            ["code"] = statusCode,
            ["message"] = message,
            ["data"] = data,
            ["meta"] = meta,
            ["errors"] = null,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static string DescribeSuccess(string? method)
    {
        return method switch
        {
            "POST" => "Created successfully",
            "PUT" => "Replaced successfully",
            "PATCH" => "Updated successfully",
            "DELETE" => "Deleted successfully",
            _ => "Retrieval successful"
        };
    }

    private static string DescribeError(JsonNode? body, int statusCode)
    {
        if (body is JsonObject obj)
        {
            var message = obj["error"]?.GetValue<string>() ?? obj["detail"]?.GetValue<string>() ??
                obj["title"]?.GetValue<string>();
            if (message is not null) return message;
        }

        return ReasonPhrases.GetReasonPhrase(statusCode);
    }

    private static bool IsPagedShape(JsonNode? body, out JsonObject? paged)
    {
        paged = body as JsonObject;
        return paged is not null && paged.Count == 4 && paged.ContainsKey("page") &&
            paged.ContainsKey("pageSize") && paged.ContainsKey("totalRecords") && paged.ContainsKey("items");
    }

    private static PageMeta BuildMeta(JsonObject paged)
    {
        var page = paged["page"]!.GetValue<int>();
        var pageSize = paged["pageSize"]!.GetValue<int>();
        var totalRecords = paged["totalRecords"]!.GetValue<int>();
        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalRecords / (double)pageSize);
        return new PageMeta(page, pageSize, totalRecords, totalPages);
    }
}
