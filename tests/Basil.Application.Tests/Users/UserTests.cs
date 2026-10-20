using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Application.Tests.Support;
using Basil.Domain.Permissions;

namespace Basil.Application.Tests.Users;

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
	public async Task RestrictAsync_OfflineAccount_DoesNotBringItOnline()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("OfflineMod", "password", PermissionsGroups.All);
		var by = await app.LoginAsync(actor);
		var target = await app.CreateUserAsync("OfflineTarget", "password");
		var offlineUser = app.Users.Of(target);

		var (result, _) = await offlineUser.ScopeTo(by).Restrictions.RestrictAsync(
			Permissions.ChatPost, null, ct: TestContext.Current.CancellationToken);

		Assert.Equal(UserResult.Ok, result);
		Assert.False(app.Users.Online.ContainsKey(target));
	}

	[Fact]
	public async Task Check_ActiveRestriction_ReturnsSuspended()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("SuspendedUser", "password");
		app.AddRestriction(record, Permissions.ChatPost);
		await app.LoginAsync(record);
		var user = app.Users.Of(record);

		Assert.Equal(Access.Suspended, user.Check(Permissions.ChatPost, app.Clock.GetUtcNow()));
		Assert.False(user.EffectivePermissions(app.Clock.GetUtcNow()).Allows(Permissions.ChatPost));
	}

	[Fact]
	public async Task Check_PermissionNeverGranted_ReturnsNotGranted()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("NoPermission", "password");
		await app.LoginAsync(record);
		var user = app.Users.Of(record);

		Assert.Equal(Access.NotGranted, user.Check(Permissions.ServerConfigure, app.Clock.GetUtcNow()));
	}

	[Fact]
	public async Task EffectivePermissions_RestrictionExpires_UsesAdvancedClock()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("ExpiryUser", "password");
		app.AddRestriction(record, Permissions.ChatPost, app.Clock.GetUtcNow().AddMinutes(1));
		await app.LoginAsync(record);
		var user = app.Users.Of(record);
		Assert.Equal(Access.Suspended, user.Check(Permissions.ChatPost, app.Clock.GetUtcNow()));

		app.Clock.Advance(TimeSpan.FromMinutes(1));

		Assert.Equal(Access.Allowed, user.Check(Permissions.ChatPost, app.Clock.GetUtcNow()));
	}

	public static IEnumerable<object[]> ManagedAreaCases => PermissionsGroups.Areas
		.Where(area => area.Manager != Permissions.None)
		.Select(area => new object[] { area.Permissions, area.Manager });

	[Theory]
	[MemberData(nameof(ManagedAreaCases))]
	public async Task EffectivePermissions_AreaManagerIsRestricted_KeepsAreaPermissions(
		Permissions area, Permissions manager)
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("AreaManager", "password",
			PermissionsGroups.Default | area | manager);
		app.AddRestriction(record, area);
		await app.LoginAsync(record);

		var effective = app.Users.Of(record).EffectivePermissions(app.Clock.GetUtcNow());

		Assert.True(effective.Allows(area));
	}
}