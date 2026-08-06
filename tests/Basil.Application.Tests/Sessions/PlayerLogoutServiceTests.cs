using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Ported from User.logout's match/channel-leaving + playerlist-removal + broadcast, extracted
///     out of LogoutHandler so !reconnect can force the same cleanup on a session without going
///     through the LOGOUT packet's 1-second login-grace-period check (which is packet-specific, not
///     part of "logout" semantics).
/// </summary>
public class PlayerLogoutServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();

	private readonly MatchMembershipService _matchMembership = new(
		Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
		Substitute.For<IUserSessionRegistry>(),
		new ChannelMembershipService(Substitute.For<IUserSessionRegistry>(), Substitute.For<IChannelRegistry>(),
			Options.Create(new IrcOptions())),
		Substitute.For<IMatchRepository>(), Substitute.For<IMatchLiveEvents>(),
		Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
		NullLogger<MatchMembershipService>.Instance);

	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	private readonly SpectatorService _spectatorService = new(Substitute.For<IChannelRegistry>(),
		new ChannelMembershipService(Substitute.For<IUserSessionRegistry>(), Substitute.For<IChannelRegistry>(),
			Options.Create(new IrcOptions())),
		NullLogger<SpectatorService>.Instance);

	private PlayerLogoutService MakeService()
	{
		var channelMembership =
			new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions()));
		return new PlayerLogoutService(_sessionRegistry, channelMembership, _spectatorService, _matchMembership,
			NullLogger<PlayerLogoutService>.Instance);
	}

	[Fact]
	public async Task Logout_RemovesFromSessionRegistry()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeService().LogoutAsync(player);

		_sessionRegistry.Received(1).Remove(player);
	}

	[Fact]
	public async Task Logout_LeavesAllJoinedChannels()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
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
		_sessionRegistry.GameSessions.Returns([other]);

		await MakeService().LogoutAsync(player);

		Assert.Equal(ServerPacketWriter.Logout(1), other.Dequeue());
	}

	[Fact]
	public async Task Logout_RestrictedPlayer_DoesNotBroadcastLogoutPacket()
	{
		var player =
			new GameSession(1, "cmyui", "token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch); // restricted
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.GameSessions.Returns([other]);

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
		_sessionRegistry.GetGameByUserId(BotBootstrapService.BotId).Returns(bot);
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
		var sessionRegistry = Substitute.For<IUserSessionRegistry>();
		var matchMembership = new MatchMembershipService(matchRegistry, channelRegistry, sessionRegistry,
			new ChannelMembershipService(sessionRegistry, channelRegistry, Options.Create(new IrcOptions())),
			matchRepository,
			new MultiplayerTestSupport.FakeMatchLiveEvents(),
			Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
			NullLogger<MatchMembershipService>.Instance);
		var host = new GameSession(1, "host", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		sessionRegistry.GameSessions.Returns([host]);
		sessionRegistry.GetGameByUserId(1).Returns(host);
		sessionRegistry.GetSessionsByUserId(1).Returns([host]);
		var match = (await matchMembership.CreateAsync(host, MultiplayerTestSupport.MakeMatchData(host.Id)))!;
		var channelMembership =
			new ChannelMembershipService(sessionRegistry, channelRegistry, Options.Create(new IrcOptions()));
		var service = new PlayerLogoutService(sessionRegistry, channelMembership, _spectatorService, matchMembership,
			NullLogger<PlayerLogoutService>.Instance);

		await service.LogoutAsync(host);

		Assert.Null(host.Match);
		// Not disposed immediately anymore — the room's slot is freed (no ghost slot) but the room
		// itself now waits out the 5-minute empty-room auto-close timer before tearing down.
		Assert.NotNull(matchRegistry.GetById(match.Id));
		Assert.NotNull(match.EmptyRoomTimer);
	}
}
