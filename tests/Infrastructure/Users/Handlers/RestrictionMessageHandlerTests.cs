using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that impose, lift and delete the restrictions of an account.</summary>
public sealed class RestrictionMessageHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RestrictUser_WithTheRight_ImposesTheRestrictionOnTheTarget()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var target = await app.CreateUserAsync("Target", "password");

		var (result, restriction) = await new RestrictUserHandler(app.Users)
			.Handle(new RestrictUser(admin, target, UserPermissions.ChatPost, null, "spam"), Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissions.ChatPost, restriction!.Value.Permissions);
		Assert.Contains(restriction, await app.Users.Of(target).ScopeTo(admin).Restrictions.ListAsync(Token));
	}

	[Fact]
	public async Task RestrictUser_WithoutTheRight_IsDeniedAndImposesNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Target", "password");

		var (result, restriction) = await new RestrictUserHandler(app.Users)
			.Handle(new RestrictUser(actor, target, UserPermissions.ChatPost, null, null), Token);

		Assert.NotNull(result.Missing);
		Assert.Null(restriction);
	}

	[Fact]
	public async Task LiftRestriction_WithTheRight_EndsTheRestriction()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var target = await app.CreateUserAsync("Target", "password");
		var restriction = app.AddRestriction(target, UserPermissions.ChatPost);

		var result = await new LiftRestrictionHandler(app.Users)
			.Handle(new LiftRestriction(admin, target, restriction), Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissions.None, ActivePermissions(app, target) & UserPermissions.ChatPost);
	}

	[Fact]
	public async Task LiftRestriction_WithoutTheRight_IsDeniedAndKeepsTheRestriction()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Target", "password");
		var restriction = app.AddRestriction(target, UserPermissions.ChatPost);

		var result = await new LiftRestrictionHandler(app.Users)
			.Handle(new LiftRestriction(actor, target, restriction), Token);

		Assert.NotNull(result.Missing);
	}

	[Fact]
	public async Task DeleteRestriction_WithoutTheRight_IsDenied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Target", "password");
		var restriction = app.AddRestriction(target, UserPermissions.ChatPost);

		var result = await new DeleteRestrictionHandler(app.Users)
			.Handle(new DeleteRestriction(actor, target, restriction), Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task DeleteRestriction_WithTheRight_RemovesItFromTheHistory()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var target = await app.CreateUserAsync("Target", "password");
		var restriction = app.AddRestriction(target, UserPermissions.ChatPost);

		var result = await new DeleteRestrictionHandler(app.Users)
			.Handle(new DeleteRestriction(admin, target, restriction), Token);

		Assert.True(result.IsOk);
		Assert.DoesNotContain(restriction, await app.Users.Of(target).ScopeTo(admin).Restrictions.ListAsync(Token));
	}

	private static UserPermissions ActivePermissions(TestApplication app, UserRecord target)
	{
		return app.Users.Of(target).Restrictions
			.Where(restriction => restriction.Value.IsActive(app.Clock.GetUtcNow()))
			.Aggregate(UserPermissions.None, (all, restriction) => all | restriction.Value.Permissions);
	}
}
