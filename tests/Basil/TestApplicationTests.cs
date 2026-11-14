using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Testing;

namespace Basil.Tests;

public sealed class TestApplicationTests
{
	[Fact]
	public async Task AllRootsResolve_LoginSucceeds()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("FixtureUser", "secret");

		var connection = await app.LoginAsync(user, password: "secret");

		Assert.IsType<ApiConnection>(connection);
		Assert.True(connection.IsOpen);
		Assert.True(app.Users.Online[user].IsOnline);
		Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>());
	}
}