using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

public sealed class ScopedUserTests
{
	[Fact]
	public async Task GrantExceedsActor_NamesMissing()
	{
		using var app = new TestApplication();
		var actorPrivileges = UserPrivilegesExtensions.Default | UserPrivileges.Owner |
		                      UserPrivileges.Owner;
		var actorRecord = await app.CreateUserAsync("PermissionActor", "password", actorPrivileges);
		var target = await app.CreateUserAsync("PermTarget", "password");
		var actor = await app.LoginAsync(actorRecord);

		var result = await app.Users.Of(target).ScopeTo(actor).Privileges.SetAsync(
			target.Value.Privileges | UserPrivileges.Developer);

		Assert.False(result == UserOutcome.Ok);
		Assert.Equal(UserOutcome.Denied, result);
		Assert.False(target.Value.Privileges.Allows(UserPrivileges.Developer));
	}

	[Fact]
	public async Task AwayPmPrivate_OtherCannotChange()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("SettingsOwner", "password");
		var other = await app.CreateUserAsync("SettingsOther", "password");
		var ownerConnection = await app.LoginAsync(owner);
		var otherConnection = await app.LoginAsync(other);
		var user = app.Users.Of(owner);

		Assert.True(await user.ScopeTo(ownerConnection).Profile.SetAwayAsync("away"));
		Assert.True(await user.ScopeTo(ownerConnection).Profile.SetPmPrivateAsync(true));
		Assert.False(await user.ScopeTo(otherConnection).Profile.SetAwayAsync("intruder"));
		Assert.False(await user.ScopeTo(otherConnection).Profile.SetPmPrivateAsync(false));

		Assert.Equal("away", user.AwayMessage);
		Assert.True(user.PmPrivate);
	}

	[Fact]
	public async Task DeletedAccount_StaysDeletedCloses()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("DeleteModerator", "password", UserPrivilegesExtensions.All);
		var target = await app.CreateUserAsync("DeleteTarget", "password");
		var actor = await app.LoginAsync(actorRecord);
		var connection = await app.LoginAsync(target);
		var scoped = app.Users.Of(target).ScopeTo(actor);

		Assert.True(await scoped.DeleteAsync() == UserOutcome.Ok);
		var deletedAt = target.Value.DeletedAt;
		Assert.NotNull(deletedAt);
		Assert.False(connection.IsOpen);
		Assert.Equal(SessionEndReason.Deleted,
			Assert.IsType<UserConnectionClosed>(app.UserEvents.Events[^1]).Reason);

		Assert.True(await scoped.DeleteAsync() == UserOutcome.Ok);

		Assert.Equal(deletedAt, target.Value.DeletedAt);
	}
}