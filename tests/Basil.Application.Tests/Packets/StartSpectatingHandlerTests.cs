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
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's StartSpectating.</summary>
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
				new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), new FakeChannelRegistry(), Options.Create(new IrcOptions())),
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
				new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), new FakeChannelRegistry(), Options.Create(new IrcOptions())),
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
				new ChannelMembershipService(_sessionRegistry, Substitute.For<ISessionRegistry<IrcSession>>(), new FakeChannelRegistry(), Options.Create(new IrcOptions())),
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