using Basil.Server.Shared.Eventing;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Configuration;
using Basil.Server.Features.Bot;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Server.Features.Chat;
using Basil.Server.Tests.Features.Multiplayer.Packets;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Server.Tests.Features.Multiplayer;

/// <summary>
///     Covers <see cref="MatchLifecycle" />'s empty-room auto-close timer (plan B.6b): a room
///     left with zero seated players for a grace period is closed automatically, with a warning
///     announcement before the deadline and a cancel-notice if a player rejoins after the warning
///     went out. Timings come from the service's fixed constants, so only the lifecycle of the
///     pending timer (cancellation, restart on rejoin) is exercised here rather than the
///     real-time close sequence itself.
/// </summary>
public class EmptyRoomAutoCloseTests
{
	private readonly MultiplayerTestSupport.FakeChannelRegistry _channelRegistry = new();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly MultiplayerTestSupport.FakeMatchRepository _matchRepository = new();
	private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
	private MultiplayerTestSupport.FakeMatchRegistry _matchRegistry = null!;

	private (MatchMembership Membership, MatchLifecycle Lifecycle) MakeService()
	{
		_matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(_channelRegistry, _matchRepository);
		var channelMembership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		var matchBroadcast = new MatchBroadcast(_channelRegistry, channelMembership, _gameRegistry, _ircRegistry,
			Substitute.For<IMatchLiveEvents>(), Substitute.For<IBeatmapRepository>(),
			Substitute.For<IUserRepository>());
		var matchLifecycle = new MatchLifecycle(_matchRegistry, _channelRegistry, channelMembership, _gameRegistry,
			_matchRepository, Substitute.For<IMatchRoundEndOutbox>(), Substitute.For<IMatchLiveEvents>(),
			Substitute.For<IBeatmapRepository>(), matchBroadcast, _serviceProvider,
			NullLogger<MatchLifecycle>.Instance);
		var matchMembership = new MatchMembership(_channelRegistry, _gameRegistry, channelMembership,
			_matchRepository, matchLifecycle, NullLogger<MatchMembership>.Instance);
		_serviceProvider.GetService(typeof(MatchMembership)).Returns(matchMembership);
		return (matchMembership, matchLifecycle);
	}

	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private void RegisterAll(params GameSession[] sessions)
	{
		_gameRegistry.All.Returns(sessions);
		foreach (var session in sessions)
		{
			_gameRegistry.GetByUserId(session.Id).Returns(session);
			_gameRegistry.GetByUserId(session.Id).Returns(session);
		}
	}

	private static MatchState MakeMatchData(int hostId)
	{
		return new MatchState(
			0, false, 0, 0, "test match", "",
			"Some Map", 100, new string('a', 32),
			[], [], [], hostId, 0,
			0, 0, false, [], 0);
	}

	[Fact]
	public async Task Close_WhileEmptyRoomTimerPending_CancelsItCleanly()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var (membership, lifecycle) = MakeService();
		var match = (await lifecycle.CreateAsync(host, MakeMatchData(host.Id)))!;
		host.Dequeue();
		await membership.LeaveAsync(host, match);
		var pendingTimer = match.EmptyRoomTimer!;

		await lifecycle.CloseAsync(match, null, null, pendingTimer.Token);

		Assert.Null(match.EmptyRoomTimer);
		Assert.True(pendingTimer.IsCancellationRequested);
	}

	[Fact]
	public async Task EmptyRoom_EmptiedAgainAfterRejoin_StartsABrandNewTimer()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var (membership, lifecycle) = MakeService();
		var match = (await lifecycle.CreateAsync(host, MakeMatchData(host.Id)))!;
		host.Dequeue();

		await membership.LeaveAsync(host, match);
		var firstTimer = match.EmptyRoomTimer;
		await membership.JoinAsync(host, match, "", firstTimer!.Token);
		Assert.Null(match.EmptyRoomTimer);

		await membership.LeaveAsync(host, match, firstTimer.Token);
		var secondTimer = match.EmptyRoomTimer;

		Assert.NotNull(secondTimer);
		Assert.NotSame(firstTimer, secondTimer);
		Assert.False(match.EmptyRoomWarningSent);
	}
}