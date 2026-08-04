using Basil.Web.Auth;
using Basil.Web.Routing.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Declares the `Authorization: Bearer` header as a real OpenAPI security scheme on the
///     `basilapi` document, attached to every operation that enforces the admin key policy.
/// </summary>
/// <remarks>
///     <para>
///         The header was previously undocumented beyond prose in each route's own `.WithDescription`
///         (see <see cref="RouteDocs.AdminKeyNote" />). Because the scheme is
///         attached to every operation that actually enforces
///         <see cref="AdminKeyDefaults.Policy" /> via `.RequireAuthorization(...)`, Scalar's
///         "Authorize" button and any generated client SDK pick it up automatically.
///     </para>
/// </remarks>
internal static class SecuritySchemeTransformers
{
	private const string SchemeId = "AdminKey";

	/// <summary>
	///     Registers the document and operation transformers that declare the admin key security scheme
	///     and attach it to every operation that enforces the admin key policy.
	/// </summary>
	/// <param name="options">The OpenAPI options to register the transformers on.</param>
	public static void AddAdminKeyDocumentTransformer(this OpenApiOptions options)
	{
		options.AddDocumentTransformer((document, _, _) =>
		{
			document.Components ??= new OpenApiComponents();
			document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
			document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
			{
				Type = SecuritySchemeType.Http,
				Scheme = "bearer",
				Description = "The server's admin key, managed via `GET`/`PUT`/`DELETE /adminkey`. Required for " +
				              "every management/mutation route on this host, unless the server is in bypass mode " +
				              "(no key configured)."
			};

			return Task.CompletedTask;
		});

		options.AddOperationTransformer((operation, context, _) =>
		{
			var requiresAdminKey = context.Description.ActionDescriptor.EndpointMetadata
				.OfType<IAuthorizeData>()
				.Any(a => a.Policy == AdminKeyDefaults.Policy);

			if (!requiresAdminKey) return Task.CompletedTask;

			operation.Security ??= [];
			operation.Security.Add(new OpenApiSecurityRequirement
			{
				[new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = []
			});

			operation.Responses ??= new OpenApiResponses();
			operation.Responses.TryAdd("401",
				new OpenApiResponse { Description = "Missing or invalid admin key bearer token." });

			return Task.CompletedTask;
		});
	}
}