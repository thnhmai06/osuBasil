using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that set, remove and list the friends and blocks the sender holds toward other accounts.</summary>
public sealed class RelationshipHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AddFriend_TargetBecomesFriend()
	{
		using var app = new TestApplication();
		var (actor, target) = await TwoAsync(app);

		var result = await new AddFriendHandler(app.Users).Handle(new AddFriend(actor, UserReference.ById(target.Record.Id)), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal([target.Record.Value.Name], await NamesAsync(new ListFriendsHandler(app.Users), actor));
	}

	[Fact]
	public async Task RemoveFriend_Ended()
	{
		using var app = new TestApplication();
		var (actor, target) = await TwoAsync(app);
		await new AddFriendHandler(app.Users).Handle(new AddFriend(actor, UserReference.ById(target.Record.Id)), Token);

		var result = await new RemoveFriendHandler(app.Users).Handle(new RemoveFriend(actor, UserReference.ById(target.Record.Id)), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Empty(await NamesAsync(new ListFriendsHandler(app.Users), actor));
	}

	[Fact]
	public async Task BlockUser_TargetBlocked()
	{
		using var app = new TestApplication();
		var (actor, target) = await TwoAsync(app);

		var result = await new BlockUserHandler(app.Users).Handle(new BlockUser(actor, UserReference.ById(target.Record.Id)), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal([target.Record.Value.Name], await NamesAsync(new ListBlocksHandler(app.Users), actor));
	}

	[Fact]
	public async Task UnblockUser_Lifted()
	{
		using var app = new TestApplication();
		var (actor, target) = await TwoAsync(app);
		await new BlockUserHandler(app.Users).Handle(new BlockUser(actor, UserReference.ById(target.Record.Id)), Token);

		var result = await new UnblockUserHandler(app.Users).Handle(new UnblockUser(actor, UserReference.ById(target.Record.Id)), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Empty(await NamesAsync(new ListBlocksHandler(app.Users), actor));
	}

	[Fact]
	public async Task FriendsOther_Refused()
	{
		using var app = new TestApplication();
		var (actor, target) = await TwoAsync(app);

		var (result, users) = await new ListFriendsHandler(app.Users).Handle(new ListFriends(actor, UserReference.ById(target.Record.Id)), Token);

		Assert.Equal(UserOutcome.NotSelf, result.Outcome);
		Assert.Empty(users);
	}

	[Fact]
	public async Task BlocksOther_Refused()
	{
		using var app = new TestApplication();
		var (actor, target) = await TwoAsync(app);

		var (result, users) = await new ListBlocksHandler(app.Users).Handle(new ListBlocks(actor, UserReference.ById(target.Record.Id)), Token);

		Assert.Equal(UserOutcome.NotSelf, result.Outcome);
		Assert.Empty(users);
	}

	private static async Task<(Connection Actor, User Target)> TwoAsync(
		TestApplication app)
	{
		var actor = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		var target = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));
		return (actor, target.User);
	}

	private static async Task<IReadOnlyList<string>> NamesAsync(ListFriendsHandler handler,
		Connection actor)
	{
		var (_, users) = await handler.Handle(new ListFriends(actor, UserReference.ById(actor.Record.Id)), Token);
		return [.. users.Select(user => user.Value.Name)];
	}

	private static async Task<IReadOnlyList<string>> NamesAsync(ListBlocksHandler handler,
		Connection actor)
	{
		var (_, users) = await handler.Handle(new ListBlocks(actor, UserReference.ById(actor.Record.Id)), Token);
		return [.. users.Select(user => user.Value.Name)];
	}
}
