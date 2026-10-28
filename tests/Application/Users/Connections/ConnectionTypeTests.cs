using Basil.Application.Users.Connections;
using Basil.Domain.Auth;

namespace Basil.Application.Users.Tests.Connections;

public sealed class ConnectionTypeTests
{
	[Fact]
	public void Values_AreDistinctPowersOfTwo()
	{
		Assert.Equal(1, (byte)ConnectionType.Osu);
		Assert.Equal(2, (byte)ConnectionType.Tourney);
		Assert.Equal(4, (byte)ConnectionType.Irc);
		Assert.Equal(8, (byte)ConnectionType.Api);
		Assert.Equal(16, (byte)ConnectionType.Bot);
	}
}
