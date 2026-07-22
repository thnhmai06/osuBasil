using Basil.Application.BackgroundServices;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.BackgroundServices;

/// <summary>
///     Ported from app/bg_loops.py's _disconnect_ghosts: every OSU_CLIENT_MIN_PING_INTERVAL/3
///     seconds (100s), any player whose last_recv_time exceeds OSU_CLIENT_MIN_PING_INTERVAL (300s)
///     is force-logged-out. Only the per-tick check is unit tested here — the sleep loop itself
///     isn't (it's a thin `while(!token.IsCancellationRequested) { delay; RunOnce(); }` wrapper).
/// </summary>
public class GhostDisconnectServiceTests
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static PlayerSession MakeSession(int id, string token, DateTimeOffset lastRecvTime)
    {
        return new PlayerSession(id, $"player{id}", token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
            { LastRecvTime = lastRecvTime };
    }

    private static ChannelMembershipService MakeChannelMembership(IPlayerSessionRegistry registry)
    {
        return new ChannelMembershipService(registry, Substitute.For<IChannelRegistry>());
    }

    private static SpectatorService MakeSpectatorService(IPlayerSessionRegistry registry,
        IChannelRegistry? channelRegistry = null)
    {
        channelRegistry ??= Substitute.For<IChannelRegistry>();
        return new SpectatorService(channelRegistry, new ChannelMembershipService(registry, channelRegistry));
    }

    [Fact]
    public void RunOnce_SessionPastThreshold_IsRemovedFromRegistry()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
        registry.Add(stale);

        new GhostDisconnectService(registry, MakeChannelMembership(registry), MakeSpectatorService(registry))
            .RunOnce();

        Assert.Null(registry.GetByToken("stale-token"));
    }

    [Fact]
    public void RunOnce_SessionWithinThreshold_StaysConnected()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var fresh = MakeSession(1, "fresh-token", Now.AddSeconds(-299));
        registry.Add(fresh);

        new GhostDisconnectService(registry, MakeChannelMembership(registry), MakeSpectatorService(registry))
            .RunOnce();

        Assert.NotNull(registry.GetByToken("fresh-token"));
    }

    [Fact]
    public void RunOnce_BotSessionPastThreshold_IsNotRemoved()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var bot = new PlayerSession(1, "BanchoBot", "bot-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
            { LastRecvTime = Now.AddSeconds(-301), IsBot = true };
        registry.Add(bot);

        new GhostDisconnectService(registry, MakeChannelMembership(registry), MakeSpectatorService(registry))
            .RunOnce();

        Assert.NotNull(registry.GetByToken("bot-token"));
    }

    [Fact]
    public void RunOnce_DisconnectingUnrestrictedPlayer_BroadcastsLogoutToOthers()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
        var bystander = MakeSession(2, "bystander-token", Now);
        registry.Add(stale);
        registry.Add(bystander);

        new GhostDisconnectService(registry, MakeChannelMembership(registry), MakeSpectatorService(registry))
            .RunOnce();

        Assert.Equal(ServerPacketWriter.Logout(1), bystander.Dequeue());
    }

    [Fact]
    public void RunOnce_SessionPastThreshold_PartsItsChannelsAndNotifiesRemainingMembers()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var channelRegistry = Substitute.For<IChannelRegistry>();
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        channelRegistry.GetByName("#osu").Returns(channel);

        var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
        var bystander = MakeSession(2, "bystander-token", Now);
        channel.Join(stale.Id);
        channel.Join(bystander.Id);
        stale.JoinChannel("#osu");
        bystander.JoinChannel("#osu");
        registry.Add(stale);
        registry.Add(bystander);

        new GhostDisconnectService(registry, new ChannelMembershipService(registry, channelRegistry),
            MakeSpectatorService(registry, channelRegistry)).RunOnce();

        Assert.False(channel.Contains(stale.Id));
        Assert.False(stale.InChannel("#osu"));
    }

    [Fact]
    public void RunOnce_SessionPastThreshold_RemovesBotSpectateRelationship()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
            UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true };
        registry.Add(bot);
        var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
        stale.AddSpectator(bot);
        bot.Spectating = stale;
        registry.Add(stale);

        new GhostDisconnectService(registry, MakeChannelMembership(registry), MakeSpectatorService(registry))
            .RunOnce();

        Assert.Empty(stale.Spectators);
        Assert.Null(bot.Spectating);
    }

    /// <summary>
    ///     A trivial in-memory double (not the production InMemoryPlayerSessionRegistry, to keep
    ///     this Application-layer test free of an Infrastructure project reference).
    /// </summary>
    private sealed class InMemoryPlayerSessionRegistryTestDouble : IPlayerSessionRegistry
    {
        private readonly Dictionary<string, PlayerSession> _byToken = [];

        public void Add(PlayerSession session)
        {
            _byToken[session.Token] = session;
        }

        public void Remove(PlayerSession session)
        {
            _byToken.Remove(session.Token);
        }

        public PlayerSession? GetByToken(string token)
        {
            return _byToken.GetValueOrDefault(token);
        }

        public PlayerSession? GetById(int id)
        {
            return _byToken.Values.FirstOrDefault(s => s.Id == id);
        }

        public PlayerSession? GetByName(string name)
        {
            return _byToken.Values.FirstOrDefault(s => s.SafeName == User.MakeSafeName(name));
        }

        public IReadOnlyList<PlayerSession> All => _byToken.Values.ToList();
    }
}
