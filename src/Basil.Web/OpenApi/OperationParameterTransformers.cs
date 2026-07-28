using System.Text.Json.Nodes;
using Basil.Web.Routing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>Per-operation declaration fixes that don't fit the schema- or envelope-level transformers.</summary>
internal static class OperationParameterTransformers
{
    /// <summary>
    ///     `page`/`pageSize` on every paginated list route (`GET /matches`, `/beatmapsets`, `/scores`)
    ///     are plain `int?` minimal-API parameters, so the default generator declares them as a bare
    ///     unconstrained integer — declares the real constraints <see cref="Pagination.Normalize" />
    ///     actually enforces at runtime instead.
    /// </summary>
    public static void AddPaginationParameterConstraintsTransformer(this OpenApiOptions options)
    {
        options.AddOperationTransformer((operation, _, _) =>
        {
            if (operation.Parameters is null) return Task.CompletedTask;

            foreach (var parameter in operation.Parameters)
            {
                if (parameter.Schema is not OpenApiSchema schema) continue;

                switch (parameter.Name)
                {
                    case "page":
                        schema.Minimum = "1";
                        schema.Default = JsonValue.Create(1);
                        break;
                    case "pageSize":
                        schema.Minimum = "1";
                        schema.Maximum = Pagination.MaxPageSize.ToString();
                        schema.Default = JsonValue.Create(Pagination.DefaultPageSize);
                        break;
                }
            }

            return Task.CompletedTask;
        });
    }
}
