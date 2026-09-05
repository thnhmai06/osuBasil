using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Backgrounds;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Application.Tests.Packets;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Basil.Application.Tests.Backgrounds;

/// <summary>
///     Verifies the per-tick ghost check: a session whose last received time exceeds
///     OSU_CLIENT_MIN_PING_INTERVAL (300s) is force-logged-out through the same
///     <see cref="PlayerLogoutService" /> a graceful LOGOUT uses, checked every
///     OSU_CLIENT_MIN_PING_INTERVAL/3 seconds (100s). Only the per-tick check is unit tested —
///     the sleep loop itself is not.
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
	private static PlayerLogoutService MakePlayerLogout(
		ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry,
		IChannelRegistry? channelRegistry = null)
	{
		channelRegistry ??= Substitute.For<IChannelRegistry>();
		var channelMembership = new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		var spectatorService = new SpectatorService(channelRegistry, channelMembership,
			NullLogger<SpectatorService>.Instance);
		var matchMembership = new MatchMembershipService(Substitute.For<IMatchRegistry>(), channelRegistry,
			gameRegistry, ircRegistry,
			channelMembership, Substitute.For<IMatchRepository>(), Substitute.For<IMatchRoundEndOutbox>(),
			Substitute.For<IMatchLiveEvents>(), Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
			NullLogger<MatchMembershipService>.Instance);
		return new PlayerLogoutService(gameRegistry, ircRegistry, channelMembership, spectatorService, matchMembership,
			Substitute.For<IPlayerStatusEvents>(), NullLogger<PlayerLogoutService>.Instance);
	}

	[Fact]
	public async Task RunOnce_SessionPastThreshold_IsRemovedFromRegistry()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		gameRegistry.TryAdd(stale);

		await new GhostDisconnectService(gameRegistry, ircRegistry, MakePlayerLogout(gameRegistry, ircRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.Null(gameRegistry.GetByToken("stale-token"));
	}

	[Fact]
	public async Task RunOnce_SessionWithinThreshold_StaysConnected()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var fresh = MakeSession(1, "fresh-token", Now.AddSeconds(-299));
		gameRegistry.TryAdd(fresh);

		await new GhostDisconnectService(gameRegistry, ircRegistry, MakePlayerLogout(gameRegistry, ircRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.NotNull(gameRegistry.GetByToken("fresh-token"));
	}

	[Fact]
	public async Task RunOnce_BotSessionPastThreshold_IsNotRemoved()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var bot = new GameSession(1, "BanchoBot", "bot-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ LastRecvTime = Now.AddSeconds(-301), IsBot = true };
		gameRegistry.TryAdd(bot);

		await new GhostDisconnectService(gameRegistry, ircRegistry, MakePlayerLogout(gameRegistry, ircRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.NotNull(gameRegistry.GetByToken("bot-token"));
	}

	[Fact]
	public async Task RunOnce_DisconnectingUnrestrictedPlayer_BroadcastsLogoutToOthers()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		var bystander = MakeSession(2, "bystander-token", Now);
		gameRegistry.TryAdd(stale);
		gameRegistry.TryAdd(bystander);

		await new GhostDisconnectService(gameRegistry, ircRegistry, MakePlayerLogout(gameRegistry, ircRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.Equal(ServerPacketWriter.Logout(1), bystander.Dequeue());
	}

	[Fact]
	public async Task RunOnce_SessionPastThreshold_PartsItsChannelsAndNotifiesRemainingMembers()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var channelRegistry = Substitute.For<IChannelRegistry>();
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		channelRegistry.GetByName("#osu").Returns(channel);

		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		var bystander = MakeSession(2, "bystander-token", Now);
		channel.Join(stale.Id);
		channel.Join(bystander.Id);
		stale.JoinChannel("#osu");
		bystander.JoinChannel("#osu");
		gameRegistry.TryAdd(stale);
		gameRegistry.TryAdd(bystander);

		await new GhostDisconnectService(gameRegistry, ircRegistry,
			MakePlayerLogout(gameRegistry, ircRegistry, channelRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.False(channel.Contains(stale.Id));
		Assert.False(stale.InChannel("#osu"));
	}

	/// <summary>
	///     One session's reap throwing (e.g. a channel lookup blowing up mid-teardown) must not abort
	///     the sweep for every session after it, and must not propagate out of RunOnce — the default
	///     BackgroundService exception behavior would otherwise take the whole host down over one bad
	///     reap.
	/// </summary>
	[Fact]
	public async Task RunOnce_OneSessionReapThrows_StillReapsTheRest()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var channelRegistry = Substitute.For<IChannelRegistry>();
		channelRegistry.GetByName("#doomed").Throws(new InvalidOperationException("boom"));

		var poisoned = MakeSession(1, "poisoned-token", Now.AddSeconds(-301));
		poisoned.JoinChannel("#doomed");
		var stale = MakeSession(2, "stale-token", Now.AddSeconds(-301));
		gameRegistry.TryAdd(poisoned);
		gameRegistry.TryAdd(stale);

		await new GhostDisconnectService(gameRegistry, ircRegistry,
			MakePlayerLogout(gameRegistry, ircRegistry, channelRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.NotNull(gameRegistry.GetByToken("poisoned-token"));
		Assert.Null(gameRegistry.GetByToken("stale-token"));
	}

	[Fact]
	public async Task RunOnce_SessionPastThreshold_RemovesBotSpectateRelationship()
	{
		var gameRegistry = new GameSessionRegistryTestDouble();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		ircRegistry.All.Returns([]);
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		gameRegistry.TryAdd(bot);
		var stale = MakeSession(1, "stale-token", Now.AddSeconds(-301));
		stale.AddSpectator(bot);
		bot.Spectating = stale;
		gameRegistry.TryAdd(stale);

		await new GhostDisconnectService(gameRegistry, ircRegistry, MakePlayerLogout(gameRegistry, ircRegistry),
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.Empty(stale.Spectators);
		Assert.Null(bot.Spectating);
	}

	/// <summary>
	///     Regression test for the multiplayer-hang bug: a ghosted userSession whose slot is still
	///     SlotStatus.Playing must leave the match and reset its slot, otherwise the round can never
	///     complete. Uses the real MatchMembershipService (via MultiplayerTestSupport.Fixture)
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

		var testChannelMembership = new ChannelMembershipService(fixture.SessionRegistry,
			fixture.IrcSessionRegistry, fixture.ChannelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		var playerLogout = new PlayerLogoutService(fixture.SessionRegistry, fixture.IrcSessionRegistry,
			testChannelMembership,
			new SpectatorService(fixture.ChannelRegistry, testChannelMembership,
				NullLogger<SpectatorService>.Instance),
			fixture.MatchMembership, Substitute.For<IPlayerStatusEvents>(), NullLogger<PlayerLogoutService>.Instance);

		await new GhostDisconnectService(fixture.SessionRegistry, fixture.IrcSessionRegistry, playerLogout,
			NullLogger<GhostDisconnectService>.Instance).RunOnce();

		Assert.Null(ghost.Match);
		Assert.Equal(SlotStatus.Open, ghostSlot.Status);
	}

	/// <summary>
	///     A trivial in-memory double for <see cref="ISessionRegistry{TSession}" />, kept in the test
	///     file so this Application-layer test stays free of an Infrastructure project reference.
	/// </summary>
	private sealed class GameSessionRegistryTestDouble : ISessionRegistry<GameSession>
	{
		private readonly Dictionary<string, GameSession> _byToken = [];
		private readonly Dictionary<int, GameSession> _byUserId = [];

		public IReadOnlyCollection<GameSession> All => [.. _byToken.Values];

		public bool TryAdd(GameSession session)
		{
			if (!_byUserId.TryAdd(session.Id, session)) return false;
			_byToken[session.Token] = session;
			return true;
		}

		public void Remove(GameSession session)
		{
			if (!ReferenceEquals(_byUserId.GetValueOrDefault(session.Id), session)) return;
			_byUserId.Remove(session.Id);
			_byToken.Remove(session.Token);
		}

		public GameSession? GetByToken(string token)
		{
			return _byToken.GetValueOrDefault(token);
		}

		public GameSession? GetByUserId(int id)
		{
			return _byUserId.GetValueOrDefault(id);
		}

		public GameSession? GetByName(string name)
		{
			return _byToken.Values.FirstOrDefault(s => s.SafeName == User.MakeSafeName(name));
		}
	}
}