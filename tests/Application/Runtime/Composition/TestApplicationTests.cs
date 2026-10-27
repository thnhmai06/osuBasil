using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Users;
using Basil.Testing;

namespace Basil.Application.Runtime.Tests.Composition;

public sealed class TestApplicationTests
{
	[Fact]
	public async Task AddApplication_AllRootsResolveAndUserLogsIn_Succeeds()
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