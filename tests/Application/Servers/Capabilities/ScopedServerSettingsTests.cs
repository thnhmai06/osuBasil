using Basil.Application.Common.Settings;
using Basil.Application.Servers.Objects;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>
///     Changing the settings needs the privilege to manage them; widening the default privileges of new accounts also
///     needs those privileges.
/// </summary>
public sealed class ScopedServerSettingsTests
{
	private static ServerSettings WithDefault(UserPrivileges privileges)
	{
		return new ServerSettings
			{ Accounts = new ServerSettings.AccountSettings { DefaultPrivileges = privileges } };
	}

	[Fact]
	public async Task NoManagePrivilege_Denied()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Server.ScopeTo(player).Settings.UpdateAsync(new ServerSettings());

		Assert.Equal(UserPrivileges.Developer, result.Missing);
	}

	[Fact]
	public async Task GrantsUnheldDefaults_DeniedAsMissing()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "SettingsOnly",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer);

		var result = await app.Server.ScopeTo(manager).Settings.UpdateAsync(
			WithDefault(UserPrivilegesExtensions.Default | UserPrivileges.Owner));

		Assert.Equal(UserPrivileges.Owner, result.Missing);
	}

	[Fact]
	public async Task GrantsHeldDefaults_Accepted()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "Both",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer | UserPrivileges.Owner);

		var result = await app.Server.ScopeTo(manager).Settings.UpdateAsync(
			WithDefault(UserPrivilegesExtensions.Default | UserPrivileges.Owner));

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task OtherSettings_NoDefaultsNeeded()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "SettingsOnly",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer);

		var result = await app.Server.ScopeTo(manager).Settings.UpdateAsync(
			new ServerSettings { General = new ServerSettings.GeneralSettings { Motd = "hello" } });

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}
}