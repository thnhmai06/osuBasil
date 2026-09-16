using Basil.Application.Irc;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Channels;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat;
using Basil.Infrastructure.Chat.Packets;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Multiplayer;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Infrastructure.Tests.Chat.Packets;

/// <summary>Verifies the `ChannelJoin` handler joins the requested channel when readable, no-op otherwise.</summary>
public class ChannelJoinHandlerTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();

	private ChannelJoinHandler MakeHandler()
	{
		return new ChannelJoinHandler(_channelRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry, new ChatNotifier(Options.Create(new IrcOptions())), new ChannelNotifier(_gameRegistry,_ircRegistry, Options.Create(new IrcOptions())),
				Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(),
				Options.Create(new IrcOptions())));
	}

	private static PacketReader ChannelNameReader(string name)
	{
		return new PacketReader(BinaryWriter.WriteString(name));
	}

	[Fact]
	public async Task Handle_ExistingReadableChannel_JoinsBothSidesAndSendsJoinSuccess()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_gameRegistry.All.Returns([player]);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

		Assert.True(channel.Contains(1));
		Assert.True(player.InChannel("#osu"));
		var expected = ServerPacketWriter.ChannelJoin("#osu")
			.Concat(ServerPacketWriter.ChannelInfo("#osu", "#osu", 1))
			.ToArray();
		var actual = player.Dequeue();
		Assert.Equal(expected, actual);
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
		var channel = new ChannelSession(1, "#staff", UserPrivileges.Staff, UserPrivileges.Staff, true);
		_channelRegistry.GetByName("#staff").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#staff"));

		Assert.False(channel.Contains(1));
		Assert.Empty(player.Dequeue());
	}

	[Fact]
	public async Task Handle_AlreadyJoined_NoOp()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		player.JoinChannel("#osu");
		channel.Join(player.Id);

		await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

		Assert.Empty(player.Dequeue());
	}
}