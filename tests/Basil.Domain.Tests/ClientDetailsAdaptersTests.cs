using Basil.Domain.Login;

namespace Basil.Domain.Tests;

/// <summary>
///     Verifies `ClientDetails.From` parses the adapters segment of a client hash string (splitting on dots, wine
///     sentinel).
/// </summary>
public class ClientDetailsAdaptersTests
{
	private static string MakeHash(string adaptersString)
	{
		return $"pathmd5:{adaptersString}:adaptersmd5:uninstallmd5:disksig:";
	}

	[Fact]
	public void From_WineSentinel_ReturnsWineTrue()
	{
		var client = ClientDetails.From(MakeHash("runningunderwine"));

		Assert.Equal(["runningunderwine"], client.Adapters);
		Assert.True(client.IsRunningUnderWine);
	}

	[Fact]
	public void From_NormalAdapterList_SplitsOnDot()
	{
		var client = ClientDetails.From(MakeHash("aa11bb22.cc33dd44."));

		Assert.Equal(["aa11bb22", "cc33dd44"], client.Adapters);
		Assert.False(client.IsRunningUnderWine);
	}

	[Fact]
	public void From_MissingTrailingDelimiter_Throws()
	{
		Assert.Throws<FormatException>(() => ClientDetails.From(MakeHash("aa11bb22")));
	}
}