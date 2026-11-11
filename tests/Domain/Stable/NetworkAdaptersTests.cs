using Basil.Domain.Client;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;

namespace Basil.Domain.Tests.Stable;

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
	public void WineSentinel_Recognized()
	{
		Assert.True(MakeAdapters("runningunderwine").IsRunningUnderWine);
	}

	[Fact]
	public void NormalAdapter_Retained()
	{
		var adapters = MakeAdapters("aa11bb22.cc33dd44.");

		Assert.Equal("aa11bb22.cc33dd44.", adapters.Adapters);
		Assert.False(adapters.IsRunningUnderWine);
	}

	[Fact]
	public void MissingTrailingDelimiter_Throws()
	{
		Assert.Throws<FormatException>(() => MakeAdapters("aa11bb22"));
	}

	[Fact]
	public void NullAdapters_Throws()
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
	public void EmptyChunk_Throws(string adapters)
	{
		Assert.Throws<FormatException>(() => MakeAdapters(adapters));
	}

	[Fact]
	public void Default_NotUnderWine()
	{
		Assert.False(default(NetworkAdapters).IsRunningUnderWine);
	}
}