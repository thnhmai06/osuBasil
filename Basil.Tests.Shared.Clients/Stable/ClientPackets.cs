using System.Text;
using Basil.Domain.Spectating;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Models.Users;
using Basil.Protocol.Game.Stable.Wire.Chat;
using Basil.Protocol.Game.Stable.Wire.Packets;

namespace Basil.Tests.Shared.Clients.Stable;

/// <summary>
///     Builds the packets an osu! client sends; every method returns one complete packet, ready to queue for the next
///     poll.
/// </summary>
public static class ClientPackets
{
	private const int HeaderSize = 7;

	private const int OpenSlot = 1;

	private static byte[] Frame(ClientPacketType type, Action<BinaryWriter> payload)
	{
		using var body = new MemoryStream();
		using (var writer = new BinaryWriter(body, Encoding.UTF8, true))
		{
			payload(writer);
		}

		return Header(type, body.ToArray());
	}

	private static byte[] Header(ClientPacketType type, ReadOnlySpan<byte> payload)
	{
		var packet = new byte[HeaderSize + payload.Length];
		BitConverter.TryWriteBytes(packet.AsSpan(0, sizeof(ushort)), (ushort)type);
		BitConverter.TryWriteBytes(packet.AsSpan(3, sizeof(int)), payload.Length);
		payload.CopyTo(packet.AsSpan(HeaderSize));
		return packet;
	}

	// The server's packets and the client's share their payload layouts for messages and rooms, so the server's encoder builds the payload and only the header changes.
	private static byte[] Rewrap(ClientPacketType type, byte[] serverPacket)
	{
		return Header(type, serverPacket.AsSpan(HeaderSize));
	}

	private static byte[] FromMessage(ClientPacketType type, string text, string recipient)
	{
		return Rewrap(type, PacketWriter.Build(writer => writer.SendMessage(string.Empty, text, recipient, 0)));
	}

	private static byte[] FromRoom(ClientPacketType type, RoomPacket room)
	{
		return Rewrap(type, PacketWriter.Build(writer => writer.UpdateMatch(room)));
	}

	private static void WriteList(BinaryWriter writer, IReadOnlyList<int> values)
	{
		writer.Write(checked((ushort)values.Count));
		foreach (var value in values) writer.Write(value);
	}

	private static void WriteString(BinaryWriter writer, string value)
	{
		if (value.Length == 0)
		{
			writer.Write((byte)0);
			return;
		}

		var bytes = Encoding.UTF8.GetBytes(value);
		writer.Write((byte)0x0B);
		for (var rest = (uint)bytes.Length;;)
		{
			var part = (byte)(rest & 0x7F);
			rest >>= 7;
			if (rest == 0)
			{
				writer.Write(part);
				break;
			}

			writer.Write((byte)(part | 0x80));
		}

		writer.Write(bytes);
	}

	#region Presence

	/// <summary>Builds the keep-alive packet.</summary>
	public static byte[] Ping()
	{
		return Frame(ClientPacketType.Ping, _ => { });
	}

	/// <summary>Builds the sign-out packet.</summary>
	public static byte[] Logout()
	{
		return Frame(ClientPacketType.Logout, writer => writer.Write(0));
	}

	/// <summary>Builds a status change.</summary>
	public static byte[] ChangeAction(ChangeAction action)
	{
		return Frame(ClientPacketType.ChangeAction, writer =>
		{
			writer.Write(action.Action);
			WriteString(writer, action.InfoText);
			WriteString(writer, action.MapMd5);
			writer.Write(action.Mods);
			writer.Write(action.Mode);
			writer.Write(action.MapId);
		});
	}

	/// <summary>Builds the request for the client's own status.</summary>
	public static byte[] RequestStatusUpdate()
	{
		return Frame(ClientPacketType.RequestStatusUpdate, _ => { });
	}

	/// <summary>Builds the request for the stats of some users.</summary>
	public static byte[] UserStatsRequest(IReadOnlyList<int> userIds)
	{
		return Frame(ClientPacketType.UserStatsRequest, writer => WriteList(writer, userIds));
	}

	/// <summary>Builds the request for the presence of some users.</summary>
	public static byte[] UserPresenceRequest(IReadOnlyList<int> userIds)
	{
		return Frame(ClientPacketType.UserPresenceRequest, writer => WriteList(writer, userIds));
	}

	/// <summary>Builds the request for the presence of every online user.</summary>
	public static byte[] UserPresenceRequestAll()
	{
		return Frame(ClientPacketType.UserPresenceRequestAll, _ => { });
	}

	/// <summary>Builds the choice of which user updates the client receives.</summary>
	public static byte[] ReceiveUpdates(int filter)
	{
		return Frame(ClientPacketType.ReceiveUpdates, writer => writer.Write(filter));
	}

	/// <summary>Builds the addition of a friend.</summary>
	public static byte[] FriendAdd(int userId)
	{
		return Frame(ClientPacketType.FriendAdd, writer => writer.Write(userId));
	}

	/// <summary>Builds the removal of a friend.</summary>
	public static byte[] FriendRemove(int userId)
	{
		return Frame(ClientPacketType.FriendRemove, writer => writer.Write(userId));
	}

	/// <summary>Builds the choice to refuse private messages from non-friends.</summary>
	public static byte[] ToggleBlockNonFriendDms(bool blocked)
	{
		return Frame(ClientPacketType.ToggleBlockNonFriendDms, writer => writer.Write(blocked ? 1 : 0));
	}

	#endregion

	#region Chat

	/// <summary>Builds a message to a channel; <paramref name="channel" /> may be <c>#multiplayer</c> or <c>#spectator</c>.</summary>
	public static byte[] SendPublicMessage(string channel, string text)
	{
		return FromMessage(ClientPacketType.SendPublicMessage, text, channel);
	}

	/// <summary>Builds a "/me" message to a channel.</summary>
	public static byte[] SendPublicAction(string channel, string text)
	{
		return FromMessage(ClientPacketType.SendPublicMessage, ChatAction.Write(text), channel);
	}

	/// <summary>Builds a private message to a user by name.</summary>
	public static byte[] SendPrivateMessage(string userName, string text)
	{
		return FromMessage(ClientPacketType.SendPrivateMessage, text, userName);
	}

	/// <summary>Builds the away message; an empty text clears it.</summary>
	public static byte[] SetAwayMessage(string text)
	{
		return FromMessage(ClientPacketType.SetAwayMessage, text, string.Empty);
	}

	/// <summary>Builds the joining of a channel.</summary>
	public static byte[] ChannelJoin(string channel)
	{
		return Frame(ClientPacketType.ChannelJoin, writer => WriteString(writer, channel));
	}

	/// <summary>Builds the leaving of a channel.</summary>
	public static byte[] ChannelPart(string channel)
	{
		return Frame(ClientPacketType.ChannelPart, writer => WriteString(writer, channel));
	}

	#endregion

	#region Multiplayer

	/// <summary>Builds the start of watching the room list.</summary>
	public static byte[] JoinLobby()
	{
		return Frame(ClientPacketType.JoinLobby, _ => { });
	}

	/// <summary>Builds the end of watching the room list.</summary>
	public static byte[] PartLobby()
	{
		return Frame(ClientPacketType.PartLobby, _ => { });
	}

	/// <summary>Builds a room with every slot open and the given settings.</summary>
	public static RoomPacket BlankRoom(string name, string password = "", int mode = 0, int teamType = 0,
		int winCondition = 0, int mods = 0, bool freeMods = false, string mapMd5 = "", int mapId = 0,
		string mapName = "")
	{
		var slots = Enumerable.Range(0, PacketWriter.SlotCount)
			.Select(_ => new RoomSlotPacket(OpenSlot, 0, 0, null)).ToList();
		return new RoomPacket(0, false, mods, name, password, mapName, mapId, mapMd5, slots, RoomPacket.NoHostId,
			mode, winCondition, teamType, freeMods, 0);
	}

	/// <summary>Builds the creation of a room.</summary>
	public static byte[] CreateMatch(RoomPacket room)
	{
		return FromRoom(ClientPacketType.CreateMatch, room);
	}

	/// <summary>Builds the joining of a room.</summary>
	public static byte[] JoinMatch(int roomId, string password = "")
	{
		return Frame(ClientPacketType.JoinMatch, writer =>
		{
			writer.Write(roomId);
			WriteString(writer, password);
		});
	}

	/// <summary>Builds the leaving of the room.</summary>
	public static byte[] PartMatch()
	{
		return Frame(ClientPacketType.PartMatch, _ => { });
	}

	/// <summary>Builds the move to another slot.</summary>
	public static byte[] MatchChangeSlot(int slot)
	{
		return Frame(ClientPacketType.MatchChangeSlot, writer => writer.Write(slot));
	}

	/// <summary>Builds the ready mark.</summary>
	public static byte[] MatchReady()
	{
		return Frame(ClientPacketType.MatchReady, _ => { });
	}

	/// <summary>Builds the end of the ready mark.</summary>
	public static byte[] MatchNotReady()
	{
		return Frame(ClientPacketType.MatchNotReady, _ => { });
	}

	/// <summary>Builds the locking or unlocking of a slot.</summary>
	public static byte[] MatchLock(int slot)
	{
		return Frame(ClientPacketType.MatchLock, writer => writer.Write(slot));
	}

	/// <summary>Builds a change of the room settings; the room carries the whole new settings.</summary>
	public static byte[] MatchChangeSettings(RoomPacket room)
	{
		return FromRoom(ClientPacketType.MatchChangeSettings, room);
	}

	/// <summary>Builds a change of the room password.</summary>
	public static byte[] MatchChangePassword(RoomPacket room, string password)
	{
		return FromRoom(ClientPacketType.MatchChangePassword, room with { Password = password });
	}

	/// <summary>Builds the start of the round.</summary>
	public static byte[] MatchStart()
	{
		return Frame(ClientPacketType.MatchStart, _ => { });
	}

	/// <summary>Builds the mark that the beatmap finished loading.</summary>
	public static byte[] MatchLoadComplete()
	{
		return Frame(ClientPacketType.MatchLoadComplete, _ => { });
	}

	/// <summary>Builds a score frame of the round.</summary>
	public static byte[] MatchScoreUpdate(ScoreFrame frame)
	{
		// A score frame has the same layout in both directions, so the encoder of the server's packet is reused.
		return Rewrap(ClientPacketType.MatchScoreUpdate, PacketWriter.Build(writer => writer.MatchScoreUpdate(frame)));
	}

	/// <summary>Builds the mark that the play finished.</summary>
	public static byte[] MatchComplete()
	{
		return Frame(ClientPacketType.MatchComplete, _ => { });
	}

	/// <summary>Builds the request to skip the intro.</summary>
	public static byte[] MatchSkipRequest()
	{
		return Frame(ClientPacketType.MatchSkipRequest, _ => { });
	}

	/// <summary>Builds the mark that the player failed.</summary>
	public static byte[] MatchFailed()
	{
		return Frame(ClientPacketType.MatchFailed, _ => { });
	}

	/// <summary>Builds the change of team.</summary>
	public static byte[] MatchChangeTeam()
	{
		return Frame(ClientPacketType.MatchChangeTeam, _ => { });
	}

	/// <summary>Builds a change of the player's own mods in a free-mod room.</summary>
	public static byte[] MatchChangeMods(int mods)
	{
		return Frame(ClientPacketType.MatchChangeMods, writer => writer.Write(mods));
	}

	/// <summary>Builds the handing of the host to the player in a slot.</summary>
	public static byte[] MatchTransferHost(int slot)
	{
		return Frame(ClientPacketType.MatchTransferHost, writer => writer.Write(slot));
	}

	/// <summary>Builds an invitation of a user to the room.</summary>
	public static byte[] MatchInvite(int userId)
	{
		return Frame(ClientPacketType.MatchInvite, writer => writer.Write(userId));
	}

	/// <summary>Builds the mark that the player has the beatmap.</summary>
	public static byte[] MatchHasBeatmap()
	{
		return Frame(ClientPacketType.MatchHasBeatmap, _ => { });
	}

	/// <summary>Builds the mark that the player lacks the beatmap.</summary>
	public static byte[] MatchNoBeatmap()
	{
		return Frame(ClientPacketType.MatchNoBeatmap, _ => { });
	}

	#endregion

	#region Spectating and tourney

	/// <summary>Builds the start of spectating a user.</summary>
	public static byte[] StartSpectating(int userId)
	{
		return Frame(ClientPacketType.StartSpectating, writer => writer.Write(userId));
	}

	/// <summary>Builds the end of spectating.</summary>
	public static byte[] StopSpectating()
	{
		return Frame(ClientPacketType.StopSpectating, _ => { });
	}

	/// <summary>Builds a bundle of the player's replay frames for the spectators.</summary>
	public static byte[] SpectateFrames(SpectatorBundle bundle)
	{
		return Frame(ClientPacketType.SpectateFrames, writer =>
		{
			writer.Write(0);
			writer.Write((ushort)bundle.ReplayFrames.Count);
			foreach (var frame in bundle.ReplayFrames)
			{
				writer.Write((byte)frame.Keys);
				writer.Write((byte)frame.Drum);
				writer.Write(frame.Position.X);
				writer.Write(frame.Position.Y);
				writer.Write((int)frame.Time.TotalMilliseconds);
			}

			writer.Write((byte)bundle.Action);
			var score = PacketWriter.Build(packets => packets.MatchScoreUpdate(bundle.ScoreFrame));
			writer.Write(score.AsSpan(HeaderSize));
			writer.Write((ushort)bundle.Sequence);
		});
	}

	/// <summary>Builds the request for the state of a room, from a tourney client.</summary>
	public static byte[] TournamentMatchInfoRequest(int roomId)
	{
		return Frame(ClientPacketType.TournamentMatchInfoRequest, writer => writer.Write(roomId));
	}

	/// <summary>Builds the joining of a room's channel, from a tourney client.</summary>
	public static byte[] TournamentJoinMatchChannel(int roomId)
	{
		return Frame(ClientPacketType.TournamentJoinMatchChannel, writer => writer.Write(roomId));
	}

	/// <summary>Builds the leaving of a room's channel, from a tourney client.</summary>
	public static byte[] TournamentLeaveMatchChannel(int roomId)
	{
		return Frame(ClientPacketType.TournamentLeaveMatchChannel, writer => writer.Write(roomId));
	}

	#endregion
}