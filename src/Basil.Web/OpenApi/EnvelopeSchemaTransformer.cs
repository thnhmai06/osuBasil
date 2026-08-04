using System.Text.Json.Nodes;
using Basil.Web.Middleware;
using Basil.Web.Routing.Api;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Rewrites every JSON response documented by basilapi so its OpenAPI schema matches the
///     actual <see cref="Envelope{T}" /> shape produced by <see cref="EnvelopeMiddleware" /> at
///     runtime.
/// </summary>
/// <remarks>
///     <para>
///         Every JSON response is normalized to the canonical <c>application/json</c> media type,
///         regardless of whether it was originally declared as
///         <c>application/json</c>, <c>application/problem+json</c>, or (for certain security
///         responses) had no declared content at all.
///     </para>
///     <para>
///         Error responses (HTTP status codes &gt;= 400) always declare <c>data</c> as
///         <c>null</c>-only, matching <c>EnvelopeBuilder.Build</c>'s runtime guarantee.
///     </para>
///     <para>
///         Nullable envelope members (<c>data</c>, <c>meta</c>, and <c>errors</c>) use the
///         framework's standard nullable schema pattern
///         (<c>oneOf: [{ type: null }, ...]</c>) instead of appearing as required, non-null
///         properties.
///     </para>
///     <para>
///         Successful responses reuse the framework-generated schema for the originally declared
///         response type, preserving shared component references (<c>$ref</c>) instead of
///         generating new closed <c>Envelope&lt;T&gt;</c> schemas for every operation.
///     </para>
///     <para>
///         The six envelope members whose schemas never vary
///         (<c>success</c>, <c>code</c>, <c>message</c>, <c>meta</c>,
///         <c>errors</c>, and <c>timestamp</c>) are defined once in a shared
///         <c>Envelope</c> component and combined with an operation-specific
///         <c>data</c> property via <c>allOf</c>. Shared <c>PageMeta</c> and
///         <c>FieldError</c> components are likewise reused instead of being
///         inlined repeatedly.
///     </para>
///     <para>
///         Error responses without an explicit example receive a minimal synthesized envelope,
///         while successful responses are left unchanged because representative payloads cannot
///         be inferred safely.
///     </para>
///     <para>
///         For Server-Sent Events endpoints (routes containing a literal <c>live</c> path
///         segment), only the successful 2xx response is excluded from rewriting because it is
///         the raw event stream by design. Any synchronous error returned before the stream
///         opens is transformed like every other JSON error response.
///     </para>
///     <para>
///         The rewritten schemas are what Scalar and generated client SDKs render for every
///         basilapi response, so the declared shape stays in lockstep with the runtime envelope.
///     </para>
///     <para>
///         Runs after route-level OpenAPI transformers: a route's <c>.WithLink(...)</c> or
///         <c>.WithExample(...)</c> calls execute before this operation transformer, and the links
///         and examples they attached to the original response are carried over when this
///         transformer replaces it.
///     </para>
/// </remarks>
internal static class EnvelopeSchemaTransformer
{
	private const string EnvelopeSchemaId = "Envelope";
	private const string PageMetaSchemaId = "PageMeta";
	private const string FieldErrorSchemaId = "FieldError";

	/// <summary>
	///     Registers the operation and document transformers that rewrite every non-SSE JSON response
	///     schema to the real <see cref="Envelope{T}" /> shape.
	/// </summary>
	/// <param name="options">The OpenAPI options to register the transformers on.</param>
	public static void AddEnvelopeSchemaTransformer(this OpenApiOptions options)
	{
		options.AddOperationTransformer((operation, context, _) =>
		{
			if (operation.Responses is null || context.Document is null) return Task.CompletedTask;

			// An SSE route's 2xx is the actual live stream, a raw unenveloped event payload by design.
			// Any other status on that same route is a genuine synchronous JSON error returned before
			// a stream ever opens (e.g., 409 "not live", 404 out-of-range, 400 no-stream-to-expose;
			// see LiveSseRoutes.SseError/NotLive), and it gets enveloped exactly like every other route's error response.
			var isSse = LiveSseRoutes.IsSseRoute(context.Description.RelativePath);

			EnsureSharedComponentsRegistered(context.Document);

			foreach (var (statusKey, existingResponse) in operation.Responses.ToList())
			{
				if (existingResponse is null || !int.TryParse(statusKey, out var statusCode)) continue;
				var isError = statusCode >= 400;
				if (isSse && !isError) continue;

				var jsonMediaType = FindJsonMediaType(existingResponse);
				// A bare 401 (added by SecuritySchemeTransformers) has no content at all yet. Every
				// other JSON media type response already has *some* media type entry to reuse or replace.
				var hasNoContent = existingResponse.Content is null or { Count: 0 };
				if (jsonMediaType is null && !(isError && hasNoContent)) continue;

				IOpenApiSchema dataSchema;
				if (isError)
				{
					dataSchema = new OpenApiSchema { Type = JsonSchemaType.Null };
				}
				else
				{
					var declaredType = context.Description.SupportedResponseTypes
						.FirstOrDefault(r => r.StatusCode == statusCode)?.Type;
					if (declaredType is null || declaredType == typeof(void) || jsonMediaType?.Schema is null)
						continue;

					// Reuse the schema the framework's own default per-operation generation already
					// built for this exact declared type. That pipeline is what actually promotes a
					// repeatedly used/complex type to a named `$ref`-able component; calling
					// GetOrCreateSchemaAsync again from inside an operation transformer does NOT
					// participate in that same promotion and silently re-inlines everything instead
					// (confirmed by inspecting the generated document, which showed this was the
					// actual cause of the duplication this transformer is meant to avoid).
					var isPaged = declaredType.IsGenericType &&
					              declaredType.GetGenericTypeDefinition() == typeof(PagedResult<>);
					if (isPaged && jsonMediaType.Schema.Properties is { } pagedProps &&
					    pagedProps.TryGetValue("items", out var itemsSchema))
						dataSchema = NullableWrap(itemsSchema);
					else
						dataSchema = NullableWrap(jsonMediaType.Schema);
				}

				var envelopeSchema = BuildEnvelopeObjectSchema(context.Document, dataSchema);
				var newMediaType = new OpenApiMediaType { Schema = envelopeSchema, Example = jsonMediaType?.Example };

				if (isError && newMediaType.Example is null)
					newMediaType.Example = BuildSyntheticErrorExample(statusCode, existingResponse.Description);

				// Every JSON media type response ends up under this one canonical key: whether it previously
				// lived under `application/json`, `application/problem+json` (folded away below, since
				// EnvelopeMiddleware always rewrites the real Content-Type to application/json for a
				// basilapi response and never actually serves problem+json), or had no content at all
				// (a bare 401).
				operation.Responses[statusKey] = new OpenApiResponse
				{
					Description = existingResponse.Description,
					Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = newMediaType },
					// Preserved from whatever a route-level transformer already attached to the
					// pre-replacement response object, e.g., LinkExtensions.WithLink, which runs before
					// this one (as does OpenApiExampleExtensions.WithExample, per its own doc comment).
					Links = existingResponse.Links
				};
			}

			return Task.CompletedTask;
		});

		options.AddDocumentTransformer((document, _, _) =>
		{
			RemoveOrphanSchemas(document);
			return Task.CompletedTask;
		});
	}

	/// <summary>
	///     Finds the response's JSON media type entry, preferring <c>application/json</c> over
	///     <c>application/problem+json</c>, since the latter is normalized to the former by
	///     <see cref="EnvelopeMiddleware" />.
	/// </summary>
	/// <param name="response">The OpenAPI response to inspect.</param>
	/// <returns>The matching media type entry, or <see langword="null" /> when the response has no JSON content.</returns>
	private static OpenApiMediaType? FindJsonMediaType(IOpenApiResponse response)
	{
		return response.Content switch
		{
			null => null,
			_ when response.Content.TryGetValue("application/json", out var json) => json,
			_ when response.Content.TryGetValue("application/problem+json", out var problem) => problem,
			_ => null
		};
	}

	/// <summary>
	///     Registers the shared PageMeta, FieldError, and Envelope components once each, when not already
	///     present in the document.
	/// </summary>
	/// <param name="document">The OpenAPI document whose components the schemas are registered on.</param>
	private static void EnsureSharedComponentsRegistered(OpenApiDocument document)
	{
		document.Components ??= new OpenApiComponents();
		document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

		document.Components.Schemas.TryAdd(PageMetaSchemaId, new OpenApiSchema
		{
			Type = JsonSchemaType.Object,
			Required = new HashSet<string> { "page", "pageSize", "totalRecords", "totalPages" },
			Properties = new Dictionary<string, IOpenApiSchema>
			{
				["page"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
				["pageSize"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
				["totalRecords"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
				["totalPages"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" }
			}
		});

		document.Components.Schemas.TryAdd(FieldErrorSchemaId, new OpenApiSchema
		{
			Type = JsonSchemaType.Object,
			Required = new HashSet<string> { "field", "message" },
			Properties = new Dictionary<string, IOpenApiSchema>
			{
				["field"] = NullableWrap(new OpenApiSchema { Type = JsonSchemaType.String }),
				["message"] = new OpenApiSchema { Type = JsonSchemaType.String }
			}
		});

		// Every field except `data` (whose type varies per operation). Registered once and combined
		// via `allOf` below instead of re-declaring all six fields' schemas on every single response,
		// which is what made the previous per-operation-inlined version thousands of lines longer
		// than it needed to be.
		document.Components.Schemas.TryAdd(EnvelopeSchemaId, new OpenApiSchema
		{
			Type = JsonSchemaType.Object,
			Required = new HashSet<string> { "success", "code", "message", "meta", "errors", "timestamp" },
			Properties = new Dictionary<string, IOpenApiSchema>
			{
				["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
				["code"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
				["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
				["meta"] = NullableWrap(new OpenApiSchemaReference(PageMetaSchemaId, document)),
				["errors"] = NullableWrap(new OpenApiSchema
				{
					Type = JsonSchemaType.Array,
					Items = new OpenApiSchemaReference(FieldErrorSchemaId, document)
				}),
				["timestamp"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
			}
		});
	}

	/// <summary>
	///     Combines the shared Envelope component with a per-operation data schema via <c>allOf</c>.
	/// </summary>
	/// <param name="document">The OpenAPI document the envelope reference is resolved against.</param>
	/// <param name="dataSchema">The schema for the response's data member.</param>
	/// <returns>The combined envelope schema object.</returns>
	private static OpenApiSchema BuildEnvelopeObjectSchema(OpenApiDocument document, IOpenApiSchema dataSchema)
	{
		return new OpenApiSchema
		{
			AllOf =
			[
				new OpenApiSchemaReference(EnvelopeSchemaId, document),
				new OpenApiSchema
				{
					Type = JsonSchemaType.Object,
					Required = new HashSet<string> { "data" },
					Properties = new Dictionary<string, IOpenApiSchema> { ["data"] = dataSchema }
				}
			]
		};
	}

	/// <summary>
	///     The same ` oneOf: [{type: null}, ...]` pattern, the framework's own nullable-property schemas
	///     already use elsewhere in this document (e.g., a PATCH request's optional fields), works
	///     uniformly whether <paramref name="schema" /> is a `$ref` or an inline object/array schema.
	/// </summary>
	private static OpenApiSchema NullableWrap(IOpenApiSchema schema)
	{
		return new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, schema] };
	}

	/// <summary>
	///     Builds a minimal example envelope for an error response that has no declared example.
	/// </summary>
	/// <param name="statusCode">The HTTP status code of the error response.</param>
	/// <param name="description">The response description to use as the message, or <see langword="null" />.</param>
	/// <returns>The synthesized example envelope as a JSON object.</returns>
	private static JsonObject BuildSyntheticErrorExample(int statusCode, string? description)
	{
		return new JsonObject
		{
			["success"] = false,
			["code"] = statusCode,
			["message"] = description ?? "Error",
			["data"] = null,
			["meta"] = null,
			["errors"] = null,
			["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
		};
	}

	/// <summary>
	///     Removes any `components.schemas` entry nothing in the document actually `$ref`s, e.g., the
	///     `PagedResult&lt;T&gt;` schemas the framework's default response-type discovery registers before
	///     this transformer replaces the actual response schema with the paginated `data`/`meta` split
	///     above, or a domain type no longer referenced by any operation.
	///     Runs as a document transformer, so it sees the fully assembled document after every operation
	///     transformer (including this file's own) has already run.
	/// </summary>
	private static void RemoveOrphanSchemas(OpenApiDocument document)
	{
		if (document.Components?.Schemas is not { } schemas || schemas.Count == 0) return;

		using var stringWriter = new StringWriter();
		var jsonWriter = new OpenApiJsonWriter(stringWriter);
		document.SerializeAsV31(jsonWriter);
		var serialized = stringWriter.ToString();

		var orphanNames = schemas.Keys
			.Where(name => !serialized.Contains($"\"#/components/schemas/{name}\"", StringComparison.Ordinal))
			.ToList();

		foreach (var name in orphanNames) schemas.Remove(name);
	}
}