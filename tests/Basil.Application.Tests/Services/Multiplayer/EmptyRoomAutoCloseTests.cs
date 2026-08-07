using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

/// <summary>
///     Covers <see cref="MatchMembershipService" />'s empty-room auto-close timer (plan B.6b): a room
///     left with zero seated players for a grace period is closed automatically, with a warning
///     announcement before the deadline and a cancel-notice if a player rejoins after the warning
///     went out. Timings come from the service's fixed constants, so only the lifecycle of the
///     pending timer (cancellation, restart on rejoin) is exercised here rather than the
///     real-time close sequence itself.
/// </summary>
public class EmptyRoomAutoCloseTests
{
	private readonly MultiplayerTestSupport.FakeChannelRegistry _channelRegistry = new();
	private readonly MultiplayerTestSupport.FakeMatchRepository _matchRepository = new();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private MultiplayerTestSupport.FakeMatchRegistry _matchRegistry = null!;

	private MatchMembershipService MakeService()
	{
		_matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(_channelRegistry, _matchRepository);
		return new MatchMembershipService(_matchRegistry, _channelRegistry, _gameRegistry, _ircRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry, Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			_matchRepository, Substitute.For<IMatchLiveEvents>(), Substitute.For<IBeatmapRepository>(),
			Substitute.For<IUserRepository>(), NullLogger<MatchMembershipService>.Instance);
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
		var service = MakeService();
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		host.Dequeue();
		await service.LeaveAsync(host, match);
		var pendingTimer = match.EmptyRoomTimer!;

		await service.CloseAsync(match, null, null, pendingTimer.Token);

		Assert.Null(match.EmptyRoomTimer);
		Assert.True(pendingTimer.IsCancellationRequested);
	}

	[Fact]
	public async Task EmptyRoom_EmptiedAgainAfterRejoin_StartsABrandNewTimer()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var service = MakeService();
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		host.Dequeue();

		await service.LeaveAsync(host, match);
		var firstTimer = match.EmptyRoomTimer;
		await service.JoinAsync(host, match, "", firstTimer!.Token);
		Assert.Null(match.EmptyRoomTimer);

		await service.LeaveAsync(host, match, firstTimer.Token);
		var secondTimer = match.EmptyRoomTimer;

		Assert.NotNull(secondTimer);
		Assert.NotSame(firstTimer, secondTimer);
		Assert.False(match.EmptyRoomWarningSent);
	}
}
