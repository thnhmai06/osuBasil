using Basil.Application.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>Schema-shape fixes that apply to how individual .NET types are represented, independent of any one operation.</summary>
internal static class SchemaTypeTransformers
{
    /// <summary>
    ///     A type with a custom <c>JsonConverter</c> (<see cref="CountryJsonConverter" />,
    ///     <see cref="TimeSpanSecondsJsonConverter" />) can't have its wire shape inferred by reflection,
    ///     so the generator emits an empty `{}` schema — declare the real shape by hand instead. Scoped to
    ///     the `basilapi` document (the only one using these converters).
    /// </summary>
    public static void AddCustomConverterSchemaTransformer(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type == typeof(Basil.Domain.Login.Country))
            {
                schema.Type = JsonSchemaType.String;
                schema.Description = "2-letter lowercase country/region acronym (e.g. \"vn\", \"xx\" for unknown).";
                schema.Pattern = "^[a-z]{2}$";
            }
            else if (context.JsonTypeInfo.Type == typeof(TimeSpan))
            {
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int32";
                schema.Description = "Whole seconds.";
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    ///     The default .NET OpenAPI generator represents every integer/number as accepting either a real
    ///     JSON number or a numeric string (`type: [integer, string]` plus a digits-only `pattern`), a
    ///     JS-safe-integer accommodation that's unconditionally unhelpful here — this server's JSON
    ///     serialization has never emitted a stringified number, on any field, at any size (`long`
    ///     included). Strips it down to the plain numeric type on every generated schema, across every
    ///     document (this is a generator-default artifact, not specific to any one document's types).
    /// </summary>
    public static void AddNumericSchemaSimplificationTransformer(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, _, _) =>
        {
            if (schema.Type is { } type && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number))
                && type.HasFlag(JsonSchemaType.String))
            {
                schema.Type = type & ~JsonSchemaType.String;
                schema.Pattern = null;
            }

            return Task.CompletedTask;
        });
    }
}
