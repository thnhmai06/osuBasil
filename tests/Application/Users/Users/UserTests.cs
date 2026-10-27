using Basil.Application.Users.Users;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Users;

public sealed class UserTests
{
	public static IEnumerable<object[]> ManagedAreaCases => UserPermissionsGroups.Areas
		.Where(area => area.Manager != UserPermissions.None)
		.Select(area => new object[] { area.Permissions, area.Manager });

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
		var actor = await app.CreateUserAsync("OfflineMod", "password", UserPermissionsGroups.All);
		var by = await app.LoginAsync(actor);
		var target = await app.CreateUserAsync("OfflineTarget", "password");
		var offlineUser = app.Users.Of(target);

		var (result, _) = await offlineUser.ScopeTo(by).Restrictions.RestrictAsync(
			UserPermissions.ChatPost, null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(app.Users.Online.ContainsKey(target));
	}

	[Fact]
	public async Task EffectivePermissions_ActiveRestriction_SuspendsThePermission()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("SuspendedUser", "password");
		app.AddRestriction(record, UserPermissions.ChatPost);
		await app.LoginAsync(record);
		var user = app.Users.Of(record);

		Assert.True(record.Value.Permissions.Allows(UserPermissions.ChatPost));
		Assert.False(user.EffectivePermissions(app.Clock.GetUtcNow()).Allows(UserPermissions.ChatPost));
	}

	[Fact]
	public async Task EffectivePermissions_PermissionNeverGranted_LacksIt()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("NoPermission", "password");
		await app.LoginAsync(record);
		var user = app.Users.Of(record);

		Assert.False(user.EffectivePermissions(app.Clock.GetUtcNow()).Allows(UserPermissions.ServerConfigure));
	}

	[Fact]
	public async Task EffectivePermissions_RestrictionExpires_UsesAdvancedClock()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("ExpiryUser", "password");
		app.AddRestriction(record, UserPermissions.ChatPost, app.Clock.GetUtcNow().AddMinutes(1));
		await app.LoginAsync(record);
		var user = app.Users.Of(record);
		Assert.False(user.EffectivePermissions(app.Clock.GetUtcNow()).Allows(UserPermissions.ChatPost));

		app.Clock.Advance(TimeSpan.FromMinutes(1));

		Assert.True(user.EffectivePermissions(app.Clock.GetUtcNow()).Allows(UserPermissions.ChatPost));
	}

	[Theory]
	[MemberData(nameof(ManagedAreaCases))]
	public async Task EffectivePermissions_AreaManagerIsRestricted_KeepsAreaPermissions(
		UserPermissions area, UserPermissions manager)
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("AreaManager", "password",
			UserPermissionsGroups.Default | area | manager);
		app.AddRestriction(record, area);
		await app.LoginAsync(record);

		var effective = app.Users.Of(record).EffectivePermissions(app.Clock.GetUtcNow());

		Assert.True(effective.Allows(area));
	}
}