using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Services.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Tests.Services.Beatmaps;

/// <summary>
///     Verifies <see cref="BeatmapsetSearchQueryParser" />'s handling of osu!'s
///     <c>key&lt;operator&gt;value</c> search syntax (Issue #4: "Support searching by beatmap ID,
///     beatmapset ID, and osu!'s search parameters").
/// </summary>
public class BeatmapsetSearchQueryParserTests
{
	[Fact]
	public void Parse_EmptyQuery_ReturnsEmptyFilters()
	{
		var result = BeatmapsetSearchQueryParser.Parse("");

		Assert.Equal(BeatmapsetSearchFilters.Empty, result);
	}

	[Fact]
	public void Parse_PlainText_BecomesKeywords()
	{
		var result = BeatmapsetSearchQueryParser.Parse("camellia exit this earth");

		Assert.Equal("camellia exit this earth", result.Keywords);
		Assert.Null(result.Stars);
	}

	[Theory]
	[InlineData("stars>5", ComparisonOperator.GreaterThan, 5.0)]
	[InlineData("star>=4.5", ComparisonOperator.GreaterThanOrEqual, 4.5)]
	[InlineData("stars<6", ComparisonOperator.LessThan, 6.0)]
	[InlineData("stars<=6.25", ComparisonOperator.LessThanOrEqual, 6.25)]
	[InlineData("stars=5", ComparisonOperator.Equal, 5.0)]
	[InlineData("stars:5", ComparisonOperator.Equal, 5.0)]
	public void Parse_Stars_ParsesOperatorAndValue(string query, ComparisonOperator op, double value)
	{
		var result = BeatmapsetSearchQueryParser.Parse(query);

		Assert.Equal(new ComparableFilter<double>(op, value), result.Stars);
		Assert.Null(result.Keywords);
	}

	[Fact]
	public void Parse_Ar_SetsArFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("ar=9");

		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.Equal, 9.0), result.Ar);
	}

	[Theory]
	[InlineData("hp<4")]
	[InlineData("dr<4")]
	public void Parse_HpAndDrAlias_SetHpFilter(string query)
	{
		var result = BeatmapsetSearchQueryParser.Parse(query);

		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.LessThan, 4.0), result.Hp);
	}

	[Fact]
	public void Parse_Cs_SetsCsFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("cs>5");

		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.GreaterThan, 5.0), result.Cs);
	}

	[Fact]
	public void Parse_Od_SetsOdFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("od>=9");

		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.GreaterThanOrEqual, 9.0), result.Od);
	}

	[Fact]
	public void Parse_Bpm_SetsBpmFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("bpm<=180");

		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.LessThanOrEqual, 180.0), result.Bpm);
	}

	[Theory]
	[InlineData("keys=7")]
	[InlineData("key=7")]
	public void Parse_KeysAndKeyAlias_SetKeysFilter(string query)
	{
		var result = BeatmapsetSearchQueryParser.Parse(query);

		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.Equal, 7.0), result.Keys);
	}

	[Fact]
	public void Parse_Circles_SetsIntFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("circles=100");

		Assert.Equal(new ComparableFilter<int>(ComparisonOperator.Equal, 100), result.Circles);
	}

	[Fact]
	public void Parse_Sliders_SetsIntFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("sliders>50");

		Assert.Equal(new ComparableFilter<int>(ComparisonOperator.GreaterThan, 50), result.Sliders);
	}

	[Theory]
	[InlineData("length>=70", 70.0)]
	[InlineData("length>=70s", 70.0)]
	[InlineData("length>=8m", 480.0)]
	[InlineData("length>=0.9h", 3240.0)]
	[InlineData("length>=70000ms", 70.0)]
	public void Parse_Length_ConvertsUnitsToSeconds(string query, double expectedSeconds)
	{
		var result = BeatmapsetSearchQueryParser.Parse(query);

		Assert.NotNull(result.LengthSeconds);
		Assert.Equal(ComparisonOperator.GreaterThanOrEqual, result.LengthSeconds.Operator);
		Assert.Equal(expectedSeconds, result.LengthSeconds.Value, 3);
	}

	[Fact]
	public void Parse_Creator_SetsTextFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("creator=hello");

		Assert.Equal("hello", result.Creator);
	}

	[Fact]
	public void Parse_QuotedArtist_StripsQuotesAndKeepsSpaces()
	{
		var result = BeatmapsetSearchQueryParser.Parse("""artist="hello world" """);

		Assert.Equal("hello world", result.Artist);
	}

	[Fact]
	public void Parse_QuotedValueWithEscapedQuote_Unescapes()
	{
		var result = BeatmapsetSearchQueryParser.Parse("""title="nested \"quote\"" """);

		Assert.Equal("""nested "quote" """.Trim(), result.Title);
	}

	[Fact]
	public void Parse_Difficulty_SetsTextFilter()
	{
		var result = BeatmapsetSearchQueryParser.Parse("difficulty=easy");

		Assert.Equal("easy", result.Difficulty);
	}

	[Theory]
	[InlineData("status=ranked", BeatmapStatus.Ranked)]
	[InlineData("status=approved", BeatmapStatus.Approved)]
	[InlineData("status=loved", BeatmapStatus.Loved)]
	[InlineData("status=graveyard", BeatmapStatus.Pending)]
	[InlineData("status=qual", BeatmapStatus.Qualified)]
	public void Parse_Status_ResolvesPrefixToStatus(string query, BeatmapStatus expected)
	{
		var result = BeatmapsetSearchQueryParser.Parse(query);

		Assert.Equal(expected, result.Status);
	}

	[Fact]
	public void Parse_UnrecognizedStatusName_LeavesTokenAsKeyword()
	{
		var result = BeatmapsetSearchQueryParser.Parse("status=nonsense");

		Assert.Null(result.Status);
		Assert.Equal("status=nonsense", result.Keywords);
	}

	[Fact]
	public void Parse_YearOnlyCreated_SpansWholeYear()
	{
		var result = BeatmapsetSearchQueryParser.Parse("created=2017");

		Assert.NotNull(result.Created);
		Assert.Equal(ComparisonOperator.Equal, result.Created.Operator);
		Assert.Equal(new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Created.RangeStart);
		Assert.Equal(new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Created.RangeEnd);
	}

	[Fact]
	public void Parse_YearMonthCreated_SpansWholeMonth()
	{
		var result = BeatmapsetSearchQueryParser.Parse("created=2018-05");

		Assert.Equal(new DateTimeOffset(2018, 5, 1, 0, 0, 0, TimeSpan.Zero), result.Created!.RangeStart);
		Assert.Equal(new DateTimeOffset(2018, 6, 1, 0, 0, 0, TimeSpan.Zero), result.Created.RangeEnd);
	}

	[Fact]
	public void Parse_YearMonthDayCreated_SpansWholeDay()
	{
		var result = BeatmapsetSearchQueryParser.Parse("created=2018-05-01");

		Assert.Equal(new DateTimeOffset(2018, 5, 1, 0, 0, 0, TimeSpan.Zero), result.Created!.RangeStart);
		Assert.Equal(new DateTimeOffset(2018, 5, 2, 0, 0, 0, TimeSpan.Zero), result.Created.RangeEnd);
	}

	[Fact]
	public void Parse_SubmittedAliasesCreated()
	{
		var result = BeatmapsetSearchQueryParser.Parse("submitted=2017");

		Assert.NotNull(result.Created);
	}

	[Fact]
	public void Parse_Updated_SetsUpdatedNotCreated()
	{
		var result = BeatmapsetSearchQueryParser.Parse("updated<2020");

		Assert.Null(result.Created);
		Assert.NotNull(result.Updated);
		Assert.Equal(ComparisonOperator.LessThan, result.Updated.Operator);
	}

	[Theory]
	[InlineData("source=anime")]
	[InlineData("tag=\"hello world\"")]
	[InlineData("favourites>200")]
	[InlineData("ranked>2018")]
	[InlineData("divisor>0")]
	[InlineData("featured_artist=123")]
	public void Parse_KeysBasilHasNoDataFor_DegradeToKeywords(string query)
	{
		var result = BeatmapsetSearchQueryParser.Parse(query);

		Assert.Equal(query, result.Keywords);
	}

	[Fact]
	public void Parse_MixedKeywordsAndFilters_SeparatesBoth()
	{
		var result = BeatmapsetSearchQueryParser.Parse("hello stars>=1 stars<4 world");

		Assert.Equal("hello world", result.Keywords);
		Assert.Equal(new ComparableFilter<double>(ComparisonOperator.LessThan, 4.0), result.Stars);
	}

	[Fact]
	public void Parse_InvalidNumericValue_LeavesTokenAsKeyword()
	{
		var result = BeatmapsetSearchQueryParser.Parse("stars>notanumber");

		Assert.Null(result.Stars);
		Assert.Equal("stars>notanumber", result.Keywords);
	}

	[Fact]
	public void Parse_WhitespaceOnlyResult_KeywordsIsNull()
	{
		var result = BeatmapsetSearchQueryParser.Parse("stars>5");

		Assert.Null(result.Keywords);
	}
}