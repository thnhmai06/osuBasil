using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Application.Json;

/// <summary>
///     Wire format for every <see cref="TimeSpan" /> field across the `api.` host: a whole-second
///     integer (e.g. <c>225</c>) instead of System.Text.Json's default string (<c>"00:03:45"</c>) —
///     registered once via <c>ConfigureHttpJsonOptions</c> in <c>Program.cs</c>, same as
///     <see cref="CountryJsonConverter" />, so it applies everywhere a <see cref="TimeSpan" /> is
///     embedded (e.g. <see cref="Basil.Domain.Beatmaps.Difficulty.TotalLength" />) without needing a
///     per-property attribute — which also means <c>Basil.Domain</c> types can carry a
///     <see cref="TimeSpan" /> field without needing this converter type in scope (that project has
///     zero project references).
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
