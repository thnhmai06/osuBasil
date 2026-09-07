using Basil.Server.Features.Users;
using Basil.Domain.Login;

namespace Basil.Application.Tests.Services.Users;

/// <summary>
///     Verifies <see cref="UserSearchQueryParser" />'s handling of <c>GET /users/search</c>'s
///     <c>key&lt;operator&gt;value</c> filter syntax.
/// </summary>
public class UserSearchQueryParserTests
{
	[Fact]
	public void Parse_EmptyQuery_ReturnsEmptyFilters()
	{
		var result = UserSearchQueryParser.Parse("");

		Assert.Equal(UserSearchFilters.Empty, result);
	}

	[Fact]
	public void Parse_PlainText_BecomesKeywords()
	{
		var result = UserSearchQueryParser.Parse("cool_player");

		Assert.Equal("cool_player", result.Keywords);
		Assert.Null(result.Countries);
	}

	[Theory]
	[InlineData("country=jp")]
	[InlineData("country:jp")]
	[InlineData("country=JP")]
	public void Parse_SingleCountry_SetsCountriesFilter(string query)
	{
		var result = UserSearchQueryParser.Parse(query);

		Assert.Equal([Country.Jp], result.Countries);
		Assert.Null(result.Keywords);
	}

	[Fact]
	public void Parse_ConcatenatedCountryCodes_SetsMultipleCountries()
	{
		var result = UserSearchQueryParser.Parse("country=vnusgb");

		Assert.Equal([Country.Vn, Country.Us, Country.Gb], result.Countries);
	}

	[Fact]
	public void Parse_OddLengthCountryValue_FallsBackToKeywords()
	{
		var result = UserSearchQueryParser.Parse("country=vnu");

		Assert.Null(result.Countries);
		Assert.Equal("country=vnu", result.Keywords);
	}

	[Fact]
	public void Parse_UnknownCountry_FallsBackToKeywords()
	{
		var result = UserSearchQueryParser.Parse("country=nowhere");

		Assert.Null(result.Countries);
		Assert.Equal("country=nowhere", result.Keywords);
	}

	[Fact]
	public void Parse_OneUnknownChunkAmongValidOnes_FailsWholeToken()
	{
		var result = UserSearchQueryParser.Parse("country=vnzzus");

		Assert.Null(result.Countries);
		Assert.Equal("country=vnzzus", result.Keywords);
	}

	[Fact]
	public void Parse_Privilege_SetsPrivilegeMask()
	{
		var result = UserSearchQueryParser.Parse("privilege=2");

		Assert.Equal((ushort)2, result.PrivilegeMask);
	}

	[Fact]
	public void Parse_ComparisonOperator_IsNotRecognized_FallsBackToKeywords()
	{
		var result = UserSearchQueryParser.Parse("privilege>2");

		Assert.Null(result.PrivilegeMask);
		Assert.Equal("privilege>2", result.Keywords);
	}

	[Fact]
	public void Parse_MixOfKeywordsAndFilters_ExtractsBoth()
	{
		var result = UserSearchQueryParser.Parse("peppy country=jp privilege=1");

		Assert.Equal("peppy", result.Keywords);
		Assert.Equal([Country.Jp], result.Countries);
		Assert.Equal((ushort)1, result.PrivilegeMask);
	}
}