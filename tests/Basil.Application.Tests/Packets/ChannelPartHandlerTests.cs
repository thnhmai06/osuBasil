using Basil.Application.Configurations;
using Microsoft.Extensions.Options;
using Basil.Application.Packets.Channels;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's ChannelPart (calls User.leave_channel).</summary>
public class ChannelPartHandlerTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();

	private ChannelPartHandler MakeHandler()
	{
		return new ChannelPartHandler(_channelRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())));
	}

	private static PacketReader ChannelNameReader(string name)
	{
		return new PacketReader(BinaryWriter.WriteString(name));
	}

	[Fact]
	public async Task Handle_JoinedChannel_LeavesBothSidesAndBroadcastsUpdatedInfo()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		channel.Join(player.Id);
		player.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);
		_gameRegistry.All.Returns([player]);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

		Assert.False(channel.Contains(1));
		Assert.False(player.InChannel("#osu"));
		var expected = ServerPacketWriter.ChannelInfo("#osu", "#osu", 0);
		Assert.Equal(expected, player.Dequeue());
	}

	[Fact]
	public async Task Handle_UnknownChannel_NoOp()
	{
		_channelRegistry.GetByName("#missing").Returns((ChannelSession?)null);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#missing"));

		Assert.Empty(player.Dequeue());
	}

	[Fact]
	public async Task Handle_NotJoined_NoOp()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

		Assert.Empty(player.Dequeue());
	}
}