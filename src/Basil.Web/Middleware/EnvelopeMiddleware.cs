using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using Basil.Web.OpenApi;
using Basil.Web.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Basil.Web.Middleware;

/// <summary>
///     Wraps every JSON body on the `basilapi` OpenAPI group in the Enveloped Response Standard
///     (see <see cref="Envelope{T}" />). Registered after <c>UseAuthorization</c> in <c>Program.cs</c>.
///     Skips a request entirely — no buffering, no rewriting — when the matched endpoint isn't tagged
///     `basilapi` (every other host group: bancho/osu-web/beatmap-assets/avatar), carries
///     <see cref="SseEndpointMarker" /> (an always-SSE route, e.g. `GET /matches/{id}/live`, that never
///     produces a plain-JSON body regardless of the request), or the request itself asks for
///     `Accept: text/event-stream` on one of the several content-negotiated routes (e.g.
///     `GET /matches/{id}/hosts`) that serve either JSON or SSE depending on that header — mirrors each
///     such route's own `WantsSse` check, so the request-time decision here always matches what the
///     handler is about to do. Buffering a live push stream until the handler completes would silently
///     turn it into one that never delivers a single event until the connection closes. A file
///     download's `Content-Type` is never "json" and is passed through unwrapped for the same
///     structural reason, with no separate marker needed.
/// </summary>
public sealed class EnvelopeMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var groupName = endpoint?.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName;
        var isAlwaysSse = endpoint?.Metadata.GetMetadata<SseEndpointMarker>() is not null;
        var wantsSse = context.Request.Headers.Accept.Any(a => a?.Contains("text/event-stream",
            StringComparison.OrdinalIgnoreCase) == true);
        if (groupName != "basilapi" || isAlwaysSse || wantsSse)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var contentType = context.Response.ContentType;
        var isJsonOrEmpty = string.IsNullOrEmpty(contentType) ||
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

        buffer.Seek(0, SeekOrigin.Begin);
        if (!isJsonOrEmpty)
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        if (context.Response.StatusCode == StatusCodes.Status204NoContent)
            context.Response.StatusCode = StatusCodes.Status200OK;

        var statusCode = context.Response.StatusCode;
        var success = statusCode < 400;
        var body = buffer.Length == 0 ? null : JsonNode.Parse(buffer);

        string message;
        JsonNode? data = null;
        PageMeta? meta = null;

        if (success)
        {
            message = DescribeSuccess(context.Request.Method);
            if (IsPagedShape(body, out var paged))
            {
                meta = BuildMeta(paged!);
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

        var envelope = new JsonObject
        {
            ["success"] = success,
            ["code"] = statusCode,
            ["message"] = message,
            ["data"] = data,
            ["meta"] = meta is null ? null : JsonSerializer.SerializeToNode(meta, JsonWebOptions),
            ["errors"] = null,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWebOptions);
        context.Response.ContentLength = bytes.Length;
        await originalBody.WriteAsync(bytes);
    }

    private static string DescribeSuccess(string method)
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

    /// <summary>
    ///     Structurally detects the internal paged shape (see <see cref="IPagedResult" />/
    ///     <see cref="PagedResult{T}" />) by an exact 4-key match — no per-route marker needed.
    /// </summary>
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
