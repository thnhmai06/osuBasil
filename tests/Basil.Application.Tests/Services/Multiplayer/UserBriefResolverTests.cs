using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Login;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

public class UserBriefResolverTests
{
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	[Fact]
	public async Task ResolveAsync_OnlinePlayer_UsesLiveSessionWithoutTouchingUserRepository()
	{
		var session = new GameSession(7, "Alice", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			Country = Country.Vn
		};
		_gameRegistry.GetByUserId(7).Returns(session);

		var brief = await UserBriefResolver.ResolveAsync(7, _gameRegistry, _ircRegistry, _users);

		Assert.NotNull(brief);
		Assert.Equal(7, brief.Id);
		Assert.Equal("Alice", brief.Name);
		Assert.Equal(Country.Vn, brief.Country);
		await _users.DidNotReceive().FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ResolveAsync_OfflinePlayer_FallsBackToUserRepository()
	{
		_users.FetchByIdAsync(9, Arg.Any<CancellationToken>())
			.Returns(new User(9, "Carol", Country.Us, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));

		var brief = await UserBriefResolver.ResolveAsync(9, _gameRegistry, _ircRegistry, _users);

		Assert.NotNull(brief);
		Assert.Equal(9, brief.Id);
		Assert.Equal("Carol", brief.Name);
		Assert.Equal(Country.Us, brief.Country);
	}

	[Fact]
	public async Task ResolveAsync_UnknownUser_ReturnsNull()
	{
		_users.FetchByIdAsync(999, Arg.Any<CancellationToken>()).Returns((User?)null);

		var brief = await UserBriefResolver.ResolveAsync(999, _gameRegistry, _ircRegistry, _users);

		Assert.Null(brief);
	}
}