using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Bot;

public class BotBootstrapServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ISessionRegistry<GameSession> _sessionRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	private static User MakeUser(string name)
	{
		return new User(0, name, Country.Xx, UserPrivileges.Unrestricted, default);
	}

	[Fact]
	public async Task BootstrapAsync_BotUserMissing_ReturnsNull()
	{
		_users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns((User?)null);
		var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
			new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), _channelRegistry, Options.Create(new IrcOptions())),
			Options.Create(new BotOptions { CommandPrefix = "!" }), NullLogger<BotBootstrapService>.Instance);

		var result = await service.BootstrapAsync();

		Assert.Null(result);
		_sessionRegistry.DidNotReceiveWithAnyArgs().TryAdd(null!);
	}

	[Fact]
	public async Task BootstrapAsync_NameMatchesConfig_RegistersSessionMarkedAsBot()
	{
		_users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns(MakeUser("BasilBot"));
		var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
			new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), _channelRegistry, Options.Create(new IrcOptions())),
			Options.Create(new BotOptions { CommandPrefix = "!" }), NullLogger<BotBootstrapService>.Instance);

		var result = await service.BootstrapAsync();

		Assert.NotNull(result);
		Assert.True(result.IsBot);
		Assert.Equal("BasilBot", result.Name);
		_sessionRegistry.Received(1).TryAdd(result);
		await _users.DidNotReceiveWithAnyArgs().UpdateNameAsync(0, null!, null!);
	}

	[Fact]
	public async Task BootstrapAsync_ConfiguredNameDiffers_RenamesUserAndUsesNewName()
	{
		_users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns(MakeUser("BasilBot"));
		var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
			new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), _channelRegistry, Options.Create(new IrcOptions())),
			Options.Create(new BotOptions { Name = "TourneyBot", CommandPrefix = "!" }),
			NullLogger<BotBootstrapService>.Instance);

		var result = await service.BootstrapAsync();

		Assert.Equal("TourneyBot", result!.Name);
		await _users.Received(1).UpdateNameAsync(0, "TourneyBot", "tourneybot", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task BootstrapAsync_JoinsAllAutoJoinChannels()
	{
		_users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns(MakeUser("BasilBot"));
		var osu = new ChannelSession(1, "#osu", "General", 0, 0, true);
		_channelRegistry.AutoJoinChannels.Returns([osu]);
		var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
			new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), _channelRegistry, Options.Create(new IrcOptions())),
			Options.Create(new BotOptions { CommandPrefix = "!" }), NullLogger<BotBootstrapService>.Instance);

		var result = await service.BootstrapAsync();

		Assert.True(result!.InChannel("#osu"));
		Assert.True(osu.Contains(0));
	}

	[Fact]
	public async Task BootstrapAsync_ConfiguredCountry_SetsCountryOnSession()
	{
		_users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns(MakeUser("BasilBot"));
		var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
			new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), _channelRegistry, Options.Create(new IrcOptions())),
			Options.Create(new BotOptions { Name = "BasilBot", Country = "jp", CommandPrefix = "!" }),
			NullLogger<BotBootstrapService>.Instance);

		var result = await service.BootstrapAsync();

		Assert.NotNull(result);
		Assert.Equal(Country.Jp, result.Country);
	}
}