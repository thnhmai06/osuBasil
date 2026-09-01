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
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Verifies the logout cleanup applied to a session: leaving matches and channels, removing the
///     player from the online player list, and broadcasting the removal.
/// </summary>
public class PlayerLogoutServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();

	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();

	private readonly MatchMembershipService _matchMembership = new(
		Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
		Substitute.For<ISessionRegistry<GameSession>>(),
		Substitute.For<ISessionRegistry<IrcSession>>(),
		new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
			Substitute.For<ISessionRegistry<IrcSession>>(), Substitute.For<IChannelRegistry>(),
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
		Substitute.For<IMatchRepository>(), Substitute.For<IMatchRoundEndOutbox>(), Substitute.For<IMatchLiveEvents>(),
		Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
		NullLogger<MatchMembershipService>.Instance);

	private readonly SpectatorService _spectatorService = new(Substitute.For<IChannelRegistry>(),
		new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
			Substitute.For<ISessionRegistry<IrcSession>>(), Substitute.For<IChannelRegistry>(),
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
		NullLogger<SpectatorService>.Instance);

	private PlayerLogoutService MakeService()
	{
		var channelMembership =
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		return new PlayerLogoutService(_gameRegistry, _ircRegistry, channelMembership, _spectatorService,
			_matchMembership, NullLogger<PlayerLogoutService>.Instance);
	}

	[Fact]
	public async Task Logout_RemovesFromSessionRegistry()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeService().LogoutAsync(player);

		_gameRegistry.Received(1).Remove(player);
	}

	[Fact]
	public async Task Logout_IrcSession_NeverBroadcastsBanchoLogoutPacket()
	{
		// An IRC-only connection was never a "player" osu! clients saw in the first place — its
		// disconnect must never surface as a bancho Logout packet to anyone.
		var irc = new IrcSession(1, "cmyui", "irc-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = Substitute.For<IIrcConnection>()
		};
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_gameRegistry.All.Returns([other]);

		await MakeService().LogoutAsync(irc);

		_ircRegistry.Received(1).Remove(irc);
		Assert.Empty(other.Dequeue());
	}

	[Fact]
	public async Task Logout_GameSessionWhileIrcSessionOfSameUserIdStaysOnline_IrcSessionUntouched()
	{
		var game = new GameSession(1, "cmyui", "game-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var irc = new IrcSession(1, "cmyui", "irc-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = Substitute.For<IIrcConnection>()
		};
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_gameRegistry.All.Returns([game, other]);
		_ircRegistry.GetByUserId(1).Returns(irc);

		await MakeService().LogoutAsync(game);

		_gameRegistry.Received(1).Remove(game);
		_ircRegistry.DidNotReceive().Remove(irc);
		// Game logout still broadcasts the bancho Logout packet to other GameSessions — that part is
		// independent of whether an IrcSession for the same account survives.
		Assert.Equal(ServerPacketWriter.Logout(1), other.Dequeue());
	}

	[Fact]
	public async Task Logout_LeavesAllJoinedChannels()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		channel.Join(player.Id);
		player.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);

		await MakeService().LogoutAsync(player);

		Assert.False(channel.Contains(1));
		Assert.False(player.InChannel("#osu"));
	}

	[Fact]
	public async Task Logout_UnrestrictedPlayer_BroadcastsLogoutPacket()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_gameRegistry.All.Returns([other]);

		await MakeService().LogoutAsync(player);

		Assert.Equal(ServerPacketWriter.Logout(1), other.Dequeue());
	}

	[Fact]
	public async Task Logout_RestrictedPlayer_DoesNotBroadcastLogoutPacket()
	{
		var player =
			new GameSession(1, "cmyui", "token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch); // restricted
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_gameRegistry.All.Returns([other]);

		await MakeService().LogoutAsync(player);

		Assert.Empty(other.Dequeue());
	}

	[Fact]
	public async Task Logout_WhileSpectating_StopsSpectatingAndClearsHostSpectatorList()
	{
		var host = new GameSession(2, "host", "host-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		host.AddSpectator(player);
		player.Spectating = host;

		await MakeService().LogoutAsync(player);

		Assert.Null(player.Spectating);
		Assert.DoesNotContain(player, host.Spectators);
	}

	[Fact]
	public async Task Logout_PlayerWhoseOnlySpectatorIsTheBot_RemovesBotSpectateRelationship()
	{
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_gameRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		player.AddSpectator(bot);
		bot.Spectating = player;

		await MakeService().LogoutAsync(player);

		Assert.Empty(player.Spectators);
		Assert.Null(bot.Spectating);
	}

	[Fact]
	public async Task Logout_WhileInAMatch_LeavesTheMatchSoItDoesNotAccumulateAGhostSlot()
	{
		var channelRegistry = new MultiplayerTestSupport.FakeChannelRegistry();
		var matchRepository = new MultiplayerTestSupport.FakeMatchRepository();
		var matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(channelRegistry, matchRepository);
		var gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		var matchMembership = new MatchMembershipService(matchRegistry, channelRegistry, gameRegistry, ircRegistry,
			new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			matchRepository, Substitute.For<IMatchRoundEndOutbox>(),
			new MultiplayerTestSupport.FakeMatchLiveEvents(),
			Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
			NullLogger<MatchMembershipService>.Instance);
		var host = new GameSession(1, "host", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		gameRegistry.All.Returns([host]);
		gameRegistry.GetByUserId(1).Returns(host);
		var match = (await matchMembership.CreateAsync(host, MultiplayerTestSupport.MakeMatchData(host.Id)))!;
		var channelMembership =
			new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		var service = new PlayerLogoutService(gameRegistry, ircRegistry, channelMembership, _spectatorService,
			matchMembership, NullLogger<PlayerLogoutService>.Instance);

		await service.LogoutAsync(host);

		Assert.Null(host.Match);
		// Not disposed immediately anymore — the room's slot is freed (no ghost slot) but the room
		// itself now waits out the 5-minute empty-room auto-close timer before tearing down.
		Assert.NotNull(matchRegistry.GetById(match.Id));
		Assert.NotNull(match.EmptyRoomTimer);
	}
}