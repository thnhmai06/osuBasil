using Bancho.Application.Abstractions;
using Bancho.Application.BackgroundServices;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;

namespace Bancho.Application.Tests.BackgroundServices;

/// <summary>
/// Ported from app/bg_loops.py's _disconnect_ghosts: every OSU_CLIENT_MIN_PING_INTERVAL/3
/// seconds (100s), any player whose last_recv_time exceeds OSU_CLIENT_MIN_PING_INTERVAL (300s)
/// is force-logged-out. Only the per-tick check is unit tested here — the sleep loop itself
/// isn't (it's a thin `while(!token.IsCancellationRequested) { delay; RunOnce(); }` wrapper).
/// </summary>
public class GhostDisconnectServiceTests
{
    private static PlayerSession MakeSession(int id, string token, double lastRecvTime) =>
        new(id, $"player{id}", token, Privileges.Unrestricted, 0.0) { LastRecvTime = lastRecvTime };

    [Fact]
    public void RunOnce_SessionPastThreshold_IsRemovedFromRegistry()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000));
        var stale = MakeSession(1, "stale-token", lastRecvTime: 1000 - 301);
        registry.Add(stale);

        new GhostDisconnectService(registry, clock).RunOnce();

        Assert.Null(registry.GetByToken("stale-token"));
    }

    [Fact]
    public void RunOnce_SessionWithinThreshold_StaysConnected()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000));
        var fresh = MakeSession(1, "fresh-token", lastRecvTime: 1000 - 299);
        registry.Add(fresh);

        new GhostDisconnectService(registry, clock).RunOnce();

        Assert.NotNull(registry.GetByToken("fresh-token"));
    }

    [Fact]
    public void RunOnce_DisconnectingUnrestrictedPlayer_BroadcastsLogoutToOthers()
    {
        var registry = new InMemoryPlayerSessionRegistryTestDouble();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000));
        var stale = MakeSession(1, "stale-token", lastRecvTime: 1000 - 301);
        var bystander = MakeSession(2, "bystander-token", lastRecvTime: 1000);
        registry.Add(stale);
        registry.Add(bystander);

        new GhostDisconnectService(registry, clock).RunOnce();

        Assert.Equal(ServerPacketWriter.Logout(1), bystander.Dequeue());
    }

    /// <summary>
    /// A trivial in-memory double (not the production InMemoryPlayerSessionRegistry, to keep
    /// this Application-layer test free of an Infrastructure project reference).
    /// </summary>
    private sealed class InMemoryPlayerSessionRegistryTestDouble : IPlayerSessionRegistry
    {
        private readonly Dictionary<string, PlayerSession> _byToken = [];

        public void Add(PlayerSession session) => _byToken[session.Token] = session;
        public void Remove(PlayerSession session) => _byToken.Remove(session.Token);
        public PlayerSession? GetByToken(string token) => _byToken.GetValueOrDefault(token);
        public PlayerSession? GetById(int id) => _byToken.Values.FirstOrDefault(s => s.Id == id);
        public PlayerSession? GetByName(string name) => _byToken.Values.FirstOrDefault(s => s.SafeName == Domain.SafeName.Make(name));
        public IReadOnlyList<PlayerSession> All => _byToken.Values.ToList();
    }
}
