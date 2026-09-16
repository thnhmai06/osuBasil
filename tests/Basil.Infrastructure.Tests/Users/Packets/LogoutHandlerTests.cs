using Basil.Application.Irc;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Beatmaps;
using Basil.Domain.Channels;
using Basil.Domain.Multiplayer;
using Basil.Application.Spectating;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat;
using Basil.Infrastructure.Chat.Packets;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Multiplayer;
using Basil.Infrastructure.Multiplayer.Packets;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Infrastructure.Spectating;
using Basil.Infrastructure.Spectating.Packets;
using Basil.Infrastructure.Users.Packets;
using Basil.Protocol.Packets;
using Basil.Application.Users;
using Basil.Application.Beatmaps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Infrastructure.Tests.Users.Packets;

/// <summary>
///     Verifies the `Logout` handler always delegates to PlayerLogoutService, with no login grace
///     period. The actual cleanup (match/channel-leaving, registry removal, broadcast) is delegated
///     to PlayerLogoutService and covered by PlayerLogoutServiceTests.
/// </summary>
public class LogoutHandlerTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();

	private LogoutHandler MakeHandler()
	{
		var channelMembership = new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
			Substitute.For<ISessionRegistry<IrcSession>>(),
			Substitute.For<IChannelRegistry>(), new ChatNotifier(Options.Create(new IrcOptions())), new ChannelNotifier(Substitute.For<ISessionRegistry<GameSession>>(),Substitute.For<ISessionRegistry<IrcSession>>(), Options.Create(new IrcOptions())), Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(),
			Options.Create(new IrcOptions()));
		var matchBroadcast = new MatchBroadcast(Substitute.For<IChannelRegistry>(), channelMembership, new BanchoMatchNotifier(_channelRegistry, channelMembership), new ChatNotifier(Options.Create(new IrcOptions())),
			Substitute.For<ISessionRegistry<GameSession>>(), Substitute.For<ISessionRegistry<IrcSession>>(), null,
			Substitute.For<IBeatmapRepository>(),
			Substitute.For<IUserRepository>());
		var matchLifecycle = new MatchLifecycle(Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
			channelMembership, new BanchoMatchNotifier(_channelRegistry, channelMembership), Substitute.For<ISessionRegistry<GameSession>>(), Substitute.For<IMatchRepository>(),
			Substitute.For<IMatchRoundEndOutbox>(), null,
			Substitute.For<IBeatmapRepository>(), matchBroadcast, Substitute.For<IServiceProvider>(),
			NullLogger<MatchLifecycle>.Instance);
		var matchMembership = new MatchMembership(Substitute.For<IChannelRegistry>(),
			Substitute.For<ISessionRegistry<GameSession>>(), channelMembership, new BanchoMatchNotifier(_channelRegistry, channelMembership), Substitute.For<IMatchRepository>(),
			matchLifecycle, NullLogger<MatchMembership>.Instance);

		var spectatorService = new SpectatorService(Substitute.For<IChannelRegistry>(),
			new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
				Substitute.For<ISessionRegistry<IrcSession>>(),
				Substitute.For<IChannelRegistry>(), new ChatNotifier(Options.Create(new IrcOptions())), new ChannelNotifier(Substitute.For<ISessionRegistry<GameSession>>(),Substitute.For<ISessionRegistry<IrcSession>>(), Options.Create(new IrcOptions())), Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(),
				Options.Create(new IrcOptions())), new BanchoSpectatorNotifier(),
			NullLogger<SpectatorService>.Instance);

		return new LogoutHandler(new PlayerLogoutService(
			[
				new MatchLeaveLogoutHandler(matchMembership),
				new SpectatorTeardownLogoutHandler(_gameRegistry, spectatorService),
				new ChannelPartLogoutHandler(new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry, new ChatNotifier(Options.Create(new IrcOptions())), new ChannelNotifier(_gameRegistry,_ircRegistry, Options.Create(new IrcOptions())),
					Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(),
					Options.Create(new IrcOptions()))),
				new GameSessionRegistryRemovalLogoutHandler(_gameRegistry),
				new IrcSessionRemovalLogoutHandler(_ircRegistry),
				new StatusPublishLogoutHandler(Substitute.For<IPlayerStatusEvents>()),
				new LogoutBroadcastHandler(_gameRegistry)
			],
			NullLogger<PlayerLogoutService>.Instance));
	}

	[Fact]
	public async Task Handle_ImmediatelyAfterLogin_DelegatesToLogoutService()
	{
		var loginTime = DateTimeOffset.UtcNow;
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, loginTime);
		var reader = new PacketReader(BinaryWriter.WriteInt32(0));

		await MakeHandler().HandleAsync(session, reader);

		// No login grace: a logout sent in the same second as login is honored like any other.
		_gameRegistry.Received(1).Remove(session);
	}

	[Fact]
	public async Task Handle_AfterOneSecond_DelegatesToLogoutService()
	{
		var loginTime = DateTimeOffset.UtcNow.AddSeconds(-2);
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, loginTime);
		var reader = new PacketReader(BinaryWriter.WriteInt32(0));

		await MakeHandler().HandleAsync(session, reader);

		_gameRegistry.Received(1).Remove(session);
	}
}