using Basil.Domain.Authentication;

namespace Basil.Application.Users.Tests.Objects.Connections;

public sealed class SessionTypeTests
{
	[Fact]
	public void Values_AreDistinctPowersOfTwo()
	{
		Assert.Equal(1, (byte)SessionType.Stable);
		Assert.Equal(2, (byte)SessionType.StableTourney);
		Assert.Equal(4, (byte)SessionType.Irc);
		Assert.Equal(8, (byte)SessionType.Api);
		Assert.Equal(16, (byte)SessionType.Bot);
	}
}