using System.Text.Json;
using System.Text.Json.Serialization;
using Basil.Domain.Login;

namespace Basil.Application.Json;

/// <summary>
///     Wire format for every <see cref="Country" /> field across the `api.` host: a lowercase
///     2-letter acronym (e.g. <c>"vn"</c>, <c>"xx"</c>) rather than the enum's numeric value —
///     registered once via <c>ConfigureHttpJsonOptions</c> in <c>Program.cs</c> (same as
///     <see cref="TimeSpanSecondsJsonConverter" />) so it applies everywhere a <see cref="Country" />
///     is embedded, with no per-property attribute needed.
/// </summary>
public sealed class CountryJsonConverter : JsonConverter<Country>
{
	public override Country Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var value = reader.GetString();
		return value is not null && Enum.TryParse<Country>(value, true, out var country)
			? country
			: Country.Xx;
	}

	public override void Write(Utf8JsonWriter writer, Country value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToAcronym());
	}
}