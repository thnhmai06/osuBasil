using System.Text.Json.Nodes;
using Basil.Web.Middleware;
using Basil.Web.Routing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Rewrites every basilapi JSON response's declared schema to the real <see cref="Envelope{T}" />
///     shape, matching what <see cref="EnvelopeMiddleware" /> actually produces at runtime:
///     <list type="bullet">
///         <item>
///             Every status code carrying a JSON-ish content type (`application/json` AND
///             `application/problem+json` — `.ProducesProblem(...)` responses were previously never
///             touched at all, since only the exact `application/json` key was checked) gets rewritten,
///             including a bare `401` with no declared type or content (added by
///             <see cref="SecuritySchemeTransformers" />, which must run before this transformer per its
///             registration order in <c>Program.cs</c>) — every JSON-ish response ends up under the one
///             canonical `application/json` key, since that's the only content type
///             <see cref="EnvelopeMiddleware" /> ever actually emits.
///         </item>
///         <item>
///             `data` is `null`-only for every &gt;=400 response (matching
///             <c>EnvelopeBuilder.Build</c>'s guarantee — an error body's `data` is never anything else),
///             not the originally-declared `ErrorResponse`/`ProblemDetails` shape.
///         </item>
///         <item>
///             `data`/`meta`/`errors` are properly nullable (`oneOf: [{type: null}, ...]`, the same
///             pattern the framework's own nullable-property schemas already use elsewhere in this
///             document) — previously `meta`/`data` on a non-paginated success response showed as
///             required-non-null even though they're `null` whenever unused.
///         </item>
///         <item>
///             `data`'s schema for a success response is obtained via
///             <see cref="OpenApiOperationTransformerContext.GetOrCreateSchemaAsync" /> against the
///             *original* declared type (not a synthesized closed <c>Envelope&lt;T&gt;</c> generic) — this
///             is the same call the framework's own default response-schema generation uses, so it
///             correctly reuses an already-registered named component via `$ref` instead of re-inlining
///             the whole nested object graph on every single operation (the previous
///             <c>GetOrCreateSchemaAsync(typeof(Envelope&lt;&gt;).MakeGenericType(...))</c> approach
///             silently broke that reuse for everything nested one level inside the wrapper).
///         </item>
///         <item>
///             The envelope's six non-`data` fields (`success`/`code`/`message`/`meta`/`errors`/
///             `timestamp`) are a single shared <c>Envelope</c> component combined via `allOf` with a
///             per-operation `{ data: ... }` object, instead of re-declaring all seven properties inline
///             on every response — `meta`/`errors` further reference shared, once-registered
///             <c>PageMeta</c>/<c>FieldError</c> components instead of re-inlining their own shape.
///         </item>
///         <item>
///             Any response left without an example after all of the above gets a minimal synthesized
///             one (`{success,code,message,data:null,...}` for an error; nothing invented for a success
///             body, since real sample data can't be guessed) — closes most of the "no `.WithExample`"
///             gap on error responses as a side effect of visiting every one of them anyway.
///         </item>
///     </list>
///     On a route carrying <see cref="SseEndpointMarker" />, only the 2xx response is left alone — that's
///     the actual live stream, a raw un-enveloped JSON line by design (see the marker's own doc comment).
///     Any other status on that same route (409/404/400 returned synchronously before a stream opens) is
///     enveloped exactly like every other route's error response.
/// </summary>
internal static class EnvelopeSchemaTransformer
{
    private const string EnvelopeSchemaId = "Envelope";
    private const string PageMetaSchemaId = "PageMeta";
    private const string FieldErrorSchemaId = "FieldError";

    public static void AddEnvelopeSchemaTransformer(this OpenApiOptions options)
    {
        options.AddOperationTransformer((operation, context, _) =>
        {
            if (operation.Responses is null) return Task.CompletedTask;
            if (context.Document is null) return Task.CompletedTask;

            // An SSE route's 2xx is the actual live stream — a raw, un-enveloped event payload by
            // design (see this type's own doc comment) — but any other status on that same route is a
            // genuine synchronous JSON error returned *before* a stream ever opens (e.g. 409 "not
            // live", 404 out-of-range, 400 no-stream-to-expose; see LiveSseRoutes.SseError/NotLive) and
            // gets enveloped exactly like every other route's error response.
            var isSse = context.Description.ActionDescriptor.EndpointMetadata
                .OfType<SseEndpointMarker>().Any();

            EnsureSharedComponentsRegistered(context.Document);

            foreach (var (statusKey, existingResponse) in operation.Responses.ToList())
            {
                if (existingResponse is null || !int.TryParse(statusKey, out var statusCode)) continue;
                if (isSse && statusCode < 400) continue;

                var jsonMediaType = FindJsonMediaType(existingResponse);
                // A bare 401 (added by SecuritySchemeTransformers) has no content at all yet — every
                // other JSON-ish response already has *some* media type entry to reuse/replace.
                var hasNoContent = existingResponse.Content is null or { Count: 0 };
                if (jsonMediaType is null && !(statusCode >= 400 && hasNoContent)) continue;

                IOpenApiSchema dataSchema;
                if (statusCode >= 400)
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
                    // built for this exact declared type — that pipeline is what actually promotes a
                    // repeatedly-used/complex type to a named `$ref`-able component; calling
                    // GetOrCreateSchemaAsync again from inside an operation transformer does NOT
                    // participate in that same promotion and silently re-inlines everything instead
                    // (confirmed by inspecting the generated document — this was the actual cause of the
                    // duplication this transformer is meant to avoid).
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

                if (statusCode >= 400 && newMediaType.Example is null)
                    newMediaType.Example = BuildSyntheticErrorExample(statusCode, existingResponse.Description);

                // Every JSON-ish response — whether it previously lived under `application/json`,
                // `application/problem+json` (folded away below, since EnvelopeMiddleware always
                // rewrites the real Content-Type to application/json for a basilapi response and never
                // actually serves problem+json), or had no content at all (a bare 401) — ends up under
                // this one canonical key.
                operation.Responses[statusKey] = new OpenApiResponse
                {
                    Description = existingResponse.Description,
                    Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = newMediaType },
                    // Carried over from whatever a route-level transformer (e.g. LinkExtensions.WithLink,
                    // which — like OpenApiExampleExtensions.WithExample — runs before this one, per its
                    // own doc comment) already attached to the pre-replacement response object.
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

    private static OpenApiMediaType? FindJsonMediaType(IOpenApiResponse response)
    {
        if (response.Content is null) return null;
        if (response.Content.TryGetValue("application/json", out var json)) return json;
        if (response.Content.TryGetValue("application/problem+json", out var problem)) return problem;
        return null;
    }

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

        // Every field except `data` (whose type varies per operation) — registered once and combined
        // via `allOf` below instead of re-declaring all six fields' schemas on every single response,
        // which is what made the previous per-operation-inlined version thousands of lines longer than
        // it needed to be.
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
    ///     Same `oneOf: [{type: null}, ...]` pattern the framework's own nullable-property schemas
    ///     already use elsewhere in this document (e.g. a PATCH request's optional fields) — works
    ///     uniformly whether <paramref name="schema" /> is a `$ref` or an inline object/array schema.
    /// </summary>
    private static IOpenApiSchema NullableWrap(IOpenApiSchema schema)
    {
        return new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, schema] };
    }

    private static JsonNode BuildSyntheticErrorExample(int statusCode, string? description)
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
    ///     Removes any `components.schemas` entry nothing in the document actually `$ref`s — e.g. the
    ///     `PagedResult&lt;T&gt;` schemas the framework's default response-type discovery registers before
    ///     this transformer replaces the actual response schema with the paginated `data`/`meta` split
    ///     above, or a domain type (like the bare `User` record) that no route documents directly anymore.
    ///     Runs as a document transformer, so it sees the fully-assembled document after every operation
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