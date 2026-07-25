using System.Text.Json;
using Basil.Application.Json;

namespace Basil.Application.Tests.Json;

public class TimeSpanSecondsJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new TimeSpanSecondsJsonConverter() } };

    [Fact]
    public void Write_ProducesWholeSecondInteger_NotTimeSpanString()
    {
        var json = JsonSerializer.Serialize(TimeSpan.FromSeconds(225), Options);

        Assert.Equal("225", json);
    }

    [Fact]
    public void Write_TruncatesSubSecondPrecision()
    {
        var json = JsonSerializer.Serialize(TimeSpan.FromMilliseconds(225_900), Options);

        Assert.Equal("225", json);
    }

    [Fact]
    public void Read_ParsesIntegerSecondsIntoTimeSpan()
    {
        var value = JsonSerializer.Deserialize<TimeSpan>("225", Options);

        Assert.Equal(TimeSpan.FromSeconds(225), value);
    }

    [Fact]
    public void RoundTrip_PreservesWholeSecondValue()
    {
        var original = TimeSpan.FromSeconds(3725); // > 1 hour, exercises no special-casing
        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<TimeSpan>(json, Options);

        Assert.Equal(original, roundTripped);
    }
}
