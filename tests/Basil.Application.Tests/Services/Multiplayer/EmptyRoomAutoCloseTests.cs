using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

/// <summary>
///     Covers <see cref="MatchMembershipService" />'s empty-room auto-close timer (plan B.6b): a room
///     with zero seated players for a grace period is closed automatically, with a warning
///     announcement before the deadline and a cancel-notice if a player rejoins after the warning
///     went out. Uses short injected timings (see the <c>emptyRoomCloseSeconds</c>/
///     <c>emptyRoomWarnAtSeconds</c> constructor parameters) instead of the real 5-minute/60-second
///     defaults so these tests run in real time without mocking the clock.
/// </summary>
public class EmptyRoomAutoCloseTests
{
	private readonly MultiplayerTestSupport.FakeChannelRegistry _channelRegistry = new();
	private readonly MultiplayerTestSupport.FakeMatchRepository _matchRepository = new();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();
	private MultiplayerTestSupport.FakeMatchRegistry _matchRegistry = null!;

	private MatchMembershipService MakeService(int closeSeconds, int warnSeconds)
	{
		_matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(_channelRegistry, _matchRepository);
		return new MatchMembershipService(_matchRegistry, _channelRegistry, _sessionRegistry,
			new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions())),
			_matchRepository, Substitute.For<IMatchLiveEvents>(), Substitute.For<IBeatmapRepository>(),
			Substitute.For<IUserRepository>(), NullLogger<MatchMembershipService>.Instance,
			closeSeconds, warnSeconds);
	}

	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private void RegisterAll(params GameSession[] sessions)
	{
		_sessionRegistry.GameSessions.Returns(sessions);
		foreach (var session in sessions)
		{
			_sessionRegistry.GetGameByUserId(session.Id).Returns(session);
			_sessionRegistry.GetSessionsByUserId(session.Id).Returns([session]);
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

	private static string Text(byte[] packet)
	{
		// SendMessage packet body: senderName, text, recipient, senderId — all length-prefixed
		// strings; decoding the whole packet stream isn't needed, a substring search on the raw
		// UTF-8 bytes is enough to prove which announcement text arrived.
		return System.Text.Encoding.UTF8.GetString(packet);
	}

	[Fact]
	public async Task EmptyRoom_WarnsAtGraceEdge_DmsRefereeNotInChannel_ButNotOneAlreadyInChannel()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		var refInChannel = MakePlayer(2, "refinside");
		var refOutside = MakePlayer(3, "refoutside");
		RegisterAll(bot, host, refInChannel, refOutside);
		var service = MakeService(closeSeconds: 2, warnSeconds: 1);
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		match.AddReferee(refInChannel.Id);
		match.AddReferee(refOutside.Id);
		var channelMembership =
			new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions()));
		var chatChannel = _channelRegistry.GetByName(match.ChatChannelName)!;
		channelMembership.Join(refInChannel, chatChannel);
		host.Dequeue();
		refInChannel.Dequeue();

		await service.LeaveAsync(host, match);

		await Task.Delay(TimeSpan.FromMilliseconds(1300));

		var outsideText = Text(refOutside.Dequeue());
		Assert.Contains("will be closed in", outsideText);

		var insideText = Text(refInChannel.Dequeue());
		Assert.Contains("will be closed in", insideText);
		// Not duplicated: refInChannel already saw it via the channel broadcast, so it must appear
		// there exactly once, not once from the channel and once more from the per-referee DM.
		Assert.Single(System.Text.RegularExpressions.Regex.Matches(insideText, "will be closed in"));
	}

	[Fact]
	public async Task EmptyRoom_PlayerRejoinsBeforeWarning_CancelsSilently()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var service = MakeService(closeSeconds: 2, warnSeconds: 1);
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		match.AddReferee(host.Id); // stays reachable for DMs even after leaving the physical seat
		host.Dequeue();

		await service.LeaveAsync(host, match);
		var timerBeforeRejoin = match.EmptyRoomTimer;

		await Task.Delay(TimeSpan.FromMilliseconds(300)); // well before the 1s warning mark
		await service.JoinAsync(host, match, "");

		Assert.Null(match.EmptyRoomTimer);
		Assert.True(timerBeforeRejoin!.IsCancellationRequested);
		Assert.False(match.EmptyRoomWarningSent);

		await Task.Delay(TimeSpan.FromMilliseconds(1000));
		Assert.DoesNotContain("closed for inactivity", Text(host.Dequeue()));
	}

	[Fact]
	public async Task EmptyRoom_PlayerRejoinsAfterWarning_AnnouncesCancellation()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var service = MakeService(closeSeconds: 2, warnSeconds: 1);
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		match.AddReferee(host.Id);
		host.Dequeue();

		await service.LeaveAsync(host, match);
		await Task.Delay(TimeSpan.FromMilliseconds(1300)); // past the 1s warning mark
		host.Dequeue();
		Assert.True(match.EmptyRoomWarningSent);

		await service.JoinAsync(host, match, "");

		Assert.Contains("no longer be closed for inactivity", Text(host.Dequeue()));
		Assert.False(match.EmptyRoomWarningSent);
	}

	[Fact]
	public async Task EmptyRoom_TimerFires_AnnouncesThenClosesTheRoom()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var service = MakeService(closeSeconds: 1, warnSeconds: 0);
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		match.AddReferee(host.Id);
		var matchId = match.Id;
		host.Dequeue();

		await service.LeaveAsync(host, match);
		await Task.Delay(TimeSpan.FromMilliseconds(1300));

		// Announced before removal (SyncEmptyRoomTimer's loop calls AnnounceToRoomAndReferees, then
		// CloseAsync, in that order, under the same match.Lock — never observable out of order).
		Assert.Contains("Closing the room", Text(host.Dequeue()));
		Assert.Null(_matchRegistry.GetById(matchId));
	}

	[Fact]
	public async Task Close_WhileEmptyRoomTimerPending_CancelsItCleanly()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var service = MakeService(closeSeconds: 300, warnSeconds: 60);
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		host.Dequeue();
		await service.LeaveAsync(host, match);
		var pendingTimer = match.EmptyRoomTimer!;

		await service.CloseAsync(match, null, null);

		Assert.Null(match.EmptyRoomTimer);
		Assert.True(pendingTimer.IsCancellationRequested);
	}

	[Fact]
	public async Task EmptyRoom_EmptiedAgainAfterRejoin_StartsABrandNewTimer()
	{
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		var host = MakePlayer(1, "host");
		RegisterAll(bot, host);
		var service = MakeService(closeSeconds: 2, warnSeconds: 1);
		var match = (await service.CreateAsync(host, MakeMatchData(host.Id)))!;
		host.Dequeue();

		await service.LeaveAsync(host, match);
		var firstTimer = match.EmptyRoomTimer;
		await service.JoinAsync(host, match, "");
		Assert.Null(match.EmptyRoomTimer);

		await service.LeaveAsync(host, match);
		var secondTimer = match.EmptyRoomTimer;

		Assert.NotNull(secondTimer);
		Assert.NotSame(firstTimer, secondTimer);
		Assert.False(match.EmptyRoomWarningSent);
	}
}
