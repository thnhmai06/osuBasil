using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Auth;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

public sealed class ScopedUserTests
{
	[Fact]
	public async Task RestrictAsync_EqualPermissions_ReturnsNotOutranking()
	{
		using var app = new TestApplication();
		const UserPermissions permissions = UserPermissionsGroups.All;
		var actor = await app.CreateUserAsync("EqualActor", "password", permissions);
		var target = await app.CreateUserAsync("EqualTarget", "password", permissions);
		var by = await app.LoginAsync(actor);

		var (result, _) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(
			UserPermissions.ChatPost, null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
	}

	[Fact]
	public async Task RestrictAsync_TargetIsActor_ReturnsNotOutranking()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("SelfRestrict", "password", UserPermissionsGroups.All);
		var by = await app.LoginAsync(actor);

		var (result, restriction) = await app.Users.Of(actor).ScopeTo(by).Restrictions.RestrictAsync(
			UserPermissions.ChatPost, null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.Null(restriction);
	}

	[Fact]
	public async Task SetPermissionsAsync_GrantExceedsActorsPermissions_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var actorPermissions = UserPermissionsGroups.Default | UserPermissions.UserManage |
		                       UserPermissions.UserManagePermissions;
		var actor = await app.CreateUserAsync("PermissionActor", "password", actorPermissions);
		var target = await app.CreateUserAsync("PermTarget", "password");
		var by = await app.LoginAsync(actor);

		var result = await app.Users.Of(target).ScopeTo(by).Permissions.SetAsync(
			target.Value.Permissions | UserPermissions.ServerManageSettings, TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ServerManageSettings, result.Missing);
		Assert.False(target.Value.Permissions.Allows(UserPermissions.ServerManageSettings));
	}

	[Fact]
	public async Task RestrictAsync_Allowed_PublishesEventAndRecordsModeration()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("Moderator", "password", UserPermissionsGroups.All);
		var target = await app.CreateUserAsync("Restricted", "password");
		var by = await app.LoginAsync(actor);
		var end = app.Clock.GetUtcNow().AddHours(1);

		var (result, restriction) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(
			UserPermissions.ChatPost, end, "test reason", TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.NotNull(restriction);
		var restricted = Assert.IsType<UserRestricted>(app.UserEvents.Events[^1]);
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
		var actor = await app.CreateUserAsync("LiftMod", "password", UserPermissionsGroups.All);
		var target = await app.CreateUserAsync("LiftedUser", "password");
		var by = await app.LoginAsync(actor);
		var scoped = app.Users.Of(target).ScopeTo(by);
		var (_, restriction) = await scoped.Restrictions.RestrictAsync(UserPermissions.ChatPost, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(restriction);

		var result = await scoped.Restrictions.LiftAsync(restriction,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal(app.Clock.GetUtcNow(), restriction.Value.EndsAt);
		var lifted = Assert.IsType<UserRestrictionLifted>(app.UserEvents.Events[^1]);
		Assert.Equal(target, lifted.User);
		Assert.Same(restriction, lifted.Restriction);
		Assert.Equal(ModerationAction.Lift, app.ModerationEntries[^1].Action);
	}

	[Fact]
	public async Task LiftAsync_KeepsTheRestrictionInTheHistory()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("LiftHistory", "password", UserPermissionsGroups.All);
		var target = await app.CreateUserAsync("LiftHistTarget", "password");
		var scoped = app.Users.Of(target).ScopeTo(await app.LoginAsync(actor));
		var (_, restriction) = await scoped.Restrictions.RestrictAsync(UserPermissions.ChatPost, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True((await scoped.Restrictions.LiftAsync(restriction!,
			cancellationToken: TestContext.Current.CancellationToken)).IsOk);

		var history = await scoped.Restrictions.ListAsync(TestContext.Current.CancellationToken);

		Assert.Same(restriction, Assert.Single(history));
		Assert.Equal(app.Clock.GetUtcNow(), restriction!.Value.EndsAt);
	}

	[Fact]
	public async Task LiftAsync_WithoutUserManageRestrict_NamesTheMissingPermissionAndKeepsTheRestriction()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var target = await app.CreateUserAsync("LiftDenied", "password");
		var (_, restriction) = await app.Users.Of(target).ScopeTo(admin).Restrictions.RestrictAsync(
			UserPermissions.ChatPost, null, cancellationToken: TestContext.Current.CancellationToken);
		var weak = await app.LoginAsync(await app.CreateUserAsync("Weak", "password"));

		var result = await app.Users.Of(target).ScopeTo(weak).Restrictions.LiftAsync(restriction!,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.UserManageRestrict, result.Missing);
		Assert.Null(restriction!.Value.EndsAt);
	}

	[Fact]
	public async Task LiftAsync_ByStaffOfEqualRank_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var peer = await app.CreateUserAsync("PeerAdmin", "password", UserPermissionsGroups.All);
		var restriction = app.AddRestriction(peer, UserPermissions.ChatPost);

		var result = await app.Users.Of(peer).ScopeTo(admin).Restrictions.LiftAsync(restriction,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.Null(restriction.Value.EndsAt);
	}

	[Fact]
	public async Task DeleteAsync_Allowed_RemovesRestrictionFromHistoryAndRecordsModeration()
	{
		using var app = new TestApplication();
		var moderator = await app.CreateUserAsync("DeleteRestrict", "password", UserPermissionsGroups.All);
		var target = await app.CreateUserAsync("DelRestrictTgt", "password");
		var by = await app.LoginAsync(moderator);
		var scoped = app.Users.Of(target).ScopeTo(by);
		var (_, restriction) = await scoped.Restrictions.RestrictAsync(UserPermissions.ChatPost, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(restriction);

		var result = await scoped.Restrictions.DeleteAsync(restriction,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Empty(await scoped.Restrictions.ListAsync(TestContext.Current.CancellationToken));
		Assert.Equal(ModerationAction.Delete, app.ModerationEntries[^1].Action);
		var deleted = Assert.IsType<UserRestrictionDeleted>(app.UserEvents.Events[^1]);
		Assert.Equal(target, deleted.User);
		Assert.Same(restriction, deleted.Restriction);
	}

	[Fact]
	public async Task DeleteAsync_WithoutUserManage_NamesMissingPermissionAndKeepsRestriction()
	{
		using var app = new TestApplication();
		var moderator = await app.CreateUserAsync("RestrictOnly", "password",
			UserPermissionsGroups.Default | UserPermissions.UserManageRestrict);
		var target = await app.CreateUserAsync("DeleteTarget", "password");
		var by = await app.LoginAsync(moderator);
		var scoped = app.Users.Of(target).ScopeTo(by);
		var (_, restriction) = await scoped.Restrictions.RestrictAsync(UserPermissions.ChatPost, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(restriction);

		var result = await scoped.Restrictions.DeleteAsync(restriction,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.UserManage, result.Missing);
		Assert.Single(await scoped.Restrictions.ListAsync(TestContext.Current.CancellationToken));
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
		var actor = await app.CreateUserAsync("DeleteModerator", "password", UserPermissionsGroups.All);
		var target = await app.CreateUserAsync("DeleteTarget", "password");
		var by = await app.LoginAsync(actor);
		var connection = await app.LoginAsync(target);
		var scoped = app.Users.Of(target).ScopeTo(by);

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
