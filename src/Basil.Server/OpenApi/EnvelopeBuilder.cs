using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Web.Routing.Api;
using Microsoft.AspNetCore.WebUtilities;

namespace Basil.Web.OpenApi;

/// <summary>Shared logic for building the Enveloped Response Standard body (see <see cref="Envelope{T}" />).</summary>
/// <remarks>
///     <para>
///         Both callers use it identically: <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> wraps
///         the real response body at runtime, and <see cref="OpenApiExampleExtensions" /> wraps a
///         <c>.WithExample</c> payload for the generated OpenAPI docs.
///     </para>
/// </remarks>
internal static class EnvelopeBuilder
{
	/// <summary>
	///     Builds the envelope JSON object for a response, splitting a paginated success body into
	///     <c>data</c> and <c>meta</c>.
	/// </summary>
	/// <param name="statusCode">The HTTP status code of the response.</param>
	/// <param name="httpMethod">The HTTP method of the operation, used to phrase the success message.</param>
	/// <param name="body">The serialized response body, or <see langword="null" /> when there is none.</param>
	/// <param name="options">The serializer options used to serialize pagination metadata.</param>
	/// <param name="messageOverride">
	///     A route-supplied success message to use instead of the generic verb-derived one (e.g. "Match
	///     aborted." instead of "Created successfully" for a <c>POST</c> that doesn't create anything).
	///     Ignored on an error response.
	/// </param>
	/// <returns>The envelope JSON object with success, code, message, data, meta, errors, and timestamp members.</returns>
	public static JsonObject Build(int statusCode, string? httpMethod, JsonNode? body, JsonSerializerOptions options,
		string? messageOverride = null)
	{
		var success = statusCode < 400;
		string message;
		JsonNode? data = null;
		JsonNode? meta = null;

		if (success)
		{
			message = messageOverride ?? DescribeSuccess(httpMethod);
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

	/// <summary>
	///     Returns the envelope's success message phrased from the HTTP method.
	/// </summary>
	/// <param name="method">The HTTP method of the operation, or <see langword="null" /> to get the retrieval message.</param>
	/// <returns>The success message, such as "Created successfully" for POST.</returns>
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

	/// <summary>
	///     Returns the envelope's error message, preferring a message carried in the error body.
	/// </summary>
	/// <param name="body">The serialized error response body, or <see langword="null" /> when there is none.</param>
	/// <param name="statusCode">The HTTP status code of the error response.</param>
	/// <returns>
	///     The error body's error, detail, or title member, or the standard HTTP reason phrase for
	///     <paramref name="statusCode" />.
	/// </returns>
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

	/// <summary>
	///     Structurally detects the internal paged shape (see <see cref="IPagedResult" />/
	///     <see cref="PagedResult{T}" />) by an exact 4-key match, no per-route marker needed.
	/// </summary>
	/// <param name="body">The serialized response body to inspect, or <see langword="null" />.</param>
	/// <param name="paged">
	///     When this method returns <see langword="true" />, contains <paramref name="body" /> as a
	///     <see cref="JsonObject" />; otherwise, <see langword="null" />.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="body" /> is an object with exactly the keys page, pageSize,
	///     totalRecords, and items; otherwise, <see langword="false" />.
	/// </returns>
	public static bool IsPagedShape(JsonNode? body, out JsonObject? paged)
	{
		paged = body as JsonObject;
		return paged is not null && paged.Count == 4 && paged.ContainsKey("page") &&
		       paged.ContainsKey("pageSize") && paged.ContainsKey("totalRecords") && paged.ContainsKey("items");
	}

	/// <summary>
	///     Computes pagination metadata from a paged response body.
	/// </summary>
	/// <param name="paged">The serialized paged response body.</param>
	/// <returns>
	///     A <see cref="PageMeta" /> carrying the body's page, pageSize, and totalRecords members plus the computed
	///     totalPages.
	/// </returns>
	public static PageMeta BuildMeta(JsonObject paged)
	{
		var page = paged["page"]!.GetValue<int>();
		var pageSize = paged["pageSize"]!.GetValue<int>();
		var totalRecords = paged["totalRecords"]!.GetValue<int>();
		var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalRecords / (double)pageSize);
		return new PageMeta(page, pageSize, totalRecords, totalPages);
	}
}