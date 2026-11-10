using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

public sealed class ScopedUserTests
{

	[Fact]
	public async Task SetPrivilegesAsync_GrantExceedsActorsPrivileges_NamesTheMissingPrivilege()
	{
		using var app = new TestApplication();
		var actorPrivileges = UserPrivilegesExtensions.Default | UserPrivileges.Owner |
		                       UserPrivileges.Owner;
		var actorRecord = await app.CreateUserAsync("PermissionActor", "password", actorPrivileges);
		var target = await app.CreateUserAsync("PermTarget", "password");
		var actor = await app.LoginAsync(actorRecord);

		var result = await app.Users.Of(target).ScopeTo(actor).Privileges.SetAsync(
			target.Value.Privileges | UserPrivileges.Developer, TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Developer, result.Missing);
		Assert.False(target.Value.Privileges.Allows(UserPrivileges.Developer));
	}

	[Fact]
	public async Task SetAwayAndPmPrivate_OtherConnectionCannotChangeUsersSettings()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("SettingsOwner", "password");
		var other = await app.CreateUserAsync("SettingsOther", "password");
		var ownerConnection = await app.LoginAsync(owner);
		var otherConnection = await app.LoginAsync(other);
		var user = app.Users.Of(owner);

		Assert.True(user.ScopeTo(ownerConnection).Profile.SetAway("away"));
		Assert.True(user.ScopeTo(ownerConnection).Profile.SetPmPrivate(true));
		Assert.False(user.ScopeTo(otherConnection).Profile.SetAway("intruder"));
		Assert.False(user.ScopeTo(otherConnection).Profile.SetPmPrivate(false));

		Assert.Equal("away", user.AwayMessage);
		Assert.True(user.PmPrivate);
	}

	[Fact]
	public async Task DeleteAsync_DeletedAccount_StaysDeletedAndClosesConnections()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("DeleteModerator", "password", UserPrivilegesExtensions.All);
		var target = await app.CreateUserAsync("DeleteTarget", "password");
		var actor = await app.LoginAsync(actorRecord);
		var connection = await app.LoginAsync(target);
		var scoped = app.Users.Of(target).ScopeTo(actor);

		Assert.True((await scoped.DeleteAsync(TestContext.Current.CancellationToken)).IsOk);
		var deletedAt = target.Value.DeletedAt;
		Assert.NotNull(deletedAt);
		Assert.False(connection.IsOpen);
		Assert.Equal(SessionEndReason.Deleted,
			Assert.IsType<UserConnectionClosed>(app.UserEvents.Events[^1]).Reason);

		Assert.True((await scoped.DeleteAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.Equal(deletedAt, target.Value.DeletedAt);
	}
}