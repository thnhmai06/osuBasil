using Basil.Application.Users.Connections;
using Basil.Domain.Auth;

namespace Basil.Application.Users.Tests.Connections;

public sealed class SessionTypeTests
{
	[Fact]
	public void Values_AreDistinctPowersOfTwo()
	{
		Assert.Equal(1, (byte)SessionType.Osu);
		Assert.Equal(2, (byte)SessionType.OsuTourney);
		Assert.Equal(4, (byte)SessionType.Irc);
		Assert.Equal(8, (byte)SessionType.Api);
		Assert.Equal(16, (byte)SessionType.Bot);
	}
}
