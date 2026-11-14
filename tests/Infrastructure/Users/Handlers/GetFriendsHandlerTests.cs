using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The message that lists the friends of the sender's own account.</summary>
public sealed class GetFriendsHandlerTests
{
	[Fact]
	public async Task GetFriends_ListsSenderFriends()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Mine", "password"));
		var friend = await app.LoginAsync(await app.CreateUserAsync("Friend", "password"));
		await friend.User.ScopeTo(actor).Relationships.AddFriendAsync();

		var (result, users) = await new GetFriendsHandler().Handle(new GetFriends(actor),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result);
		Assert.Equal(["Friend"], users.Select(user => user.Value.Name));
	}

	[Fact]
	public async Task NoFriend_OkEmpty()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Alone", "password"));

		var (result, users) = await new GetFriendsHandler().Handle(new GetFriends(actor),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserOutcome.Ok, result);
		Assert.Empty(users);
	}
}