using Basil.Application.Configurations;
using Microsoft.Extensions.Options;
using Basil.Application.Packets.Channels;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's ChannelJoin (calls User.join_channel).</summary>
public class ChannelJoinHandlerTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	private ChannelJoinHandler MakeHandler()
	{
		return new ChannelJoinHandler(_channelRegistry,
			new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions())));
	}

	private static PacketReader ChannelNameReader(string name)
	{
		return new PacketReader(BinaryWriter.WriteString(name));
	}

	[Fact]
	public async Task Handle_ExistingReadableChannel_JoinsBothSidesAndSendsJoinSuccess()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.GameSessions.Returns([player]);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

		Assert.True(channel.Contains(1));
		Assert.True(player.InChannel("#osu"));
		var expected = ServerPacketWriter.ChannelJoin("#osu")
			.Concat(ServerPacketWriter.ChannelInfo("#osu", "General", 1))
			.ToArray();
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
	public async Task Handle_NoReadPrivilege_NoOp()
	{
		var channel = new ChannelSession(1, "#staff", "Staff", UserPrivileges.Staff, UserPrivileges.Staff, true);
		_channelRegistry.GetByName("#staff").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#staff"));

		Assert.False(channel.Contains(1));
		Assert.Empty(player.Dequeue());
	}

	[Fact]
	public async Task Handle_AlreadyJoined_NoOp()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		player.JoinChannel("#osu");
		channel.Join(player.Id);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

		Assert.Empty(player.Dequeue());
	}
}