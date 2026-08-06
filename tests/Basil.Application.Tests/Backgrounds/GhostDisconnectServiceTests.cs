using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Backgrounds;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Application.Configurations;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Backgrounds;

/// <summary>
///     Ported from app/bg_loops.py's _disconnect_ghosts: every OSU_CLIENT_MIN_PING_INTERVAL/3
///     seconds (100s), any userSession whose last_recv_time exceeds OSU_CLIENT_MIN_PING_INTERVAL (300s)
///     is force-logged-out via the same <see cref="PlayerLogoutService" /> a graceful LOGOUT uses —
///     see GhostDisconnectService's own doc comment for why this is no longer a hand-rolled second
///     copy of that cleanup. Only the per-tick check is unit tested here — the sleep loop itself
///     isn't (it's a thin `while(!token.IsCancellationRequested) { delay; RunOnce(); }` wrapper).
/// </summary>
public class GhostDisconnectServiceTests
{
	private static DateTimeOffset Now => DateTimeOffset.UtcNow;

	private static GameSession MakeSession(int id, string token, DateTimeOffset lastRecvTime)
	{
		return new GameSession(id, $"player{id}", token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ LastRecvTime = lastRecvTime };
	}

	/// <summary>
	///     Builds a real PlayerLogoutService wired the same way DI wires it — none of the tests below
	///     put a userSession in a match, so MatchMembershipService is never actually exercised by them and
	///     can be built with throwaway fakes, matching MultiplayerTestSupport.Fixture's own pattern.
	/// </summary>
	private static PlayerLogoutService MakePlayerLogout(IUserSessionRegistry registry,
		IChannelRegistry? channelRegistry = null)
	{
		channelRegistry ??= Substitute.For<IChannelRegistry>();
		var channelMembership = new ChannelMembershipService(registry, channelRegistry,
			Options.Create(new IrcOptions()));
		var spectatorService = new SpectatorService(channelRegistry, channelMembership,
			NullLogger<SpectatorService>.Instance);
		var matchMembership = new MatchMembershipService(Substitute.For<IMatchRegistry>(), channelRegistry, registry,
			channelMembership, Substitute.For<IMatchRepository>(),
			Substitute.For<IMatchLiveEvents>(), Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
			NullLogger<MatchMembershipService>.Instance);
		return new PlayerLogoutService(registry, channelMembership, spectatorService, matchMembership,
			NullLogger<PlayerLogoutService>.Instance);
	}

	[Fact]
	public async Task RunOnce_SessionPastThreshold_IsRemovedFromRegistry()
	{
		var registry = new InMemoryUserSessionRegistryTestDouble();
		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		registry.TryAddGameSession(stale);

		await new GhostDisconnectService(registry, MakePlayerLogout(registry),
				NullLogger<GhostDisconnectService>.Instance)
			.RunOnce();

		Assert.Null(registry.GetGameByToken("stale-token"));
	}

	[Fact]
	public async Task RunOnce_SessionWithinThreshold_StaysConnected()
	{
		var registry = new InMemoryUserSessionRegistryTestDouble();
		var fresh = MakeSession(1, "fresh-token", Now.AddSeconds(-299));
		registry.TryAddGameSession(fresh);

		await new GhostDisconnectService(registry, MakePlayerLogout(registry),
				NullLogger<GhostDisconnectService>.Instance)
			.RunOnce();

		Assert.NotNull(registry.GetGameByToken("fresh-token"));
	}

	[Fact]
	public async Task RunOnce_BotSessionPastThreshold_IsNotRemoved()
	{
		var registry = new InMemoryUserSessionRegistryTestDouble();
		var bot = new GameSession(1, "BanchoBot", "bot-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ LastRecvTime = Now.AddSeconds(-301), IsBot = true };
		registry.TryAddGameSession(bot);

		await new GhostDisconnectService(registry, MakePlayerLogout(registry),
				NullLogger<GhostDisconnectService>.Instance)
			.RunOnce();

		Assert.NotNull(registry.GetGameByToken("bot-token"));
	}

	[Fact]
	public async Task RunOnce_DisconnectingUnrestrictedPlayer_BroadcastsLogoutToOthers()
	{
		var registry = new InMemoryUserSessionRegistryTestDouble();
		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		var bystander = MakeSession(2, "bystander-token", Now);
		registry.TryAddGameSession(stale);
		registry.TryAddGameSession(bystander);

		await new GhostDisconnectService(registry, MakePlayerLogout(registry),
				NullLogger<GhostDisconnectService>.Instance)
			.RunOnce();

		Assert.Equal(ServerPacketWriter.Logout(1), bystander.Dequeue());
	}

	[Fact]
	public async Task RunOnce_SessionPastThreshold_PartsItsChannelsAndNotifiesRemainingMembers()
	{
		var registry = new InMemoryUserSessionRegistryTestDouble();
		var channelRegistry = Substitute.For<IChannelRegistry>();
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		channelRegistry.GetByName("#osu").Returns(channel);

		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		var bystander = MakeSession(2, "bystander-token", Now);
		channel.Join(stale.Id);
		channel.Join(bystander.Id);
		stale.JoinChannel("#osu");
		bystander.JoinChannel("#osu");
		registry.TryAddGameSession(stale);
		registry.TryAddGameSession(bystander);

		await new GhostDisconnectService(registry, MakePlayerLogout(registry, channelRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.False(channel.Contains(stale.Id));
		Assert.False(stale.InChannel("#osu"));
	}

	[Fact]
	public async Task RunOnce_SessionPastThreshold_RemovesBotSpectateRelationship()
	{
		var registry = new InMemoryUserSessionRegistryTestDouble();
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		registry.TryAddGameSession(bot);
		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		stale.AddSpectator(bot);
		bot.Spectating = stale;
		registry.TryAddGameSession(stale);

		await new GhostDisconnectService(registry, MakePlayerLogout(registry),
				NullLogger<GhostDisconnectService>.Instance)
			.RunOnce();

		Assert.Empty(stale.Spectators);
		Assert.Null(bot.Spectating);
	}

	/// <summary>
	///     Regression test for the actual multiplayer-hang bug: a ghosted userSession whose slot was still
	///     SlotStatus.Playing previously kept that slot stuck forever (GhostDisconnectService never
	///     called into match-leave), permanently blocking every other userSession's MatchComplete from ever
	///     completing the round. Uses the real MatchMembershipService (via MultiplayerTestSupport.Fixture)
	///     so LeaveAsync's actual slot-reset behavior is exercised, not a fake.
	/// </summary>
	[Fact]
	public async Task RunOnce_GhostWasPlayingInMatch_LeavesMatchAndResetsSlot()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var ghost = MultiplayerTestSupport.MakePlayer(1, "ghost");
		ghost.LastRecvTime = Now.AddSeconds(-301);
		var survivor = MultiplayerTestSupport.MakePlayer(2, "survivor");
		fixture.RegisterAll(ghost, survivor);
		var match = fixture.CreateMatch(ghost);
		await fixture.MatchMembership.JoinAsync(survivor, match, "");
		var ghostSlot = match.GetSlot(ghost.Id)!;
		ghostSlot.Status = SlotStatus.Playing;

		var testChannelMembership = new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry,
			Options.Create(new IrcOptions()));
		var playerLogout = new PlayerLogoutService(fixture.SessionRegistry, testChannelMembership,
			new SpectatorService(fixture.ChannelRegistry, testChannelMembership,
				NullLogger<SpectatorService>.Instance),
			fixture.MatchMembership, NullLogger<PlayerLogoutService>.Instance);

		await new GhostDisconnectService(fixture.SessionRegistry, playerLogout,
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.Null(ghost.Match);
		Assert.Equal(SlotStatus.Open, ghostSlot.Status);
	}

	/// <summary>
	///     A trivial in-memory double (not the production InMemoryUserSessionRegistry, to keep
	///     this Application-layer test free of an Infrastructure project reference).
	/// </summary>
	private sealed class InMemoryUserSessionRegistryTestDouble : IUserSessionRegistry
	{
		private readonly Dictionary<string, UserSession> _byToken = [];

		public IReadOnlyCollection<UserSession> All => [.. _byToken.Values];
		public IReadOnlyCollection<GameSession> GameSessions => [.. _byToken.Values.OfType<GameSession>()];
		public IReadOnlyCollection<IrcSession> IrcSessions => [.. _byToken.Values.OfType<IrcSession>()];

		public bool TryAddGameSession(GameSession session)
		{
			if (_byToken.Values.OfType<GameSession>().Any(s => s.Id == session.Id)) return false;
			_byToken[session.Token] = session;
			return true;
		}

		public bool TryAddIrcSession(IrcSession session)
		{
			if (_byToken.Values.OfType<IrcSession>().Any(s => s.Id == session.Id)) return false;
			_byToken[session.Token] = session;
			return true;
		}

		public void Remove(UserSession session)
		{
			_byToken.Remove(session.Token);
		}

		public GameSession? GetGameByToken(string token)
		{
			return _byToken.GetValueOrDefault(token) as GameSession;
		}

		public GameSession? GetGameByUserId(int id)
		{
			return _byToken.Values.OfType<GameSession>().FirstOrDefault(s => s.Id == id);
		}

		public GameSession? GetGameByName(string name)
		{
			return _byToken.Values.OfType<GameSession>().FirstOrDefault(s => s.SafeName == User.MakeSafeName(name));
		}

		public IrcSession? GetIrcByUserId(int id)
		{
			return _byToken.Values.OfType<IrcSession>().FirstOrDefault(s => s.Id == id);
		}

		public IrcSession? GetIrcByName(string name)
		{
			return _byToken.Values.OfType<IrcSession>().FirstOrDefault(s => s.SafeName == User.MakeSafeName(name));
		}

		public IReadOnlyList<UserSession> GetSessionsByUserId(int id)
		{
			return [.. _byToken.Values.Where(s => s.Id == id)];
		}
	}
}