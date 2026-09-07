using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Formats;
using Basil.Web.OpenApi;
using Basil.Web.Routing.Api;

namespace Basil.Web.Middleware;

/// <summary>
///     Wraps every JSON body on the <c>basilapi</c> OpenAPI group in the Enveloped Response Standard (see
///     <see cref="Envelope{T}" />). Registered after <c>UseAuthorization</c> in <c>Program.cs</c>.
/// </summary>
/// <remarks>
///     Skips a request entirely (no buffering, no rewriting) when the matched endpoint isn't tagged
///     `basilapi` (every other host group: bancho/osu-web/beatmap-assets/avatar) or is a live SSE route
///     (any route whose path carries a literal `live` segment, e.g. `GET /matches/{id}/live`), which
///     never produces a plain-JSON body regardless of the request.
///     Every SSE route on this host is a dedicated, unconditionally-SSE `.../live` path, so no route
///     branches on the `Accept` header anymore; the route pattern's `live` segment alone decides
///     skip-vs.-wrap (see <see cref="LiveSseRoutes.IsSseRoute" />). A file download's `Content-Type`
///     is never "json", so it passes through unwrapped, with no separate marker needed. `HEAD` requests
///     and responses that must never carry a body under the HTTP spec (`304 Not Modified`,
///     `205 Reset Content`) are also passed through unwrapped rather than enveloped, regardless of
///     their (absent) `Content-Type`.
/// </remarks>
public sealed class EnvelopeMiddleware(RequestDelegate next)
{
	/// <summary>
	///     The <see cref="HttpContext.Items" /> key a route handler sets to override the envelope's
	///     generic verb-derived success message (e.g. "Created successfully") with one that actually
	///     describes what the endpoint did.
	/// </summary>
	public const string EnvelopeMessageKey = "EnvelopeMessage";

	/// <summary>
	///     JSON options used to parse and reserialize the buffered envelope — the shared instance
	///     every response body serialization uses, so re-serializing the envelope here doesn't
	///     re-escape a value (e.g. a literal <c>+</c>) differently than the route handler that
	///     originally wrote it.
	/// </summary>
	private static readonly JsonSerializerOptions JsonWebOptions = BasilJsonOptions.Instance;

	/// <summary>Envelopes the current response body when the matched endpoint belongs to the basilapi group.</summary>
	/// <remarks>
	///     The response is buffered in memory, then parsed and wrapped in an
	///     <see cref="Envelope{T}" /> built from the status code, request method, and body. A
	///     <c>204 No Content</c> status becomes <c>200 OK</c> with a real envelope body. Non-JSON
	///     content types (file downloads) and responses that must not carry a body (<c>304</c>,
	///     <c>205</c>) are passed through unwrapped.
	/// </remarks>
	/// <param name="context">The HTTP context whose response is buffered, inspected, and rewritten.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var endpoint = context.GetEndpoint();
		var groupName = endpoint?.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName;
		var isAlwaysSse = endpoint is RouteEndpoint { RoutePattern.RawText: { } raw } &&
		                  LiveSseRoutes.IsSseRoute(raw);
		// A route that failed to match at all (404, 405, or a route-constraint miss e.g. an
		// overflowing {id:int}) resolves no endpoint whatsoever — endpoint is null, distinct from a
		// matched endpoint that simply isn't part of any of our named groups (the framework's own
		// /openapi/*.json, Scalar UI, etc., which must stay unwrapped). Falling straight to "skip"
		// on a null endpoint left every unmatched-route response an unwrapped, empty body on exactly
		// the host whose contract promises an envelope on every response. The api. host is
		// identified by its "api." subdomain prefix (the same convention every host group in
		// BanchoHostGroups.MapAll uses), not by re-deriving the configured domain here.
		var isUnmatchedOnApiHost = endpoint is null &&
		                           context.Request.Host.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase);
		if ((groupName != "basilapi" && !isUnmatchedOnApiHost) || isAlwaysSse ||
		    HttpMethods.IsHead(context.Request.Method))
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

		buffer.Seek(0, SeekOrigin.Begin);
		if (context.Response.StatusCode is StatusCodes.Status304NotModified or StatusCodes.Status205ResetContent)
		{
			// Safe: Results.File() writes no body for 304 responses, leaving the buffer empty.
			// CopyToAsync() therefore performs no writes to the original response stream.
			await buffer.CopyToAsync(originalBody);
			return;
		}

		var contentType = context.Response.ContentType;
		var isJsonOrEmpty = string.IsNullOrEmpty(contentType) ||
		                    contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

		if (!isJsonOrEmpty)
		{
			await buffer.CopyToAsync(originalBody);
			return;
		}

		if (context.Response.StatusCode == StatusCodes.Status204NoContent)
			context.Response.StatusCode = StatusCodes.Status200OK;

		var statusCode = context.Response.StatusCode;
		var body = buffer.Length == 0 ? null : await JsonNode.ParseAsync(buffer);
		var messageOverride = context.Items[EnvelopeMessageKey] as string;
		var envelope = EnvelopeBuilder.Build(statusCode, context.Request.Method, body, JsonWebOptions,
			messageOverride);

		context.Response.ContentType = "application/json; charset=utf-8";
		var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWebOptions);
		context.Response.ContentLength = bytes.Length;
		await originalBody.WriteAsync(bytes);
	}
}