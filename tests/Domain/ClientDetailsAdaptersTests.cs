using Basil.Domain.Bancho;
using Basil.Domain.Common;

namespace Basil.Domain.Tests;

/// <summary>Verifies the Domain rules for network-adapter fingerprints reported by osu! clients.</summary>
public class ClientDetailsAdaptersTests
{
	private static NetworkAdapters MakeAdapters(string adapters)
	{
		return new NetworkAdapters
		{
			Adapters = adapters,
			Hash = new Md5("00000000000000000000000000000000")
		};
	}

	[Fact]
	public void NetworkAdapters_WineSentinel_IsRecognized()
	{
		Assert.True(MakeAdapters("runningunderwine").IsRunningUnderWine);
	}

	[Fact]
	public void NetworkAdapters_NormalAdapterString_IsRetained()
	{
		var adapters = MakeAdapters("aa11bb22.cc33dd44.");

		Assert.Equal("aa11bb22.cc33dd44.", adapters.Adapters);
		Assert.False(adapters.IsRunningUnderWine);
	}

	[Fact]
	public void NetworkAdapters_MissingTrailingDelimiter_Throws()
	{
		Assert.Throws<FormatException>(() => MakeAdapters("aa11bb22"));
	}
}