using Basil.Web.Middleware;
using Basil.Web.Routing;
using Microsoft.AspNetCore.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Rewrites every declared JSON response schema on the `basilapi` document from the bare handler
///     type `T` to <see cref="Envelope{T}" /> (or, for a paginated `PagedResult{TItem}` response,
///     <see cref="Envelope{T}" /> of <c>IReadOnlyList&lt;TItem&gt;</c>, matching what
///     <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> actually produces at runtime — the paging
///     fields move to <c>meta</c>, only the items array stays in <c>data</c>). Reuses
///     <see cref="OpenApiOperationTransformerContext.GetOrCreateSchemaAsync" /> against the closed
///     <c>Envelope&lt;T&gt;</c> generic type rather than hand-building the wrapper shape, so
///     required/nullable annotations fall straight out of that record's own C# nullability. Routes
///     carrying <see cref="SseEndpointMarker" /> are skipped — every SSE payload on this host is a raw,
///     un-enveloped JSON line by design (see the marker's own doc comment).
/// </summary>
internal static class EnvelopeSchemaTransformer
{
    public static void AddEnvelopeSchemaTransformer(this OpenApiOptions options)
    {
        options.AddOperationTransformer(async (operation, context, cancellationToken) =>
        {
            var isSse = context.Description.ActionDescriptor.EndpointMetadata
                .OfType<SseEndpointMarker>().Any();
            if (isSse) return;

            foreach (var responseType in context.Description.SupportedResponseTypes)
            {
                var declaredType = responseType.Type;
                if (declaredType is null || declaredType == typeof(void)) continue;

                if (operation.Responses is null ||
                    !operation.Responses.TryGetValue(responseType.StatusCode.ToString(), out var response) ||
                    response?.Content is null)
                    continue;
                if (!response.Content.TryGetValue("application/json", out var mediaType))
                    continue;

                var envelopeType = declaredType.IsGenericType &&
                    declaredType.GetGenericTypeDefinition() == typeof(PagedResult<>)
                        ? typeof(Envelope<>).MakeGenericType(
                            typeof(IReadOnlyList<>).MakeGenericType(declaredType.GetGenericArguments()[0]))
                        : typeof(Envelope<>).MakeGenericType(declaredType);

                mediaType.Schema = await context.GetOrCreateSchemaAsync(envelopeType, null, cancellationToken);
            }
        });
    }
}
