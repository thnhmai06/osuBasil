using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>
///     Changing the settings needs the right to manage them; widening the default permissions of new accounts also
///     needs those permissions.
/// </summary>
public sealed class ScopedServerSettingsTests
{
	private static ServerSettings WithDefault(UserPermissions permissions)
	{
		return new ServerSettings
			{ Accounts = new ServerSettings.AccountSettings { DefaultPermissions = permissions } };
	}

	[Fact]
	public async Task UpdateAsync_WithoutServerManageSettings_IsDenied()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Server.ScopeTo(player).Settings.UpdateAsync(new ServerSettings(),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ServerManageSettings, result.Missing);
	}

	[Fact]
	public async Task UpdateAsync_GrantingDefaultPermissionsTheCallerLacks_IsDeniedWithThoseAsMissing()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "SettingsOnly",
			UserPermissionsGroups.Default | UserPermissions.ServerManageSettings);

		var result = await app.Server.ScopeTo(manager).Settings.UpdateAsync(
			WithDefault(UserPermissionsGroups.Default | UserPermissions.UserManage),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task UpdateAsync_GrantingDefaultPermissionsTheCallerHolds_IsAccepted()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "Both",
			UserPermissionsGroups.Default | UserPermissions.ServerManageSettings | UserPermissions.UserManage);

		var result = await app.Server.ScopeTo(manager).Settings.UpdateAsync(
			WithDefault(UserPermissionsGroups.Default | UserPermissions.UserManage),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task UpdateAsync_ChangingOtherSettings_NeedsNoDefaultPermissionsHeld()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "SettingsOnly",
			UserPermissionsGroups.Default | UserPermissions.ServerManageSettings);

		var result = await app.Server.ScopeTo(manager).Settings.UpdateAsync(
			new ServerSettings { General = new ServerSettings.GeneralSettings { Motd = "hello" } },
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}
}