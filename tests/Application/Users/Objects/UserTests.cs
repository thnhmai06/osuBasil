using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

public sealed class UserTests
{
	[Fact]
	public async Task OnlineAccount_SameInstance()
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
	public async Task OfflineAccount_TransientInstances()
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
	public async Task UngrantedPermission_NotEffective()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("NoPermission", "password");
		await app.LoginAsync(record);
		var user = app.Users.Of(record);

		Assert.False(user.EffectivePrivileges(app.Clock.GetUtcNow()).Allows(UserPrivileges.Developer));
	}
}