using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace Basil.Web.OpenApi;

/// <summary>
///     Shared logic for building the Enveloped Response Standard body (see <see cref="Envelope{T}" />),
///     used identically by <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> (wrapping the real
///     response body at runtime) and <see cref="OpenApiExampleExtensions" /> (wrapping a
///     <c>.WithExample</c> payload for the generated OpenAPI docs) — previously two independent copies
///     of the same four methods that had to be kept in sync by hand.
/// </summary>
internal static class EnvelopeBuilder
{
    public static JsonObject Build(int statusCode, string? httpMethod, JsonNode? body, JsonSerializerOptions options)
    {
        var success = statusCode < 400;
        string message;
        JsonNode? data = null;
        JsonNode? meta = null;

        if (success)
        {
            message = DescribeSuccess(httpMethod);
            if (IsPagedShape(body, out var paged))
            {
                meta = JsonSerializer.SerializeToNode(BuildMeta(paged!), options);
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

    public static string DescribeSuccess(string? method)
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

    public static string DescribeError(JsonNode? body, int statusCode)
    {
        if (body is JsonObject obj)
        {
            var message = obj["error"]?.GetValue<string>() ?? obj["detail"]?.GetValue<string>() ??
                obj["title"]?.GetValue<string>();
            if (message is not null) return message;
        }

        return ReasonPhrases.GetReasonPhrase(statusCode);
    }

    /// <summary>Structurally detects the internal paged shape (see <see cref="IPagedResult" />/<see cref="Basil.Web.Routing.PagedResult{T}" />) by an exact 4-key match — no per-route marker needed.</summary>
    public static bool IsPagedShape(JsonNode? body, out JsonObject? paged)
    {
        paged = body as JsonObject;
        return paged is not null && paged.Count == 4 && paged.ContainsKey("page") &&
            paged.ContainsKey("pageSize") && paged.ContainsKey("totalRecords") && paged.ContainsKey("items");
    }

    public static PageMeta BuildMeta(JsonObject paged)
    {
        var page = paged["page"]!.GetValue<int>();
        var pageSize = paged["pageSize"]!.GetValue<int>();
        var totalRecords = paged["totalRecords"]!.GetValue<int>();
        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalRecords / (double)pageSize);
        return new PageMeta(page, pageSize, totalRecords, totalPages);
    }
}
