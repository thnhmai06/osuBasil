using Bancho.Application.Abstractions;
using Bancho.Application.DependencyInjection;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Authentication;
using Bancho.Application.UseCases.Scores;
using Bancho.Application.UseCases.Spectating;
using Bancho.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace Bancho.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Resolves the full Web-endpoint dependency graph (OsuLoginUseCase's 17 constructor deps,
/// BanchoPacketDispatcher's 9 handlers, session registries) from the real DI container. A
/// deep constructor graph like this is exactly what silently breaks at runtime without a test
/// like this one — unit tests on the individual pieces can't see a missing/misconfigured
/// registration in the composition root itself.
/// </summary>
public class CompositionRootTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4").Build();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();

        var redisEndpoint = _redis.GetConnectionString().Split(':');
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "localhost",
                ["Database:User"] = "root",
                ["Database:Password"] = "unused",
                ["Database:Name"] = "unused",
                ["Redis:Host"] = redisEndpoint[0],
                ["Redis:Port"] = redisEndpoint[1],
                ["ServerBehavior:Domain"] = "test.local",
                ["ServerBehavior:CommandPrefix"] = "!",
                ["ServerBehavior:MenuIconUrl"] = "https://example.test/icon.png",
                ["ServerBehavior:MenuOnclickUrl"] = "https://example.test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBanchoInfrastructure(configuration);
        services.AddBanchoApplication();
        _provider = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _redis.DisposeAsync().AsTask();

    [Fact]
    public void ResolvesOsuLoginUseCase()
    {
        Assert.NotNull(_provider.GetRequiredService<OsuLoginUseCase>());
    }

    [Fact]
    public void ResolvesBanchoPacketDispatcherWithAllHandlers()
    {
        Assert.NotNull(_provider.GetRequiredService<BanchoPacketDispatcher>());
        Assert.Equal(20, _provider.GetServices<IBanchoPacketHandler>().Count());
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
        Assert.NotNull(_provider.GetRequiredService<ScoreSubmissionUseCase>());
    }

    [Fact]
    public void ResolvesScoreSubmissionPersistence()
    {
        Assert.NotNull(_provider.GetRequiredService<IScoreSubmissionPersistence>());
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
