using System.Numerics;
using System.Text.Json.Nodes;
using Basil.Application.Formats;
using Basil.Domain.Beatmaps;
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
	///     Enums genuinely combined by bitwise OR on the wire (a real userSession can have
	///     `Hidden | HardRock` set at once). The noun feeds the generated prose in
	///     <see cref="ApplyBitmaskDescription" />. <c>SlotStatus</c> is `[Flags]` in C# too, but only
	///     for internal grouped-comparison convenience (see
	///     <see cref="AddEnumValuesSchemaTransformer" />'s own doc comment). A slot's serialized
	///     `status` is always exactly one of its single-bit values, never a combination, so it's
	///     treated as a regular closed enum instead of a bitmask below.
	/// </summary>
	private static readonly Dictionary<Type, string> CombinableFlagsNouns = new()
	{
		[typeof(Mods)] = "osu! mods",
		[typeof(UserPrivileges)] = "user privileges"
	};

	/// <param name="options">The OpenAPI options to register the schema transformer on.</param>
	extension(OpenApiOptions options)
	{
		/// <summary>
		///     A type with a custom <c>JsonConverter</c> (<see cref="CountryJsonConverter" />,
		///     <see cref="TimeSpanSecondsJsonConverter" />) can't have its wire shape inferred by reflection,
		///     so the generator emits an empty `{}` schema, and the real shape is declared by hand instead.
		///     Scoped to the `basilapi` document (the only one using these converters).
		/// </summary>
		public void AddCustomConverterSchemaTransformer()
		{
			options.AddSchemaTransformer((schema, context, _) =>
			{
				if (context.JsonTypeInfo.Type == typeof(Country))
				{
					var acronyms = Enum.GetValues<Country>().Select(c => c.ToAcronym()).Order().ToList();
					schema.Type = JsonSchemaType.String;
					schema.Description =
						"2-letter lowercase ISO 3166-1 country/region acronym, or \"xx\" if unknown. " +
						$"Accepted values: {string.Join(", ", acronyms.Select(a => $"\"{a}\""))}.";
					schema.Enum = [.. acronyms.Select(JsonNode (a) => JsonValue.Create(a))];
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
		///     The default .NET OpenAPI generator represents every integer/number as accepting either a
		///     real JSON number or a numeric string (`type: [integer, string]` plus a digits-only
		///     `pattern`). That's a JS-safe-integer accommodation, and it's unconditionally unhelpful
		///     here: this server's JSON serialization has never emitted a stringified number on any
		///     field at any size, `long` included. Strips it down to the plain numeric type on every
		///     generated schema across every document (a generator-default artifact, not specific to any
		///     one document's types).
		/// </summary>
		public void AddNumericSchemaSimplificationTransformer()
		{
			options.AddSchemaTransformer((schema, _, _) =>
			{
				if (schema.Type is { } type && (type.HasFlag(JsonSchemaType.Integer) ||
				                                type.HasFlag(JsonSchemaType.Number))
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
		///     bullet in <c>CLAUDE.md</c>, no <c>JsonStringEnumConverter</c> anywhere), but its valid
		///     values are a closed set, unlike an arbitrary integer field. Declaring it via `enum:` lets
		///     Scalar/generated clients offer a fixed value list instead of a bare "integer" input. The
		///     name-to-value mapping is spelled out in the description, since OpenAPI's `enum:` carries
		///     no built-in slot for member names. A genuinely combinable <c>[Flags]</c> enum (see
		///     <see cref="CombinableFlagsNouns" />) gets bitmask prose instead, since `enum:` can't
		///     represent "any OR-combination of these bits": the prose lists each single-bit flag's
		///     value, notes that flags combine via bitwise OR, and shows a worked example. Any other
		///     `[Flags]` enum (currently just <c>SlotStatus</c>) is treated as a regular closed enum,
		///     since its wire value is never actually a combination despite the C# attribute.
		///     <see cref="Country" /> is excluded entirely: it already gets its own string shape from
		///     <see cref="AddCustomConverterSchemaTransformer" />, and it has far too many members for a
		///     meaningful dropdown anyway.
		///     <para>
		///         Runs as a *document* transformer over the final `components.schemas`, matched by
		///         component name against every public enum across the `Basil.*` assemblies, rather than
		///         as a schema transformer keyed on <c>context.JsonTypeInfo.Type</c>. A schema
		///         transformer only reliably mutates the *first* schema object generated for a given
		///         type, and for a type used at several call sites (nullable in one place, non-nullable
		///         in another) that first mutation isn't guaranteed to be the one that survives into the
		///         final named component (confirmed by inspecting the generated document: some enum
		///         components kept the mutation, others silently didn't). Operating on the
		///         fully-assembled document sidesteps that ordering entirely.
		///     </para>
		/// </summary>
		public void AddEnumValuesSchemaTransformer()
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

		/// <summary>
		///     The generator represents a `[JsonPolymorphic]`/`[JsonDerivedType]` base type (e.g.
		///     <see cref="BeatmapObjectCounts" />) as `anyOf` + `discriminator`. That's technically not
		///     wrong, every listed branch is still a valid match, but a discriminated value is always
		///     exactly one branch, never several at once. `oneOf` is the semantically correct keyword, and
		///     it's the one Scalar renders as a clean type-switcher. Runs as a *document* transformer for
		///     the same ordering reason <see cref="AddEnumValuesSchemaTransformer" /> does. It's matched
		///     generically by "has a discriminator" rather than by type, so any future polymorphic base
		///     gets this for free.
		/// </summary>
		public void AddPolymorphicOneOfSchemaTransformer()
		{
			options.AddDocumentTransformer((document, _, _) =>
			{
				if (document.Components?.Schemas is not { } schemas) return Task.CompletedTask;

				foreach (var schema in schemas.Values)
					if (schema is OpenApiSchema { Discriminator: not null, AnyOf.Count: > 0 } s)
					{
						s.OneOf = s.AnyOf;
						s.AnyOf = null;
					}

				return Task.CompletedTask;
			});
		}
	}

	/// <summary>
	///     Populates the schema's enum values and a name-to-value mapping in its description.
	/// </summary>
	/// <param name="schema">The OpenAPI schema to populate.</param>
	/// <param name="type">The closed enum type the schema represents.</param>
	private static void ApplyClosedEnumValues(OpenApiSchema schema, Type type)
	{
		var members = Enum.GetValues(type).Cast<object>()
			.Select(v => (Name: v.ToString()!, Value: Convert.ToInt64(v)))
			.ToList();

		schema.Enum = [.. members.Select(JsonNode (m) => JsonValue.Create(m.Value))];
		var mapping = string.Join(", ", members.Select(m => $"{m.Value} = {m.Name}"));
		schema.Description = schema.Description is { Length: > 0 } ? $"{schema.Description} ({mapping})" : mapping;
	}

	/// <summary>
	///     A combinable flags field has no `enum:` array, because its valid values are every
	///     OR-combination of its single-bit members, not a closed list. Only single-bit members are
	///     listed as "flag values": a combo alias like `UserPrivileges.Donator = Supporter | Premium`
	///     is expressible as the OR of its parts, so it isn't a distinct flag value worth listing
	///     separately. Each is written as `1 &lt;&lt; N`, matching how every one of these enums is
	///     declared in source (see `Mods.cs`/`Privileges.cs`), rather than the decimal value, which
	///     hides which bit it is. The worked example combines the first two single-bit flags in
	///     ascending value order. It's deliberately generic rather than hand-picked per type, so it
	///     can't go stale if a type's members change.
	/// </summary>
	private static void ApplyBitmaskDescription(OpenApiSchema schema, Type type, string noun)
	{
		var singleBitFlags = Enum.GetValues(type).Cast<object>()
			.Select(v => (Name: v.ToString()!, Value: Convert.ToInt64(v)))
			.Where(m => m.Value != 0 && (m.Value & (m.Value - 1)) == 0)
			.Select(m => (m.Name, m.Value, Shift: BitOperations.Log2((ulong)m.Value)))
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