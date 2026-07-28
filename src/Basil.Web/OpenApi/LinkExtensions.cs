using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Attaches an OpenAPI `links` entry to a route's already-declared response, tying its output to
///     another operation an API consumer/generated-client would naturally call next (e.g. a create
///     response's id feeding straight into that resource's own read/update/delete operations). Purely
///     descriptive — Scalar and most codegen tools use it to wire up a "try this next" affordance, it
///     changes no runtime behavior. Must run after the target status code's response entry already
///     exists (i.e. after the matching `.Produces`/`.WithExample` call in the same fluent chain).
/// </summary>
internal static class LinkExtensions
{
    /// <param name="statusCode">The response status this link is attached to (the operation's own success status).</param>
    /// <param name="linkName">Scalar's display name for the link (PascalCase by OpenAPI convention).</param>
    /// <param name="targetOperationId">The `operationId` of the operation this link points to.</param>
    /// <param name="description">Shown alongside the link in Scalar — what calling it does.</param>
    /// <param name="parameters">
    ///     `(targetParameterName, runtimeExpression)` pairs, e.g. `("userId", "$response.body#/data/id")`
    ///     to feed this response's `data.id` into the target operation's `userId` path parameter.
    /// </param>
    public static RouteHandlerBuilder WithLink(this RouteHandlerBuilder builder, int statusCode, string linkName,
        string targetOperationId, string description, params (string ParameterName, string Expression)[] parameters)
    {
        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (!operation.Responses.TryGetValue(statusCode.ToString(), out var response) ||
                response is not OpenApiResponse concrete)
                return Task.CompletedTask;

            concrete.Links ??= new Dictionary<string, IOpenApiLink>();
            concrete.Links[linkName] = new OpenApiLink
            {
                OperationId = targetOperationId,
                Description = description,
                Parameters = parameters.ToDictionary(
                    p => p.ParameterName,
                    p => new RuntimeExpressionAnyWrapper { Expression = RuntimeExpression.Build(p.Expression) })
            };

            return Task.CompletedTask;
        });
    }
}
