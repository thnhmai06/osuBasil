using Basil.Server.Shared.Http;

namespace Basil.Server.Tests.Host;

/// <summary>
///     Verifies that the names offered to the local network are exactly the names the server serves.
/// </summary>
public class DomainAdvertisementTests
{
	[Fact]
	public void EveryServedHostNameIsOfferedForTheConfiguredDomain()
	{
		var names = BanchoHostGroups.HostNamesFor("basil.local");

		Assert.Equal(
		[
			"basil.local",
			"c.basil.local", "ce.basil.local", "c4.basil.local", "c5.basil.local", "c6.basil.local",
			"osu.basil.local", "b.basil.local", "a.basil.local", "api.basil.local", "assets.basil.local"
		], names);
	}

	/// <summary>
	///     The server answers on the equivalent ppy.sh hosts too, but claiming those on a shared
	///     network would answer for traffic that is not this server's.
	/// </summary>
	[Fact]
	public void NoPpyShHostIsEverOffered()
	{
		var names = BanchoHostGroups.HostNamesFor("basil.local");

		Assert.DoesNotContain(names, name => name.EndsWith("ppy.sh", StringComparison.OrdinalIgnoreCase));
	}
}
