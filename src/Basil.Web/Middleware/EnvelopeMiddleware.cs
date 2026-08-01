using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Web.OpenApi;

namespace Basil.Web.Middleware;

/// <summary>
///     Wraps every JSON body on the `basilapi` OpenAPI group in the Enveloped Response Standard
///     (see <see cref="Envelope{T}" />). Registered after <c>UseAuthorization</c> in <c>Program.cs</c>.
///     Skips a request entirely (no buffering, no rewriting) when the matched endpoint isn't tagged
///     `basilapi` (every other host group: bancho/osu-web/beatmap-assets/avatar) or carries
///     <see cref="SseEndpointMarker" /> (an always-SSE route, e.g. `GET /matches/{id}/live`, that never
///     produces a plain-JSON body regardless of the request). Every SSE route on this host is now a
///     dedicated, unconditionally-SSE `.../live` path, so no route branches on the `Accept` header
///     anymore, and the marker check alone is sufficient to decide skip-vs-wrap. Buffering a live push
///     stream until the handler completes would silently turn it into one that never delivers a single
///     event until the connection closes. A file download's `Content-Type` is never "json" and is
///     passed through unwrapped for the same structural reason, with no separate marker needed.
/// </summary>
public sealed class EnvelopeMiddleware(RequestDelegate next)
{
	/// <summary>JSON options using web defaults, used to parse and reserialize the buffered envelope.</summary>
	private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

	/// <summary>Envelopes the current response body when the matched endpoint belongs to the basilapi group.</summary>
	/// <remarks>
	///     The response is buffered in memory, then parsed and wrapped in an
	///     <see cref="Envelope{T}" /> built from the status code, request method, and body. A
	///     <c>204 No Content</c> status becomes <c>200 OK</c> with a real envelope body, and non-JSON
	///     content types (file downloads) are passed through unwrapped.
	/// </remarks>
	/// <param name="context">The HTTP context whose response is buffered, inspected, and rewritten.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var endpoint = context.GetEndpoint();
		var groupName = endpoint?.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName;
		var isAlwaysSse = endpoint?.Metadata.GetMetadata<SseEndpointMarker>() is not null;
		if (groupName != "basilapi" || isAlwaysSse)
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
		var body = buffer.Length == 0 ? null : JsonNode.Parse(buffer);
		var envelope = EnvelopeBuilder.Build(statusCode, context.Request.Method, body, JsonWebOptions);

		context.Response.ContentType = "application/json; charset=utf-8";
		var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWebOptions);
		context.Response.ContentLength = bytes.Length;
		await originalBody.WriteAsync(bytes);
	}
}