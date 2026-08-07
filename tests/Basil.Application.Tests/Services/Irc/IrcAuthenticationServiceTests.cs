using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Basil.Application.Services.Irc;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Options;
using NSubstitute;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Application.Tests.Services.Irc;

/// <summary>
///     Verifies the IRC PASS/NICK/USER handshake in <see cref="IrcAuthenticationService" /> — the
///     dual-session login path: a real IRC client authenticates independently of any game session
///     for the same account, and two concurrent IRC logins for the same account can't both win.
/// </summary>
public class IrcAuthenticationServiceTests
{
	private const string Password = "hunter2";
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
	private readonly ISessionRegistry<IrcSession> _sessionRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();
	private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();

	public IrcAuthenticationServiceTests()
	{
		_channelRegistry.AutoJoinChannels.Returns([]);
		_sessionRegistry.TryAdd(Arg.Any<IrcSession>()).Returns(true);
	}

	private IrcAuthenticationService MakeService()
	{
		var options = Options.Create(new IrcOptions { Name = "basil.local" });
		var channelMembership =
			new ChannelMembershipService(_gameRegistry, _sessionRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		var queries = new IrcQueryService(_channelRegistry, _gameRegistry, _sessionRegistry, channelMembership,
			new MotdService(Substitute.For<ISettingsRepository>()), options);
		return new IrcAuthenticationService(_users, _sessionRegistry, _channelRegistry, channelMembership,
			queries, options, _passwordHasher, _tokenGenerator);
	}

	private void StubValidCredentials(int userId, string name)
	{
		_users.FetchByNameAsync(name, Arg.Any<CancellationToken>())
			.Returns(new User(userId, name, Country.Xx, UserPrivileges.Unrestricted, default));
		_users.FetchPasswordHashAsync(userId, Arg.Any<CancellationToken>()).Returns("stored-hash");
		_passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
	}

	[Fact]
	public async Task AuthenticateAsync_UnknownUser_Fails()
	{
		_users.FetchByNameAsync("ghost", Arg.Any<CancellationToken>()).Returns((User?)null);

		var outcome = await MakeService().AuthenticateAsync("ghost", Password, Substitute.For<IIrcConnection>());

		Assert.False(outcome.Success);
		Assert.Null(outcome.Session);
	}

	[Fact]
	public async Task AuthenticateAsync_NoStoredPasswordHash_Fails()
	{
		_users.FetchByNameAsync("alice", Arg.Any<CancellationToken>())
			.Returns(new User(1, "alice", Country.Xx, UserPrivileges.Unrestricted, default));
		_users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns((string?)null);

		var outcome = await MakeService().AuthenticateAsync("alice", Password, Substitute.For<IIrcConnection>());

		Assert.False(outcome.Success);
	}

	[Fact]
	public async Task AuthenticateAsync_WrongPassword_Fails()
	{
		_users.FetchByNameAsync("alice", Arg.Any<CancellationToken>())
			.Returns(new User(1, "alice", Country.Xx, UserPrivileges.Unrestricted, default));
		_users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns("stored-hash");
		_passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(false);

		var outcome = await MakeService().AuthenticateAsync("alice", "wrong", Substitute.For<IIrcConnection>());

		Assert.False(outcome.Success);
	}

	[Fact]
	public async Task AuthenticateAsync_CorrectPassword_CreatesIrcSessionAndRegistersIt()
	{
		StubValidCredentials(1, "alice");

		var outcome = await MakeService().AuthenticateAsync("alice", Password, Substitute.For<IIrcConnection>());

		Assert.True(outcome.Success);
		Assert.NotNull(outcome.Session);
		Assert.Equal(1, outcome.Session!.Id);
		_sessionRegistry.Received(1).TryAdd(Arg.Is<IrcSession>(s => s != null && s.Id == 1 && s.Name == "alice"));
	}

	[Fact]
	public async Task AuthenticateAsync_GameSessionSameAccountAlreadyOnline_StillSucceeds_DoesNotEvictIt()
	{
		// The whole point of splitting IrcSession/GameSession: an IRC login never touches, checks,
		// or evicts an existing GameSession for the same account — the two coexist independently.
		StubValidCredentials(1, "alice");

		var outcome = await MakeService().AuthenticateAsync("alice", Password, Substitute.For<IIrcConnection>());

		Assert.True(outcome.Success);
		_gameRegistry.DidNotReceiveWithAnyArgs().GetByUserId(default);
		_gameRegistry.DidNotReceiveWithAnyArgs().GetByName(default!);
		_gameRegistry.DidNotReceive().Remove(Arg.Any<GameSession>());
	}

	[Fact]
	public async Task AuthenticateAsync_AnotherIrcSessionSameAccountAlreadyRegistered_FailsWithNicknameInUse()
	{
		// TryAdd is the final, atomic authority — this is what makes two concurrent IRC
		// logins for the same account resolve to exactly one winner.
		StubValidCredentials(1, "alice");
		_sessionRegistry.TryAdd(Arg.Any<IrcSession>()).Returns(false);

		var outcome = await MakeService().AuthenticateAsync("alice", Password, Substitute.For<IIrcConnection>());

		Assert.False(outcome.Success);
		Assert.Null(outcome.Session);
		Assert.Contains(outcome.Messages, m => m.Command == "433"); // ERR_NICKNAMEINUSE
	}

	[Fact]
	public async Task AuthenticateAsync_JoinsAutoJoinChannelsAndBuildsWelcomeMessages()
	{
		StubValidCredentials(1, "alice");
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		_channelRegistry.AutoJoinChannels.Returns([channel]);

		var outcome = await MakeService().AuthenticateAsync("alice", Password, Substitute.For<IIrcConnection>());

		Assert.True(outcome.Success);
		Assert.True(channel.Contains(1));
		Assert.Contains(outcome.Messages, m => m.Command == "001"); // RPL_WELCOME
		Assert.Contains(outcome.Messages, m => m.Command == "332"); // RPL_TOPIC
	}
}
