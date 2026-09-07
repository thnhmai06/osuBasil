using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Attaches OpenAPI links to an already-declared response, documenting the operations that
///     naturally follow from its result without affecting runtime behavior.
/// </summary>
/// <remarks>
///     <para>
///         Links describe relationships between operations, such as a newly created resource's
///         identifier feeding directly into its corresponding read, update, or delete endpoint.
///         They exist solely for documentation and client tooling.
///     </para>
///     <para>
///         Tools such as Scalar and some OpenAPI code generators use these links to surface
///         "try this next" workflows or otherwise connect related operations. The generated
///         document changes only its metadata; no runtime behavior is modified.
///     </para>
///     <para>
///         This extension must run after the target response entry has already been created,
///         meaning the matching <c>.Produces(...)</c> or <c>.WithExample(...)</c> call must
///         appear earlier in the same endpoint configuration.
///     </para>
/// </remarks>
internal static class LinkExtensions
{
	/// <summary>
	///     Attaches an OpenAPI link to the route's already-declared response for <paramref name="statusCode" />.
	/// </summary>
	/// <param name="builder">The route whose already-declared response gets the link entry.</param>
	/// <param name="statusCode">The response status this link is attached to (the operation's own success status).</param>
	/// <param name="linkName">Scalar's display name for the link (PascalCase by OpenAPI convention).</param>
	/// <param name="targetOperationId">The `operationId` of the operation this link points to.</param>
	/// <param name="description">Shown alongside the link in Scalar, describing what calling it does.</param>
	/// <param name="parameters">
	///     `(targetParameterName, runtimeExpression)` pairs, e.g. `("userId", "$response.body#/data/id")`
	///     to feed this response's `data.id` into the target operation's `userId` path parameter.
	/// </param>
	/// <returns>The <paramref name="builder" /> for continued chaining.</returns>
	public static RouteHandlerBuilder WithLink(this RouteHandlerBuilder builder, int statusCode, string linkName,
		string targetOperationId, string description, params (string ParameterName, string Expression)[] parameters)
	{
		return builder.AddOpenApiOperationTransformer((operation, _, _) =>
		{
			if (operation.Responses?.TryGetValue(statusCode.ToString(), out var response) != true ||
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