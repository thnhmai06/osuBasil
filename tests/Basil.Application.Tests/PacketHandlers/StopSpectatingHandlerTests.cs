using Basil.Application.PacketHandlers.Spectating;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StopSpectating.</summary>
public class StopSpectatingHandlerTests
{
	private static PlayerSession MakePlayer(int id, string name)
	{
		return new PlayerSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public async Task Handle_NotSpectating_NoOp()
	{
		var sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
		var handler = new StopSpectatingHandler(new SpectatorService(new FakeChannelRegistry(),
			new ChannelMembershipService(sessionRegistry, new FakeChannelRegistry()),
			NullLogger<SpectatorService>.Instance));
		var player = MakePlayer(1, "alice");

		await handler.HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Null(player.Spectating);
	}

	[Fact]
	public async Task Handle_Spectating_StopsAndClearsHostSpectatorList()
	{
		var sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
		var host = MakePlayer(2, "host");
		var player = MakePlayer(1, "alice");
		sessionRegistry.All.Returns([host, player]);
		sessionRegistry.GetById(2).Returns(host);
		sessionRegistry.GetById(1).Returns(player);
		var spectatorService =
			new SpectatorService(new FakeChannelRegistry(),
				new ChannelMembershipService(sessionRegistry, new FakeChannelRegistry()),
				NullLogger<SpectatorService>.Instance);
		spectatorService.AddSpectator(host, player);
		var handler = new StopSpectatingHandler(spectatorService);

		await handler.HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Null(player.Spectating);
		Assert.DoesNotContain(player, host.Spectators);
	}
}