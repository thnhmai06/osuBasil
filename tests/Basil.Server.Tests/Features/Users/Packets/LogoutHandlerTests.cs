using Basil.Server.Shared.Eventing;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Configuration;
using Basil.Server.Features.Users.Packets;
using Basil.Server.Features.Spectating;
using Basil.Server.Shared.Sessions;
using Basil.Server.Features.Chat;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Server.Tests.Features.Users.Packets;

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
			Substitute.For<IChannelRegistry>(), Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions()));
		var matchBroadcast = new MatchBroadcast(Substitute.For<IChannelRegistry>(), channelMembership,
			Substitute.For<ISessionRegistry<GameSession>>(), Substitute.For<ISessionRegistry<IrcSession>>(), null, Substitute.For<IBeatmapRepository>(),
			Substitute.For<IUserRepository>());
		var matchLifecycle = new MatchLifecycle(Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
			channelMembership, Substitute.For<ISessionRegistry<GameSession>>(), Substitute.For<IMatchRepository>(),
			Substitute.For<IMatchRoundEndOutbox>(), null,
			Substitute.For<IBeatmapRepository>(), matchBroadcast, Substitute.For<IServiceProvider>(),
			NullLogger<MatchLifecycle>.Instance);
		var matchMembership = new MatchMembership(Substitute.For<IChannelRegistry>(),
			Substitute.For<ISessionRegistry<GameSession>>(), channelMembership, Substitute.For<IMatchRepository>(),
			matchLifecycle, NullLogger<MatchMembership>.Instance);

		return new LogoutHandler(new PlayerLogoutService(
			_gameRegistry, _ircRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions())),
			new SpectatorService(Substitute.For<IChannelRegistry>(),
				new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
					Substitute.For<ISessionRegistry<IrcSession>>(),
					Substitute.For<IChannelRegistry>(), Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions())),
				NullLogger<SpectatorService>.Instance),
			matchMembership,
			Substitute.For<IPlayerStatusEvents>(),
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