using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Bot;

public class BotBootstrapServiceTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private static User MakeUser(string name)
    {
        return new User(0, name, name.ToLowerInvariant(), 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null);
    }

    [Fact]
    public async Task BootstrapAsync_BotUserMissing_ReturnsNull()
    {
        _users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns((User?)null);
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions { CommandPrefix = "!" }), _clock);

        var result = await service.BootstrapAsync();

        Assert.Null(result);
        _sessionRegistry.DidNotReceiveWithAnyArgs().Add(null!);
    }

    [Fact]
    public async Task BootstrapAsync_NameMatchesConfig_RegistersSessionMarkedAsBot()
    {
        _users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns(MakeUser("BasilBot"));
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions { CommandPrefix = "!" }), _clock);

        var result = await service.BootstrapAsync();

        Assert.NotNull(result);
        Assert.True(result.IsBot);
        Assert.Equal("BasilBot", result.Name);
        _sessionRegistry.Received(1).Add(result);
        await _users.DidNotReceiveWithAnyArgs().UpdateNameAsync(0, null!, null!);
    }

    [Fact]
    public async Task BootstrapAsync_ConfiguredNameDiffers_RenamesUserAndUsesNewName()
    {
        _users.FetchByIdAsync(0, Arg.Any<CancellationToken>()).Returns(MakeUser("BasilBot"));
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions { Name = "TourneyBot", CommandPrefix = "!" }), _clock);

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
            Options.Create(new BotOptions { CommandPrefix = "!" }), _clock);

        var result = await service.BootstrapAsync();

        Assert.True(result!.InChannel("#osu"));
        Assert.True(osu.Contains(0));
    }
}