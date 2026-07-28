using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Application.Json;

/// <summary>
///     Wire format for <see cref="BeatmapView.TotalLength" />: a whole-second integer (e.g. <c>225</c>)
///     instead of System.Text.Json's default <see cref="TimeSpan" /> string (<c>"00:03:45"</c>) — the
///     domain/repository/ingestion layers keep <see cref="TimeSpan" /> unchanged, only this one API
///     response property carries the converter, via <c>[property: JsonConverter(typeof(...))]</c>, so
///     it's not registered globally.
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