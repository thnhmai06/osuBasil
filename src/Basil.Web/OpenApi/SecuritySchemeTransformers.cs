using Basil.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Declares the `X-Admin-Key` header as a real OpenAPI security scheme on the `basilapi` document
///     (it was previously undocumented beyond prose in each route's own `.WithDescription`, per
///     <see cref="Basil.Web.Routing.RouteDocs.AdminKeyNote" />) and attaches it to every operation that
///     actually enforces <see cref="AdminKeyDefaults.Policy" /> via `.RequireAuthorization(...)`, so
///     Scalar's "Authorize" button and any generated client SDK pick it up automatically.
/// </summary>
internal static class SecuritySchemeTransformers
{
    private const string SchemeId = "AdminKey";

    public static void AddAdminKeyDocumentTransformer(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Admin-Key",
                Description = "Admin key matching the server's configured `Server:AdminKey`. Required for " +
                              "every management/mutation route on this host."
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
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid X-Admin-Key." });

            return Task.CompletedTask;
        });
    }
}