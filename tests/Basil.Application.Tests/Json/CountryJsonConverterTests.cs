using System.Text.Json;
using Basil.Application.Json;
using Basil.Domain.Login;

namespace Basil.Application.Tests.Json;

public class CountryJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new CountryJsonConverter() } };

    [Theory]
    [InlineData(Country.Vn, "\"vn\"")]
    [InlineData(Country.Us, "\"us\"")]
    [InlineData(Country.Xx, "\"xx\"")]
    public void Write_ProducesLowercaseTwoLetterAcronym(Country country, string expectedJson)
    {
        var json = JsonSerializer.Serialize(country, Options);

        Assert.Equal(expectedJson, json);
    }

    [Theory]
    [InlineData("\"vn\"", Country.Vn)]
    [InlineData("\"VN\"", Country.Vn)]
    [InlineData("\"us\"", Country.Us)]
    public void Read_ParsesAcronymIgnoringCase(string json, Country expected)
    {
        var country = JsonSerializer.Deserialize<Country>(json, Options);

        Assert.Equal(expected, country);
    }

    [Fact]
    public void Read_UnknownAcronym_FallsBackToXx()
    {
        var country = JsonSerializer.Deserialize<Country>("\"zz-not-real\"", Options);

        Assert.Equal(Country.Xx, country);
    }

    [Theory]
    [InlineData(Country.Vn)]
    [InlineData(Country.Us)]
    [InlineData(Country.Xx)]
    public void RoundTrip_PreservesValue(Country country)
    {
        var json = JsonSerializer.Serialize(country, Options);
        var roundTripped = JsonSerializer.Deserialize<Country>(json, Options);

        Assert.Equal(country, roundTripped);
    }
}
