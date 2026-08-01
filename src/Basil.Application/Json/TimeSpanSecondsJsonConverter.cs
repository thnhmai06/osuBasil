using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Application.Json;

/// <summary>
///     Serializes and deserializes <see cref="TimeSpan" /> values as a whole-second integer (for
///     example <c>225</c>) instead of System.Text.Json's default string form (<c>"00:03:45"</c>).
/// </summary>
/// <remarks>
///     This is the wire format for TimeSpan fields throughout the JSON API. It is registered once,
///     the same as <see cref="CountryJsonConverter" />, so it applies everywhere a TimeSpan is
///     embedded (for example <see cref="Basil.Domain.Beatmaps.Difficulty.TotalLength" />) without a
///     per-property attribute. Keeping the converter out of Basil.Domain means that project, which
///     has no project references, can carry a TimeSpan field without needing this type in scope.
/// </remarks>
public sealed class TimeSpanSecondsJsonConverter : JsonConverter<TimeSpan>
{
	/// <summary>
	///     Reads a TimeSpan value from a JSON integer and interprets it as seconds.
	/// </summary>
	public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return TimeSpan.FromSeconds(reader.GetInt32());
	}

	/// <summary>
	///     Writes a TimeSpan value as a JSON integer of its whole-second count.
	/// </summary>
	public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
	{
		writer.WriteNumberValue((int)value.TotalSeconds);
	}
}