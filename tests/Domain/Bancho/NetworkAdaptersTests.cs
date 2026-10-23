using Basil.Domain.Bancho;
using Basil.Domain.Common;

namespace Basil.Domain.Tests.Bancho;

/// <summary>Verifies the Domain rules for network-adapter fingerprints reported by osu! clients.</summary>
public class NetworkAdaptersTests
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

	[Fact]
	public void NetworkAdapters_NullAdapters_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new NetworkAdapters
		{
			Adapters = null!,
			Hash = new Md5("00000000000000000000000000000000")
		});
	}

	[Theory]
	[InlineData(".")]
	[InlineData("a..b.")]
	public void NetworkAdapters_EmptyChunk_Throws(string adapters)
	{
		Assert.Throws<FormatException>(() => MakeAdapters(adapters));
	}

	[Fact]
	public void NetworkAdapters_Default_IsNotRunningUnderWine()
	{
		Assert.False(default(NetworkAdapters).IsRunningUnderWine);
	}
}