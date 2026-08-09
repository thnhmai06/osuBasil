using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Packets.Users;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

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
		return new LogoutHandler(new PlayerLogoutService(
			_gameRegistry, _ircRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			new SpectatorService(Substitute.For<IChannelRegistry>(),
				new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
					Substitute.For<ISessionRegistry<IrcSession>>(),
					Substitute.For<IChannelRegistry>(), Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
				NullLogger<SpectatorService>.Instance),
			new MatchMembershipService(Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
				Substitute.For<ISessionRegistry<GameSession>>(),
				Substitute.For<ISessionRegistry<IrcSession>>(),
				new ChannelMembershipService(Substitute.For<ISessionRegistry<GameSession>>(),
					Substitute.For<ISessionRegistry<IrcSession>>(),
					Substitute.For<IChannelRegistry>(), Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
				Substitute.For<IMatchRepository>(), Substitute.For<IMatchLiveEvents>(),
				Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
				NullLogger<MatchMembershipService>.Instance),
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