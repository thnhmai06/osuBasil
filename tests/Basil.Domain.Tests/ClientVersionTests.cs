using Basil.Domain.Client;

namespace Basil.Domain.Tests;

/// <summary>
///     Verifies `ClientVersion.From` parses osu! client version strings into date, revision, and stream.
/// </summary>
public class ClientVersionTests
{
	[Fact]
	public void From_WithRevisionAndStream_ParsesAllParts()
	{
		var version = ClientVersion.From("b20231231.1cuttingedge");

		Assert.Equal(new DateOnly(2023, 12, 31), version.Date);
		Assert.Equal(1, version.Revision);
		Assert.Equal(ClientVersionStream.CuttingEdge, version.Stream);
	}

	[Fact]
	public void From_DateOnly_DefaultsToStableStreamAndNullRevision()
	{
		var version = ClientVersion.From("b20231231");

		Assert.Equal(new DateOnly(2023, 12, 31), version.Date);
		Assert.Null(version.Revision);
		Assert.Equal(ClientVersionStream.Stable, version.Stream);
	}

	[Fact]
	public void From_TourneyStream_Recognized()
	{
		var version = ClientVersion.From("b20200201.2tourney");

		Assert.Equal(2, version.Revision);
		Assert.Equal(ClientVersionStream.Tourney, version.Stream);
	}

	[Fact]
	public void From_StreamWithoutRevision_Recognized()
	{
		var version = ClientVersion.From("b20231231beta");

		Assert.Null(version.Revision);
		Assert.Equal(ClientVersionStream.Beta, version.Stream);
	}

	[Theory]
	[InlineData("invalid")]
	[InlineData("b2020")]
	[InlineData("b20231231.")]
	public void From_Malformed_Throws(string input)
	{
		Assert.Throws<ArgumentException>(() => ClientVersion.From(input));
	}
}