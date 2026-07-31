using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Domain.Json;

/// <summary>
///     Wire format for <see cref="Basil.Domain.Beatmaps.Difficulty.TotalLength" />: a whole-second
///     integer (e.g. <c>225</c>) instead of System.Text.Json's default <see cref="TimeSpan" /> string
///     (<c>"00:03:45"</c>) — applied via <c>[property: JsonConverter(typeof(...))]</c> directly on
///     that one property, so it's not registered globally. Lives in <c>Basil.Domain</c> (rather than
///     <c>Basil.Application</c>, where the rest of the API's JSON converters live) purely because
///     <see cref="Basil.Domain.Beatmaps.Difficulty" /> itself has zero project references and needs
///     the converter type in scope to declare the attribute.
/// </summary>
public sealed class TimeSpanSecondsJsonConverter : JsonConverter<TimeSpan>
{
	public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return TimeSpan.FromSeconds(reader.GetInt32());
	}

	public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
	{
		writer.WriteNumberValue((int)value.TotalSeconds);
	}
}
