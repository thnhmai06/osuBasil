using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Packets.Users;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check. The
///     actual cleanup (match/channel-leaving, registry removal, broadcast) is delegated to
///     PlayerLogoutService and covered by PlayerLogoutServiceTests.
/// </summary>
public class LogoutHandlerTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	private LogoutHandler MakeHandler()
	{
		return new LogoutHandler(new PlayerLogoutService(
			_sessionRegistry, _channelRegistry,
			new SpectatorService(Substitute.For<IChannelRegistry>(),
				new ChannelMembershipService(Substitute.For<IUserSessionRegistry>(),
					Substitute.For<IChannelRegistry>()), NullLogger<SpectatorService>.Instance),
			new MatchMembershipService(Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
				Substitute.For<IUserSessionRegistry>(),
				new ChannelMembershipService(Substitute.For<IUserSessionRegistry>(),
					Substitute.For<IChannelRegistry>()),
				Substitute.For<IMatchRepository>(), Substitute.For<IMatchLiveEvents>(),
				Substitute.For<IBeatmapRepository>(), Substitute.For<IUserRepository>(),
				NullLogger<MatchMembershipService>.Instance),
			NullLogger<PlayerLogoutService>.Instance), NullLogger<LogoutHandler>.Instance);
	}

	[Fact]
	public async Task Handle_WithinOneSecondOfLogin_Ignored()
	{
		var loginTime = DateTimeOffset.UtcNow;
		var session = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, loginTime);
		var reader = new PacketReader(BinaryWriter.WriteInt32(0));

		await MakeHandler().HandleAsync(session, reader);

		_sessionRegistry.DidNotReceive().Remove(Arg.Any<UserSession>());
		Assert.Equal("token", session.Token);
	}

	[Fact]
	public async Task Handle_AfterOneSecond_DelegatesToLogoutService()
	{
		var loginTime = DateTimeOffset.UtcNow.AddSeconds(-2);
		var session = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, loginTime);
		var reader = new PacketReader(BinaryWriter.WriteInt32(0));

		await MakeHandler().HandleAsync(session, reader);

		_sessionRegistry.Received(1).Remove(session);
	}
}