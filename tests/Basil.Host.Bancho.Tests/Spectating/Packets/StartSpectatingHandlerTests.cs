using Basil.Application.Irc;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Users;
using Basil.Application.Chat;
using Basil.Host.Bancho.Chat.Packets;
using Basil.Host.Bancho.Multiplayer;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Application.Spectating;
using Basil.Host.Bancho.Spectating.Packets;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Host.Bancho.Tests.Spectating.Packets;

/// <summary>Verifies the `StartSpectating` handler starts spectating the target player.</summary>
public class StartSpectatingHandlerTests
{
	private readonly ISessionRegistry<GameSession> _sessionRegistry = Substitute.For<ISessionRegistry<GameSession>>();

	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private static PacketReader TargetIdReader(int targetId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(targetId));
	}

	[Fact]
	public async Task Handle_UnknownTarget_NoOp()
	{
		_sessionRegistry.GetByUserId(999).Returns((GameSession?)null);
		var handler = new StartSpectatingHandler(_sessionRegistry,
			new SpectatorService(new FakeChannelRegistry(),
				new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(),
					new FakeChannelRegistry(), new ChatNotifier(Options.Create(new IrcOptions())),
					new ChannelNotifier(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(),
						Options.Create(new IrcOptions())), Substitute.For<IMatchRegistry>(),
					Substitute.For<ILiveEventHub>(),
					Options.Create(new IrcOptions())), new SpectatorNotifier(),
				NullLogger<SpectatorService>.Instance),
			NullLogger<StartSpectatingHandler>.Instance);
		var player = MakePlayer(1, "alice");

		await handler.HandleAsync(player, TargetIdReader(999));

		Assert.Null(player.Spectating);
	}

	[Fact]
	public async Task Handle_NewHost_StartsSpectating()
	{
		var host = MakePlayer(2, "host");
		var player = MakePlayer(1, "alice");
		_sessionRegistry.GetByUserId(2).Returns(host);
		_sessionRegistry.All.Returns([host, player]);
		_sessionRegistry.GetByUserId(1).Returns(player);
		var handler = new StartSpectatingHandler(_sessionRegistry,
			new SpectatorService(new FakeChannelRegistry(),
				new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(),
					new FakeChannelRegistry(), new ChatNotifier(Options.Create(new IrcOptions())),
					new ChannelNotifier(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(),
						Options.Create(new IrcOptions())), Substitute.For<IMatchRegistry>(),
					Substitute.For<ILiveEventHub>(),
					Options.Create(new IrcOptions())), new SpectatorNotifier(),
				NullLogger<SpectatorService>.Instance),
			NullLogger<StartSpectatingHandler>.Instance);

		await handler.HandleAsync(player, TargetIdReader(2));

		Assert.Same(host, player.Spectating);
		Assert.Contains(player, host.Spectators);
	}

	[Fact]
	public async Task Handle_SameHostAgain_ResendsSpectatorJoinedWithoutRejoiningChannel()
	{
		var host = MakePlayer(2, "host");
		var player = MakePlayer(1, "alice");
		_sessionRegistry.GetByUserId(2).Returns(host);
		_sessionRegistry.GetByUserId(1).Returns(player);
		_sessionRegistry.All.Returns([host, player]);
		var spectatorService =
			new SpectatorService(new FakeChannelRegistry(),
				new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(),
					new FakeChannelRegistry(), new ChatNotifier(Options.Create(new IrcOptions())),
					new ChannelNotifier(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(),
						Options.Create(new IrcOptions())), Substitute.For<IMatchRegistry>(),
					Substitute.For<ILiveEventHub>(),
					Options.Create(new IrcOptions())), new SpectatorNotifier(),
				NullLogger<SpectatorService>.Instance);
		var handler = new StartSpectatingHandler(_sessionRegistry, spectatorService,
			NullLogger<StartSpectatingHandler>.Instance);
		await handler.HandleAsync(player, TargetIdReader(2));
		host.Dequeue();

		await handler.HandleAsync(player, TargetIdReader(2));

		Assert.Contains(ServerPacketWriter.SpectatorJoined(player.Id), Chunk(host.Dequeue()));
	}

	private static List<byte[]> Chunk(byte[] data)
	{
		var chunks = new List<byte[]>();
		var offset = 0;
		while (offset < data.Length)
		{
			var length = BitConverter.ToInt32(data, offset + 3);
			var total = 7 + length;
			chunks.Add(data[offset..(offset + total)]);
			offset += total;
		}

		return chunks;
	}
}
