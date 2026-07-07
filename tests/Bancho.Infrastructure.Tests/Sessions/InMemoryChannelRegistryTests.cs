using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Infrastructure.Sessions;
using Bancho.Application.Abstractions.Channels;
using Bancho.Application.Sessions.Channels;

namespace Bancho.Infrastructure.Tests.Sessions;

/// <summary>
/// Ported from app/state/sessions.py's Channels collection — runtime channel registry, seeded
/// from the DB (IChannelRepository) at startup.
/// </summary>
public class InMemoryChannelRegistryTests
{
    [Fact]
    public void Seed_ThenGetByName_ReturnsSession()
    {
        var registry = new InMemoryChannelRegistry();
        registry.Seed([new Channel(1, "#osu", "General discussion.", 1, 2, true)]);

        var session = registry.GetByName("#osu");

        Assert.NotNull(session);
        Assert.Equal("General discussion.", session!.Topic);
    }

    [Fact]
    public void GetByName_Unknown_ReturnsNull()
    {
        var registry = new InMemoryChannelRegistry();

        Assert.Null(registry.GetByName("#does-not-exist"));
    }

    [Fact]
    public void AutoJoinChannels_OnlyIncludesAutoJoinTrue()
    {
        var registry = new InMemoryChannelRegistry();
        registry.Seed([
            new Channel(1, "#osu", "t", 1, 2, true),
            new Channel(2, "#lobby", "t", 1, 2, false),
        ]);

        var autoJoin = registry.AutoJoinChannels;

        Assert.Single(autoJoin);
        Assert.Equal("#osu", autoJoin[0].Name);
    }

    [Fact]
    public void All_ReturnsEverySeededChannel()
    {
        var registry = new InMemoryChannelRegistry();
        registry.Seed([
            new Channel(1, "#osu", "t", 1, 2, true),
            new Channel(2, "#lobby", "t", 1, 2, false),
        ]);

        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public void Add_ThenGetByName_ReturnsInstanceChannel()
    {
        var registry = new InMemoryChannelRegistry();
        var channel = new ChannelSession(0, "#spec_5", "topic", 0, 0, false, displayName: "#spectator", instance: true);

        registry.Add(channel);

        Assert.Same(channel, registry.GetByName("#spec_5"));
    }

    [Fact]
    public void Remove_ThenGetByName_ReturnsNull()
    {
        var registry = new InMemoryChannelRegistry();
        registry.Add(new ChannelSession(0, "#spec_5", "topic", 0, 0, false, instance: true));

        registry.Remove("#spec_5");

        Assert.Null(registry.GetByName("#spec_5"));
    }
}
