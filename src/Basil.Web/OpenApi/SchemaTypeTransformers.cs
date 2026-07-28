using System.Text.Json.Nodes;
using Basil.Application.Json;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;
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
    ///     Enums that are genuinely combined by bitwise OR on the wire (a real player can have
    ///     `Hidden | HardRock` set at once) — the noun is used in <see cref="ApplyBitmaskDescription" />'s
    ///     generated prose. <c>SlotStatus</c> is `[Flags]` in C# too, but only for internal
    ///     grouped-comparison convenience (see <see cref="AddEnumValuesSchemaTransformer" />'s own doc
    ///     comment) — a slot's serialized `status` is always exactly one of its single-bit values, never
    ///     a combination, so it's treated as a regular closed enum instead of a bitmask below.
    /// </summary>
    private static readonly Dictionary<Type, string> CombinableFlagsNouns = new()
    {
        [typeof(Mods)] = "osu! mods",
        [typeof(UserPrivileges)] = "user privileges"
    };

    /// <summary>
    ///     A non-<c>[Flags]</c> enum still serializes as a plain number (see the enum-wire-convention
    ///     bullet in <c>CLAUDE.md</c> — no <c>JsonStringEnumConverter</c> anywhere), but the *set* of
    ///     valid numbers is closed, unlike an arbitrary integer field — declaring it via `enum:` lets
    ///     Scalar/generated clients offer a fixed value list instead of a bare "integer" input, with the
    ///     name-to-value mapping spelled out in the description since OpenAPI's `enum:` carries no
    ///     built-in slot for member names. A genuinely combinable <c>[Flags]</c> enum (see
    ///     <see cref="CombinableFlagsNouns" />) gets bitmask prose instead — `enum:` can't represent
    ///     "any OR-combination of these bits" — spelling out each single-bit flag's value, that multiple
    ///     flags combine via bitwise OR, and a worked example. Any other `[Flags]` enum (currently just
    ///     <c>SlotStatus</c>) is treated as a regular closed enum, since its wire value is never actually
    ///     a combination despite the C# attribute. <see cref="Country" /> is excluded entirely — it
    ///     already gets its own string shape from <see cref="AddCustomConverterSchemaTransformer" /> and
    ///     has far too many members for a meaningful dropdown anyway.
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
                .Where(t => t.IsEnum && t.IsPublic && t != typeof(Country))
                .ToDictionary(t => t.Name);

            foreach (var (name, schema) in schemas)
            {
                if (schema is not OpenApiSchema s || !enumTypesByName.TryGetValue(name, out var type)) continue;

                if (CombinableFlagsNouns.TryGetValue(type, out var noun))
                    ApplyBitmaskDescription(s, type, noun);
                else
                    ApplyClosedEnumValues(s, type);
            }

            return Task.CompletedTask;
        });
    }

    private static void ApplyClosedEnumValues(OpenApiSchema schema, Type type)
    {
        var members = Enum.GetValues(type).Cast<object>()
            .Select(v => (Name: v.ToString()!, Value: Convert.ToInt64(v)))
            .ToList();

        schema.Enum = members.Select(m => (JsonNode)JsonValue.Create(m.Value)).ToList();
        var mapping = string.Join(", ", members.Select(m => $"{m.Value} = {m.Name}"));
        schema.Description = schema.Description is { Length: > 0 } ? $"{schema.Description} ({mapping})" : mapping;
    }

    /// <summary>
    ///     No `enum:` array (a combinable flags field's valid values are every OR-combination of its
    ///     single-bit members, not a closed list) — only single-bit members are listed as "flag values"
    ///     (a combo alias like `UserPrivileges.Donator = Supporter | Premium` is itself expressible as the
    ///     OR of its parts, so it isn't a distinct flag value worth listing separately), each written as
    ///     `1 &lt;&lt; N` — matching how every one of these enums is itself declared in source (see
    ///     `Mods.cs`/`Privileges.cs`) — rather than the decimal value, which hides which bit it is. The
    ///     worked example combines the first two single-bit flags in ascending value order — deliberately
    ///     generic rather than hand-picked per type, so it can't go stale if a type's members change.
    /// </summary>
    private static void ApplyBitmaskDescription(OpenApiSchema schema, Type type, string noun)
    {
        var singleBitFlags = Enum.GetValues(type).Cast<object>()
            .Select(v => (Name: v.ToString()!, Value: Convert.ToInt64(v)))
            .Where(m => m.Value != 0 && (m.Value & (m.Value - 1)) == 0)
            .Select(m => (m.Name, m.Value, Shift: System.Numerics.BitOperations.Log2((ulong)m.Value)))
            .OrderBy(m => m.Value)
            .ToList();

        var flagLines = string.Join("\n", singleBitFlags.Select(m => $"1 << {m.Shift} = {m.Name}"));
        var (a, b) = (singleBitFlags[0], singleBitFlags[1]);

        schema.Description =
            $"Bitmask of enabled {noun}.\n\n" +
            $"Flag values:\n{flagLines}\n\n" +
            $"Multiple {noun} are combined using bitwise OR.\n\n" +
            $"Example:\n{a.Name} (1 << {a.Shift}) + {b.Name} (1 << {b.Shift}) = {a.Value + b.Value}.";
    }
}
