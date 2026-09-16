using Basil.Domain.Login;
using Basil.Domain.Users;

namespace Basil.Domain.Tests;

/// <summary>
///     Verifies <see cref="UserFilters" />'s handling of <c>GET /users/search</c>'s
///     <c>key&lt;operator&gt;value</c> filter syntax.
/// </summary>
public class UserFiltersTests
{
	[Fact]
	public void Parse_EmptyQuery_ReturnsEmptyFilters()
	{
		var result = UserFilters.From("");

		Assert.Equal(UserFilters.Empty, result);
	}

	[Fact]
	public void Parse_PlainText_BecomesKeywords()
	{
		var result = UserFilters.From("cool_player");

		Assert.Equal("cool_player", result.Keywords);
		Assert.Null(result.Countries);
	}

	[Theory]
	[InlineData("country=jp")]
	[InlineData("country:jp")]
	[InlineData("country=JP")]
	public void Parse_SingleCountry_SetsCountriesFilter(string query)
	{
		var result = UserFilters.From(query);

		Assert.Equal([Country.Jp], result.Countries);
		Assert.Null(result.Keywords);
	}

	[Fact]
	public void Parse_ConcatenatedCountryCodes_SetsMultipleCountries()
	{
		var result = UserFilters.From("country=vnusgb");

		Assert.Equal([Country.Vn, Country.Us, Country.Gb], result.Countries);
	}

	[Fact]
	public void Parse_OddLengthCountryValue_FallsBackToKeywords()
	{
		var result = UserFilters.From("country=vnu");

		Assert.Null(result.Countries);
		Assert.Equal("country=vnu", result.Keywords);
	}

	[Fact]
	public void Parse_UnknownCountry_FallsBackToKeywords()
	{
		var result = UserFilters.From("country=nowhere");

		Assert.Null(result.Countries);
		Assert.Equal("country=nowhere", result.Keywords);
	}

	[Fact]
	public void Parse_OneUnknownChunkAmongValidOnes_FailsWholeToken()
	{
		var result = UserFilters.From("country=vnzzus");

		Assert.Null(result.Countries);
		Assert.Equal("country=vnzzus", result.Keywords);
	}

	[Fact]
	public void Parse_Privilege_SetsPrivilege()
	{
		var result = UserFilters.From("privilege=2");

		Assert.Equal((UserPrivileges)2, result.Privilege);
	}

	[Fact]
	public void Parse_ComparisonOperator_IsNotRecognized_FallsBackToKeywords()
	{
		var result = UserFilters.From("privilege>2");

		Assert.Null(result.Privilege);
		Assert.Equal("privilege>2", result.Keywords);
	}

	[Fact]
	public void Parse_MixOfKeywordsAndFilters_ExtractsBoth()
	{
		var result = UserFilters.From("peppy country=jp privilege=1");

		Assert.Equal("peppy", result.Keywords);
		Assert.Equal([Country.Jp], result.Countries);
		Assert.Equal((UserPrivileges)1, result.Privilege);
	}
}