using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Testing;
using Basil.Domain.Permissions;

namespace Basil.Application.Runtime.Tests.Users;

public sealed class ScopedUserTests
{
	[Fact]
	public async Task RestrictAsync_EqualPermissions_ReturnsNotOutranking()
	{
		using var app = new TestApplication();
		const Permissions permissions = PermissionsGroups.All;
		var actor = await app.CreateUserAsync("EqualActor", "password", permissions);
		var target = await app.CreateUserAsync("EqualTarget", "password", permissions);
		var by = await app.LoginAsync(actor);

		var (result, _) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(
			Permissions.ChatPost, null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
	}

	[Fact]
	public async Task RestrictAsync_TargetIsActor_ReturnsNotOutranking()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("SelfRestrict", "password", PermissionsGroups.All);
		var by = await app.LoginAsync(actor);

		var (result, restriction) = await app.Users.Of(actor).ScopeTo(by).Restrictions.RestrictAsync(
			Permissions.ChatPost, null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.Null(restriction);
	}

	[Fact]
	public async Task SetPermissionsAsync_GrantExceedsActorsPermissions_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var actorPermissions = PermissionsGroups.Default | Permissions.UserManage |
							   Permissions.UserManagePermissions;
		var actor = await app.CreateUserAsync("PermissionActor", "password", actorPermissions);
		var target = await app.CreateUserAsync("PermTarget", "password");
		var by = await app.LoginAsync(actor);

		var result = await app.Users.Of(target).ScopeTo(by).SetPermissionsAsync(
			target.Value.Permissions | Permissions.ServerConfigure, TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(Permissions.ServerConfigure, result.Missing);
		Assert.False(target.Value.Permissions.Allows(Permissions.ServerConfigure));
	}

	[Fact]
	public async Task RestrictAsync_Allowed_PublishesEventAndRecordsModeration()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("Moderator", "password", PermissionsGroups.All);
		var target = await app.CreateUserAsync("Restricted", "password");
		var by = await app.LoginAsync(actor);
		var end = app.Clock.GetUtcNow().AddHours(1);

		var (result, restriction) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(
			Permissions.ChatPost, end, "test reason", TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.NotNull(restriction);
		var restricted = Assert.IsType<UserRestricted>(app.UserEvents.Events.Last());
		Assert.Equal(target, restricted.User);
		Assert.Same(restriction, restricted.Restriction);
		var entry = Assert.Single(app.ModerationEntries);
		Assert.Equal(ModerationAction.Restrict, entry.Action);
		Assert.Equal(actor, entry.By);
		Assert.Equal(target, entry.Target);
		Assert.Equal("test reason", entry.Reason);
	}

	[Fact]
	public async Task LiftAsync_Allowed_EndsRestrictionAndPublishesEvent()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("LiftMod", "password", PermissionsGroups.All);
		var target = await app.CreateUserAsync("LiftedUser", "password");
		var by = await app.LoginAsync(actor);
		var scoped = app.Users.Of(target).ScopeTo(by);
		var (_, restriction) = await scoped.Restrictions.RestrictAsync(Permissions.ChatPost, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(restriction);

		var result = await scoped.Restrictions.LiftAsync(restriction,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal(app.Clock.GetUtcNow(), restriction.Value.EndsAt);
		var lifted = Assert.IsType<UserRestrictionLifted>(app.UserEvents.Events.Last());
		Assert.Equal(target, lifted.User);
		Assert.Same(restriction, lifted.Restriction);
		Assert.Equal(ModerationAction.Lift, app.ModerationEntries.Last().Action);
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

		Assert.True(user.ScopeTo(ownerConnection).SetAway("away"));
		Assert.True(user.ScopeTo(ownerConnection).SetPmPrivate(true));
		Assert.False(user.ScopeTo(otherConnection).SetAway("intruder"));
		Assert.False(user.ScopeTo(otherConnection).SetPmPrivate(false));

		Assert.Equal("away", user.AwayMessage);
		Assert.True(user.PmPrivate);
	}

	[Fact]
	public async Task DeleteAsync_DeletedAccount_StaysDeletedAndClosesConnections()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("DeleteModerator", "password", PermissionsGroups.All);
		var target = await app.CreateUserAsync("DeleteTarget", "password");
		var by = await app.LoginAsync(actor);
		var connection = await app.LoginAsync(target);
		var scoped = app.Users.Of(target).ScopeTo(by);

		Assert.True((await scoped.DeleteAsync(TestContext.Current.CancellationToken)).IsOk);
		var deletedAt = target.Value.DeletedAt;
		Assert.NotNull(deletedAt);
		Assert.False(connection.IsOpen);
		Assert.Equal(ConnectionCloseReason.Deleted,
			Assert.IsType<UserConnectionClosed>(app.UserEvents.Events.Last()).Reason);

		Assert.True((await scoped.DeleteAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.Equal(deletedAt, target.Value.DeletedAt);
	}
}