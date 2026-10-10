using Basil.Application.Irc;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Application.Bot;
using Basil.Application.Chat;
using Basil.Host.Bancho.Chat.Packets;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Infrastructure.Tests.Multiplayer.Packets;
using Basil.Application.Users;
using Basil.Application.Beatmaps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Infrastructure.Tests.Multiplayer;

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
	private readonly ILiveEventHub _hub = new LiveEventHub();
	private MultiplayerTestSupport.FakeMatchRegistry _matchRegistry = null!;

	private (MatchMembership Membership, MatchLifecycle Lifecycle) MakeService()
	{
		_matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(_channelRegistry, _matchRepository);
		var channelMembership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			new ChatNotifier(Options.Create(new IrcOptions())),
			new ChannelNotifier(_gameRegistry, _ircRegistry, Options.Create(new IrcOptions())),
			Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions()),
			Substitute.For<IUserCache>());
		var matchBroadcast = new MatchBroadcast(_channelRegistry, channelMembership,
			new MatchNotifier(_channelRegistry, channelMembership),
			new ChatNotifier(Options.Create(new IrcOptions())), _gameRegistry, _ircRegistry,
			_hub, Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>());
		var matchLifecycle = new MatchLifecycle(_matchRegistry, _channelRegistry, channelMembership,
			new MatchNotifier(_channelRegistry, channelMembership), _gameRegistry,
			_matchRepository, Substitute.For<IMatchRoundEndOutbox>(), _hub,
			Substitute.For<IBeatmapRepository>(), matchBroadcast, _serviceProvider,
			Substitute.For<IUserCache>(), NullLogger<MatchLifecycle>.Instance);
		var matchMembership = new MatchMembership(_channelRegistry, _gameRegistry, channelMembership,
			new MatchNotifier(_channelRegistry, channelMembership),
			_matchRepository, matchLifecycle, Substitute.For<IUserCache>(), NullLogger<MatchMembership>.Instance);
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

	private static MatchCreationData MakeMatchData(int hostId)
	{
		return new MatchCreationData(
			"test match", "", "Some Map", 100, new string('a', 32), hostId,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead,
			false, 0);
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