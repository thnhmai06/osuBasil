using Basil.Application;
using Basil.Application.Abstractions.Scores;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Scores;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Tests.DependencyInjection;

/// <summary>
///     Resolves the full Web-endpoint dependency graph (LoginService's 17 constructor deps,
///     BanchoPacketDispatcher's 9 handlers, session registries) from the real DI container. A
///     deep constructor graph like this is exactly what silently breaks at runtime without a test
///     like this one — unit tests on the individual pieces can't see a missing/misconfigured
///     registration in the composition root itself.
/// </summary>
public class CompositionRootTests
{
    private readonly ServiceProvider _provider;

    public CompositionRootTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Basil:Server:Domain"] = "test.local",
                ["Basil:Bot:CommandPrefix"] = "!",
                ["Basil:Server:MenuIconPath"] = "icon.png",
                ["Basil:Server:MenuOnclickUrl"] = "https://example.test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddApplication();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvesOsuLoginUseCase()
    {
        Assert.NotNull(_provider.GetRequiredService<LoginService>());
    }

    [Fact]
    public void ResolvesBanchoPacketDispatcherWithAllHandlers()
    {
        Assert.NotNull(_provider.GetRequiredService<BanchoPacketDispatcher>());
        Assert.Equal(44, _provider.GetServices<IBanchoPacketHandler>().Count());
    }

    [Fact]
    public void ResolvesSessionRegistriesAsSharedSingletons()
    {
        var registry1 = _provider.GetRequiredService<IPlayerSessionRegistry>();
        var registry2 = _provider.GetRequiredService<IPlayerSessionRegistry>();
        Assert.Same(registry1, registry2);
    }

    [Fact]
    public void ResolvesScoreSubmissionUseCase()
    {
        Assert.NotNull(_provider.GetRequiredService<ScoreSubmissionService>());
    }

    [Fact]
    public void ResolvesReplayServiceAndItsStorage()
    {
        Assert.NotNull(_provider.GetRequiredService<ReplayService>());
        Assert.NotNull(_provider.GetRequiredService<IReplayStorage>());
    }

    [Fact]
    public void ResolvesScoreDecryptor()
    {
        Assert.NotNull(_provider.GetRequiredService<IScoreDecryptor>());
    }

    [Fact]
    public void ResolvesSpectatorServiceAndChannelMembershipService()
    {
        Assert.NotNull(_provider.GetRequiredService<SpectatorService>());
        Assert.NotNull(_provider.GetRequiredService<ChannelMembershipService>());
    }

    [Fact]
    public void ResolvesMatchRegistryAsASharedSingleton()
    {
        var registry1 = _provider.GetRequiredService<IMatchRegistry>();
        var registry2 = _provider.GetRequiredService<IMatchRegistry>();
        Assert.Same(registry1, registry2);
    }
}
