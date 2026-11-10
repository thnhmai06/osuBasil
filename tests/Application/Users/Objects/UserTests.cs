using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

public sealed class UserTests
{
	[Fact]
	public async Task Of_OnlineAccount_ReturnsSameUserInstance()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("OnlineIdentity", "password");
		await app.LoginAsync(record);

		var first = app.Users.Of(record);
		var second = app.Users.Of(record);

		Assert.Same(first, second);
		Assert.True(first.IsOnline);
	}

	[Fact]
	public async Task Of_OfflineAccount_ReturnsTransientUserInstances()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("OfflineIdentity", "password");

		var first = app.Users.Of(record);
		var second = app.Users.Of(record);

		Assert.NotSame(first, second);
		Assert.False(first.IsOnline);
		Assert.False(second.IsOnline);
		Assert.False(app.Users.Online.ContainsKey(record));
	}

	[Fact]
	public async Task EffectivePrivileges_PermissionNeverGranted_LacksIt()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("NoPermission", "password");
		await app.LoginAsync(record);
		var user = app.Users.Of(record);

		Assert.False(user.EffectivePrivileges(app.Clock.GetUtcNow()).Allows(UserPrivileges.Developer));
	}
}