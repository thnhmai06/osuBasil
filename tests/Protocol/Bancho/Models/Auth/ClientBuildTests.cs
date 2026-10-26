using Basil.Protocol.Bancho.Models.Auth;

namespace Basil.Protocol.Bancho.Tests.Models.Auth;

/// <summary>
///     Verifies the build-string contract <c>bYYYYMMDD[.revision][stream]</c>: the <c>b</c> prefix is
///     optional, the revision is a single digit, the stream suffix (and the whole text) parse
///     case-insensitively, and <see cref="ClientBuild.ToString" /> returns the canonical lowercase wire text.
///     Anything else is rejected.
/// </summary>
public class ClientBuildTests
{
	public static TheoryData<string, string, int?, int> ValidBuilds()
	{
		return new TheoryData<string, string, int?, int>
		{
			{ "b20240815", "2024-08-15", null, (int)ClientStream.Stable },
			{ "20240815", "2024-08-15", null, (int)ClientStream.Stable },
			{ "b20240815.2", "2024-08-15", 2, (int)ClientStream.Stable },
			{ "b20240815beta", "2024-08-15", null, (int)ClientStream.Beta },
			{ "b20240815cuttingedge", "2024-08-15", null, (int)ClientStream.CuttingEdge },
			{ "b20240815dev", "2024-08-15", null, (int)ClientStream.Dev },
			{ "b20240815tourney", "2024-08-15", null, (int)ClientStream.Tourney }
		};
	}

	public static TheoryData<string> Rejects()
	{
		// "20241399" has month 13: the shape matches but the date is not a date.
		return new TheoryData<string>
		{
			"",
			"xb20240815",
			"b20240815gamma",
			"b20240815.23",
			"b20240815\n",
			"20241399",
			"B20240815",
			"b20240815BETA",
			"b20240815Tourney"
		};
	}

	public static TheoryData<string, string> CanonicalText()
	{
		return new TheoryData<string, string>
		{
			{ "b20240815", "b20240815" },
			{ "20240815", "b20240815" },
			{ "b20240815.2", "b20240815.2" },
			{ "b20240815beta", "b20240815beta" },
			{ "b20240815cuttingedge", "b20240815cuttingedge" }
		};
	}

	public static TheoryData<string, int?, int, string> ToStringCases()
	{
		return new TheoryData<string, int?, int, string>
		{
			{ "2024-08-15", null, (int)ClientStream.Stable, "b20240815" },
			{ "2024-08-15", 2, (int)ClientStream.Stable, "b20240815.2" },
			{ "2024-08-15", null, (int)ClientStream.Beta, "b20240815beta" },
			{ "2024-08-15", null, (int)ClientStream.CuttingEdge, "b20240815cuttingedge" },
			{ "2024-08-15", null, (int)ClientStream.Dev, "b20240815dev" },
			{ "2024-08-15", null, (int)ClientStream.Tourney, "b20240815tourney" },
			{ "2024-08-15", 3, (int)ClientStream.Dev, "b20240815.3dev" }
		};
	}

	[Theory]
	[MemberData(nameof(ValidBuilds))]
	public void Parse_DocumentedBuildText_ParsesDateRevisionAndStream(
		string text,
		string expectedDate,
		int? expectedRevision,
		int expectedStream)
	{
		var build = ClientBuild.Parse(text);

		Assert.Equal(DateOnly.ParseExact(expectedDate, "yyyy-MM-dd"), build.Date);
		Assert.Equal(expectedRevision, build.Revision);
		Assert.Equal((ClientStream)expectedStream, build.Stream);
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void TryParse_MalformedBuildText_ReturnsFalse(string text)
	{
		Assert.False(ClientBuild.TryParse(text, out _));
	}

	[Fact]
	public void TryParse_NullText_ReturnsFalse()
	{
		Assert.False(ClientBuild.TryParse(null, out _));
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void Parse_MalformedBuildText_ThrowsFormatException(string text)
	{
		Assert.Throws<FormatException>(() => ClientBuild.Parse(text));
	}

	[Theory]
	[MemberData(nameof(CanonicalText))]
	public void ToString_ParsedBuild_ReturnsCanonicalLowercaseWireText(string text, string expected)
	{
		var build = ClientBuild.Parse(text);

		Assert.Equal(expected, build.ToString());
	}

	[Theory]
	[MemberData(nameof(ToStringCases))]
	public void ToString_Build_ReturnsDocumentedWireText(
		string date,
		int? revision,
		int stream,
		string expected)
	{
		var build = new ClientBuild(DateOnly.ParseExact(date, "yyyy-MM-dd"), revision, (ClientStream)stream);

		Assert.Equal(expected, build.ToString());
	}
}