using Microsoft.Extensions.Options;
using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Application.Configuration;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Application.UseCases.Bot;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Bot;

public class BotBootstrapServiceTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private static User MakeUser(string name)
    {
        return new User(1, name, name.ToLowerInvariant(), null, 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null,
            null);
    }

    [Fact]
    public async Task BootstrapAsync_BotUserMissing_ReturnsNull()
    {
        _users.FetchByIdAsync(1, Arg.Any<CancellationToken>()).Returns((User?)null);
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions()), _clock);

        var result = await service.BootstrapAsync();

        Assert.Null(result);
        _sessionRegistry.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task BootstrapAsync_NameMatchesConfig_RegistersSessionMarkedAsBot()
    {
        _users.FetchByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeUser("BanchoBot"));
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions()), _clock);

        var result = await service.BootstrapAsync();

        Assert.NotNull(result);
        Assert.True(result.IsBot);
        Assert.Equal("BanchoBot", result.Name);
        _sessionRegistry.Received(1).Add(result);
        await _users.DidNotReceiveWithAnyArgs().UpdateNameAsync(default, default!, default!);
    }

    [Fact]
    public async Task BootstrapAsync_ConfiguredNameDiffers_RenamesUserAndUsesNewName()
    {
        _users.FetchByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeUser("BanchoBot"));
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions { Name = "TourneyBot" }), _clock);

        var result = await service.BootstrapAsync();

        Assert.Equal("TourneyBot", result!.Name);
        await _users.Received(1).UpdateNameAsync(1, "TourneyBot", "tourneybot", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BootstrapAsync_JoinsAllAutoJoinChannels()
    {
        _users.FetchByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeUser("BanchoBot"));
        var osu = new ChannelSession(1, "#osu", "General", 0, 0, true);
        _channelRegistry.AutoJoinChannels.Returns([osu]);
        var service = new BotBootstrapService(_users, _sessionRegistry, _channelRegistry,
            Options.Create(new BotOptions()), _clock);

        var result = await service.BootstrapAsync();

        Assert.True(result!.InChannel("#osu"));
        Assert.True(osu.Contains(1));
    }
}
