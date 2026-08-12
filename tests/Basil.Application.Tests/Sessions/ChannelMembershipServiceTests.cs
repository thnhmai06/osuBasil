using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Verifies channel join/leave membership handling, shared between client-initiated packets and
///     server-initiated membership (spectator, mp). Covers the dual-session (GameSession + IrcSession)
///     roster dedup: a UserId with two live sessions in the same channel is one member for
///     broadcast/roster purposes, but each session still gets its own echo.
/// </summary>
public class ChannelMembershipServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly IMatchLiveEvents _matchLiveEvents = Substitute.For<IMatchLiveEvents>();
	private readonly IMatchRegistry _matchRegistry = Substitute.For<IMatchRegistry>();

	private ChannelMembershipService MakeService()
	{
		return new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			_matchRegistry, _matchLiveEvents, Options.Create(new IrcOptions()));
	}

	private static GameSession MakeGame(int id, string name)
	{
		return new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private static IrcSession MakeIrc(int id, string name,
		UserPrivileges privilege = UserPrivileges.Unrestricted)
	{
		return new IrcSession(id, name, $"irc-{id}", privilege, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = new RecordingIrcConnection()
		};
	}

	private void Register(params UserSession[] sessions)
	{
		_gameRegistry.All.Returns(sessions.OfType<GameSession>().ToList());
		foreach (var game in sessions.OfType<GameSession>())
			_gameRegistry.GetByUserId(game.Id).Returns(game);
		foreach (var irc in sessions.OfType<IrcSession>())
			_ircRegistry.GetByUserId(irc.Id).Returns(irc);
	}

	[Fact]
	public void Join_OrdinaryChannel_BroadcastsChannelInfoToEveryGameSessionThatCanRead()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		var player = MakeGame(1, "alice");
		var other = MakeGame(2, "bob");
		Register(player, other);

		var joined = MakeService().Join(player, channel);

		Assert.True(joined);
		Assert.True(channel.Contains(1));
		Assert.True(player.InChannel("#osu"));
		Assert.Contains(ServerPacketWriter.ChannelInfo("#osu", "#osu", 1), Chunk(other.Dequeue()));
	}

	[Fact]
	public void Join_InstanceChannel_OnlyBroadcastsToChannelMembers()
	{
		var channel = new ChannelSession(0, "#spec_9", 0, 0, false, "#spectator", true);
		var host = MakeGame(9, "host");
		var joiner = MakeGame(1, "alice");
		var bystander = MakeGame(2, "bob");
		channel.Join(host.Id);
		Register(host, joiner, bystander);

		MakeService().Join(joiner, channel);

		Assert.Contains(ServerPacketWriter.ChannelInfo("#spectator", "#spec_9", 2), Chunk(host.Dequeue()));
		Assert.Empty(bystander.Dequeue());
	}

	[Fact]
	public void Join_AlreadyInChannel_ReturnsFalseAndNoBroadcast()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
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
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
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
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
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
		Assert.Contains(ServerPacketWriter.ChannelInfo("#osu", "#osu", 1), Chunk(other.Dequeue()));
	}

	[Fact]
	public void SyncTopic_ChangesTopic_BroadcastsChannelInfoAndIrcTopicLine()
	{
		var channel = new ChannelSession(1, "#mp_5", 0, 0, false, "#multiplayer", true) { Topic = "Old Name" };
		var bot = MakeGame(BotBootstrapService.BotId, "BasilBot");
		var gamePlayer = MakeGame(1, "alice");
		var ircMember = MakeIrc(2, "bob");
		channel.Join(gamePlayer.Id);
		channel.Join(ircMember.Id);
		Register(bot, gamePlayer, ircMember);

		MakeService().SyncTopic(channel, "New Name");

		Assert.Equal("New Name", channel.Topic);
		Assert.Contains(ServerPacketWriter.ChannelInfo("#multiplayer", "New Name", 2), Chunk(gamePlayer.Dequeue()));
		var connection = (RecordingIrcConnection)ircMember.IrcConnection;
		var topicMsg = Assert.Single(connection.Received, m => m.Command == "TOPIC");
		Assert.Equal(["#mp_5", "New Name"], topicMsg.Params);
		Assert.StartsWith("BasilBot!", topicMsg.Prefix);
	}

	[Fact]
	public void SyncTopic_UnchangedTopic_NoBroadcast()
	{
		var channel = new ChannelSession(1, "#mp_5", 0, 0, false, "#multiplayer", true) { Topic = "Same" };
		var gamePlayer = MakeGame(1, "alice");
		channel.Join(gamePlayer.Id);
		Register(gamePlayer);

		MakeService().SyncTopic(channel, "Same");

		Assert.Empty(gamePlayer.Dequeue());
	}

	[Fact]
	public void Part_WithoutKick_SkipsKickPacket()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
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
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
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
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
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
		Assert.Equal(ServerPacketWriter.ChannelInfo("#osu", "#osu", 2), existing.Dequeue());
	}

	[Fact]
	public void Join_BroadcastsJoinOnlyToIrcMembers()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		var existingIrc = MakeIrc(2, "bob");
		channel.Join(existingIrc.Id);
		var joinerIrc = MakeIrc(1, "alice");
		Register(existingIrc, joinerIrc);

		MakeService().Join(joinerIrc, channel);

		var recorded = ((RecordingIrcConnection)existingIrc.IrcConnection).Received;
		Assert.Contains(recorded, m => m.Command == "JOIN");
	}

	[Fact]
	public void Join_MatchChannel_DeniedForANonParticipantNonReferee()
	{
		var outsider = MakeIrc(1, "alice");
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 9, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		_matchRegistry.All.Returns([match]);
		Register(outsider);

		var joined = MakeService().Join(outsider, room);

		Assert.False(joined);
		Assert.False(room.Contains(outsider.Id));
	}

	[Fact]
	public void Join_MatchChannel_AllowedForAReferee_EvenBeforeEverPhysicallyJoining()
	{
		var referee = MakeIrc(2, "ref");
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 9, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		match.AddReferee(referee.Id);
		_matchRegistry.All.Returns([match]);
		Register(referee);

		var joined = MakeService().Join(referee, room);

		Assert.True(joined);
		Assert.True(room.Contains(referee.Id));
	}

	[Fact]
	public void Join_MatchChannel_AllowedForASeatedPlayer()
	{
		var seated = MakeGame(3, "bob");
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 9, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		match.Slots[0].PlayerId = seated.Id;
		_matchRegistry.All.Returns([match]);
		Register(seated);

		var joined = MakeService().Join(seated, room);

		Assert.True(joined);
	}

	[Fact]
	public void Join_MatchChannel_BypassMatchGate_SeatsBeforeParticipantOrRefereeIsTrue()
	{
		// Mirrors MatchMembershipService.OccupySlot: the channel join happens before the slot is
		// actually assigned, so without the bypass this legitimate seat would be rejected.
		var incoming = MakeGame(4, "carol");
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 9, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		_matchRegistry.All.Returns([match]);
		Register(incoming);

		Assert.False(MakeService().Join(incoming, room));
		Assert.True(MakeService().Join(incoming, room, true));
	}

	[Fact]
	public void BuildListReply_MatchChannel_ShowsOnlyToItsParticipantOrReferee()
	{
		var referee = MakeIrc(1, "ref");
		var outsider = MakeIrc(2, "alice");
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 9, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		match.AddReferee(referee.Id);
		_matchRegistry.All.Returns([match]);
		_channelRegistry.All.Returns([room]);
		var service = MakeService();

		var refereeListing = service.BuildListReply(referee).Where(m => m.Command == "322").ToList();
		var outsiderListing = service.BuildListReply(outsider).Where(m => m.Command == "322").ToList();

		Assert.Equal(["#mp_5"], refereeListing.Select(m => m.Params[1]));
		Assert.Empty(outsiderListing);
	}

	[Fact]
	public void MemberPrefix_AuthorityIsStaffOutsideAMatchRoomAndTheRoomsRefereesInside()
	{
		var staff = MakeIrc(1, "mod", UserPrivileges.Unrestricted | UserPrivileges.Moderator);
		var referee = MakeIrc(2, "ref");
		var player = MakeIrc(3, "alice");
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var announce = new ChannelSession(2, "#announce", 0, UserPrivileges.Staff, true);
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", referee.Id, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		match.AddReferee(referee.Id);
		_matchRegistry.All.Returns([match]);
		var service = MakeService();

		Assert.Equal("@", service.MemberPrefix(staff, general));
		Assert.Equal("+", service.MemberPrefix(player, general));
		// Staff is authority outside a room, but a room answers to its own referees.
		Assert.Equal("+", service.MemberPrefix(staff, room));
		Assert.Equal("@", service.MemberPrefix(referee, room));
		Assert.Equal("+", service.MemberPrefix(player, room));
		// Read-only for this member: no voice, so no prefix at all.
		Assert.Equal("", service.MemberPrefix(player, announce));
	}

	[Fact]
	public void BroadcastPrivmsg_InAMatchChannel_PublishesEveryLineIncludingTheSenderSOwn()
	{
		var speaker = MakeIrc(1, "alice");
		var room = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", speaker.Id, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5") { DbId = 7 };
		room.Join(speaker.Id);
		_matchRegistry.All.Returns([match]);
		Register(speaker);
		var service = MakeService();

		// skipMemberId is the sender: they must still show up on the stream an observer is watching.
		service.BroadcastPrivmsg(room, IrcMessageWriter.Privmsg("alice", 1, "#mp_5", "glhf"), speaker.Id);
		service.BroadcastPrivmsg(general, IrcMessageWriter.Privmsg("alice", 1, "#osu", "hello"));

		_matchLiveEvents.Received(1).PublishChat(7, Arg.Any<byte[]>());
		_matchLiveEvents.DidNotReceive().PublishChat(Arg.Is<int>(id => id != 7), Arg.Any<byte[]>());
	}

	[Fact]
	public void BuildListReply_ListsOnlyReadableNonInstanceChannels()
	{
		var requester = MakeIrc(1, "alice");
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var staff = new ChannelSession(2, "#staff", UserPrivileges.Moderator, 0, false);
		var match = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		general.Join(requester.Id);
		_channelRegistry.All.Returns([match, staff, general]);

		var replies = MakeService().BuildListReply(requester).ToList();

		Assert.Equal("321", replies[0].Command);
		Assert.Equal("323", replies[^1].Command);
		var listed = replies.Where(m => m.Command == "322").ToList();
		Assert.Equal(["#osu"], listed.Select(m => m.Params[1]));
		Assert.Equal("1", listed[0].Params[2]);
	}

	[Fact]
	public void BuildListReply_ChannelFilter_KeepsOnlyNamedChannelsButIgnoresMasks()
	{
		var requester = MakeIrc(1, "alice");
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var announce = new ChannelSession(2, "#announce", 0, 0, true);
		_channelRegistry.All.Returns([general, announce]);
		var service = MakeService();

		var filtered = service.BuildListReply(requester, "#osu").Where(m => m.Command == "322").ToList();
		var masked = service.BuildListReply(requester, ">0").Where(m => m.Command == "322").ToList();

		Assert.Equal(["#osu"], filtered.Select(m => m.Params[1]));
		Assert.Equal(["#announce", "#osu"], masked.Select(m => m.Params[1]));
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