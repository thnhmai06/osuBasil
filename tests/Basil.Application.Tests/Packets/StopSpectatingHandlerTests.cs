using Basil.Application.Configurations;
using Basil.Application.Packets.Spectating;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `StopSpectating` handler stops spectating and clears the host's spectator list.</summary>
public class StopSpectatingHandlerTests
{
	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public async Task Handle_NotSpectating_NoOp()
	{
		var gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		var handler = new StopSpectatingHandler(new SpectatorService(new FakeChannelRegistry(),
			new ChannelMembershipService(gameRegistry, ircRegistry, new FakeChannelRegistry(), Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			NullLogger<SpectatorService>.Instance));
		var player = MakePlayer(1, "alice");

		await handler.HandleAsync(player, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Null(player.Spectating);
	}

	[Fact]
	public async Task Handle_Spectating_StopsAndClearsHostSpectatorList()
	{
		var gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		var host = MakePlayer(2, "host");
		var player = MakePlayer(1, "alice");
		gameRegistry.All.Returns([host, player]);
		gameRegistry.GetByUserId(2).Returns(host);
		gameRegistry.GetByUserId(1).Returns(player);
		var spectatorService =
			new SpectatorService(new FakeChannelRegistry(),
				new ChannelMembershipService(gameRegistry, ircRegistry, new FakeChannelRegistry(), Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
				NullLogger<SpectatorService>.Instance);
		spectatorService.AddSpectator(host, player);
		var handler = new StopSpectatingHandler(spectatorService);

		await handler.HandleAsync(player, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Null(player.Spectating);
		Assert.DoesNotContain(player, host.Spectators);
	}
}