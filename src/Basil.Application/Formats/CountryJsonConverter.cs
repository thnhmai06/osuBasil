using System.Text.Json;
using System.Text.Json.Serialization;
using Basil.Domain.Login;

namespace Basil.Application.Formats;

/// <summary>
///     Serializes and deserializes <see cref="Country" /> values using a lowercase two-letter
///     acronym (for example <c>"vn"</c> or <c>"xx"</c>) instead of the enum's numeric value.
/// </summary>
/// <remarks>
///     This is the wire format for Country fields throughout the JSON API. It is registered once,
///     the same as <see cref="TimeSpanSecondsJsonConverter" />, so it applies everywhere a Country
///     is embedded without a per-property attribute.
/// </remarks>
public sealed class CountryJsonConverter : JsonConverter<Country>
{
	/// <summary>
	///     Reads a Country value from a JSON string that names a member, case-insensitively.
	/// </summary>
	/// <returns>
	///     The matching Country member, or <see cref="Country.Xx" /> when the value is null or does
	///     not match any member.
	/// </returns>
	public override Country Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var value = reader.GetString();
		return value is not null && Enum.TryParse<Country>(value, true, out var country)
			? country
			: Country.Xx;
	}

	/// <summary>
	///     Writes a Country value as a JSON string of its two-letter acronym.
	/// </summary>
	public override void Write(Utf8JsonWriter writer, Country value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToAcronym());
	}
}