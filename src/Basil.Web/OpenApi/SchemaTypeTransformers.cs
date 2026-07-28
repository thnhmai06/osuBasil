using System.Text.Json.Nodes;
using Basil.Application.Json;
using Basil.Domain.Login;
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

    /// <summary>
    ///     A non-<c>[Flags]</c> enum still serializes as a plain number (see the enum-wire-convention
    ///     bullet in <c>CLAUDE.md</c> — no <c>JsonStringEnumConverter</c> anywhere), but the *set* of
    ///     valid numbers is closed, unlike an arbitrary integer field — declaring it via `enum:` lets
    ///     Scalar/generated clients offer a fixed value list instead of a bare "integer" input, with the
    ///     name-to-value mapping spelled out in the description since OpenAPI's `enum:` carries no
    ///     built-in slot for member names. A `[Flags]` enum (<c>Mods</c>, <c>UserPrivileges</c>,
    ///     <c>SlotStatus</c>) is a bitwise combination of members, not a closed set of single values, so
    ///     it's left as a plain integer; <see cref="Country" /> is excluded too — it already gets its own
    ///     string shape from <see cref="AddCustomConverterSchemaTransformer" /> and has far too many
    ///     members for a meaningful dropdown anyway.
    ///     <para>
    ///     Runs as a *document* transformer over the final `components.schemas`, matched by component
    ///     name against every public enum across the `Basil.*` assemblies, rather than as a schema
    ///     transformer keyed on <c>context.JsonTypeInfo.Type</c> — a schema transformer only reliably
    ///     mutates the *first* schema object generated for a given type, and for a type used at several
    ///     call sites (nullable in one place, non-nullable in another) that first mutation isn't
    ///     guaranteed to be the one that survives into the final named component (confirmed by
    ///     inspecting the generated document: some enum components kept the mutation, others silently
    ///     didn't). Operating on the fully-assembled document sidesteps that ordering entirely.
    ///     </para>
    /// </summary>
    public static void AddEnumValuesSchemaTransformer(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            if (document.Components?.Schemas is not { } schemas) return Task.CompletedTask;

            var enumTypesByName = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("Basil.", StringComparison.Ordinal) == true)
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsEnum && t.IsPublic && t != typeof(Country) &&
                    !t.IsDefined(typeof(FlagsAttribute), inherit: false))
                .ToDictionary(t => t.Name);

            foreach (var (name, schema) in schemas)
            {
                if (schema is not OpenApiSchema s || !enumTypesByName.TryGetValue(name, out var type)) continue;

                var members = Enum.GetValues(type).Cast<object>()
                    .Select(v => (Name: v.ToString()!, Value: Convert.ToInt64(v)))
                    .ToList();

                s.Enum = members.Select(m => (JsonNode)JsonValue.Create(m.Value)).ToList();
                var mapping = string.Join(", ", members.Select(m => $"{m.Value} = {m.Name}"));
                s.Description = s.Description is { Length: > 0 } ? $"{s.Description} ({mapping})" : mapping;
            }

            return Task.CompletedTask;
        });
    }
}
