using Basil.Application.Configurations;
using Microsoft.Extensions.Options;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Ported from User.join_channel/leave_channel, shared between client-initiated packets and server-initiated
///     instance membership (spectator, mp). Covers the dual-session (GameSession + IrcSession) roster
///     dedup: a UserId with two live sessions in the same channel is one member for
///     broadcast/roster purposes, but each session still gets its own echo.
/// </summary>
public class ChannelMembershipServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	private ChannelMembershipService MakeService()
	{
		return new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions()));
	}

	private static GameSession MakeGame(int id, string name)
	{
		return new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private static IrcSession MakeIrc(int id, string name)
	{
		return new IrcSession(id, name, $"irc-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			Connection = new RecordingIrcConnection()
		};
	}

	private void Register(params UserSession[] sessions)
	{
		foreach (var group in sessions.GroupBy(s => s.Id))
			_sessionRegistry.GetSessionsByUserId(group.Key).Returns(group.ToList());

		_sessionRegistry.GameSessions.Returns(sessions.OfType<GameSession>().ToList());
	}

	[Fact]
	public void Join_OrdinaryChannel_BroadcastsChannelInfoToEveryGameSessionThatCanRead()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var player = MakeGame(1, "alice");
		var other = MakeGame(2, "bob");
		Register(player, other);

		var joined = MakeService().Join(player, channel);

		Assert.True(joined);
		Assert.True(channel.Contains(1));
		Assert.True(player.InChannel("#osu"));
		Assert.Contains(ServerPacketWriter.ChannelInfo("#osu", "General", 1), Chunk(other.Dequeue()));
	}

	[Fact]
	public void Join_InstanceChannel_OnlyBroadcastsToChannelMembers()
	{
		var channel = new ChannelSession(0, "#spec_9", "topic", 0, 0, false, "#spectator", true);
		var host = MakeGame(9, "host");
		var joiner = MakeGame(1, "alice");
		var bystander = MakeGame(2, "bob");
		channel.Join(host.Id);
		Register(host, joiner, bystander);

		MakeService().Join(joiner, channel);

		Assert.Contains(ServerPacketWriter.ChannelInfo("#spectator", "topic", 2), Chunk(host.Dequeue()));
		Assert.Empty(bystander.Dequeue());
	}

	[Fact]
	public void Join_AlreadyInChannel_ReturnsFalseAndNoBroadcast()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var player = MakeGame(1, "alice");
		player.JoinChannel("#osu");
		channel.Join(1);
		Register(player);

		var joined = MakeService().Join(player, channel);

		Assert.False(joined);
		Assert.Empty(player.Dequeue());
	}

	[Fact]
	public void Join_SecondSessionOfSameUserId_StillEchoesToItself_ButDoesNotBroadcastToOthers()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var game = MakeGame(1, "alice");
		var irc = MakeIrc(1, "alice");
		var other = MakeGame(2, "bob");
		channel.Join(game.Id); // game session already in the channel
		Register(game, irc, other);

		var joined = MakeService().Join(irc, channel);

		Assert.True(joined);
		Assert.Equal(1, channel.PlayerCount); // roster still counts UserId 1 once
		Assert.Empty(other.Dequeue()); // no broadcast to others — not the first session of this UserId
	}

	[Fact]
	public void Part_SendsKickAndBroadcastsUpdatedCount()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var player = MakeGame(1, "alice");
		var other = MakeGame(2, "bob");
		player.JoinChannel("#osu");
		channel.Join(1);
		channel.Join(2);
		Register(player, other);

		MakeService().Part(player, channel);

		Assert.False(channel.Contains(1));
		Assert.False(player.InChannel("#osu"));
		Assert.Contains(ServerPacketWriter.ChannelKick("#osu"), Chunk(player.Dequeue()));
		Assert.Contains(ServerPacketWriter.ChannelInfo("#osu", "General", 1), Chunk(other.Dequeue()));
	}

	[Fact]
	public void Part_WithoutKick_SkipsKickPacket()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var player = MakeGame(1, "alice");
		player.JoinChannel("#osu");
		channel.Join(1);
		Register(player);

		MakeService().Part(player, channel, false);

		var dequeued = player.Dequeue();
		Assert.DoesNotContain(ServerPacketWriter.ChannelKick("#osu"), Chunk(dequeued));
	}

	[Fact]
	public void Part_WhileAnotherSessionOfSameUserIdRemains_DoesNotBroadcastToOthers()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var game = MakeGame(1, "alice");
		var irc = MakeIrc(1, "alice");
		var other = MakeGame(2, "bob");
		game.JoinChannel("#osu");
		irc.JoinChannel("#osu");
		channel.Join(game.Id);
		channel.Join(irc.Id);
		Register(game, irc, other);

		MakeService().Part(game, channel, false);

		Assert.True(channel.Contains(1)); // irc session keeps UserId 1 in the roster
		Assert.Empty(other.Dequeue()); // not the last session for this UserId — no PART broadcast
	}

	[Fact]
	public void Join_IrcLifecycleMessage_NeverDeliveredToGameSession()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var existing = MakeGame(2, "bob");
		channel.Join(existing.Id);
		Register(existing);

		var joinerIrc = MakeIrc(1, "alice");
		Register(existing, joinerIrc);

		MakeService().Join(joinerIrc, channel);

		// The GameSession still gets the ordinary ChannelInfo playercount update (channel membership
		// mechanics don't distinguish IRC from game joins for that) — but the IRC JOIN wire message
		// itself must never surface as bancho bytes on its packet queue, since its IrcConnection is a
		// bancho bridge that only re-encodes PRIVMSG.
		Assert.Equal(ServerPacketWriter.ChannelInfo("#osu", "General", 2), existing.Dequeue());
	}

	[Fact]
	public void Join_BroadcastsJoinOnlyToIrcMembers()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var existingIrc = MakeIrc(2, "bob");
		channel.Join(existingIrc.Id);
		var joinerIrc = MakeIrc(1, "alice");
		Register(existingIrc, joinerIrc);

		MakeService().Join(joinerIrc, channel);

		var recorded = ((RecordingIrcConnection)existingIrc.Connection).Received;
		Assert.Contains(recorded, m => m.Command == "JOIN");
	}

	// Handlers concatenate multiple packets into one Dequeue() call; this splits it back for
	// Contains-style assertions without needing exact-offset byte matching.
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

	private sealed class RecordingIrcConnection : IIrcConnection
	{
		public List<IrcMessage> Received { get; } = [];
		public UserSession User => throw new NotImplementedException();

		public void Send(IrcMessage message)
		{
			Received.Add(message);
		}
	}
}
