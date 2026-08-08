using Basil.Protocol;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.LoadTests.Client;

/// <summary>
///     Builds client-to-server bancho packets, reusing <see cref="Basil.Protocol" />'s serialization
///     primitives and wire-shape writers (<see cref="ServerPacketWriter.WriteMatch" /> for the match
///     struct, <see cref="ServerPacketWriter.WriteScoreFrame" /> for score frames) so the exact same
///     production code that encodes a match/score frame for the server-to-client direction encodes it
///     here too. Nothing about packet framing or field layout is reimplemented.
/// </summary>
public static class ClientPacketWriter
{
	/// <summary>
	///     Wraps a client packet id with the shared 7-byte header. <see cref="Basil.Protocol.Packets.PacketWriter.Wrap" />
	///     only accepts <see cref="ServerPackets" /> because Basil.Protocol has no client-side writer;
	///     both enums are <c>: byte</c> (verified max members: ClientPackets 109, ServerPackets 107), so
	///     this cast is lossless in both directions. This is the single place that cast happens.
	/// </summary>
	private static byte[] Wrap(ClientPackets id, ReadOnlySpan<byte> payload)
	{
		return PacketWriter.Wrap((ServerPackets)(byte)id, payload);
	}

	private static byte[] Empty(ClientPackets id)
	{
		return Wrap(id, []);
	}

	public static byte[] Ping()
	{
		return Empty(ClientPackets.Ping);
	}

	/// <summary>
	///     Builds a Logout packet. Unlike most empty-bodied packets, <c>LogoutHandler</c> unconditionally
	///     reads a leading reserved <c>i32</c> from the payload before doing anything else — an
	///     empty payload throws server-side (caught and swallowed by <c>PacketDispatcher</c>, but the
	///     logout itself silently never happens), so this always carries that reserved field.
	/// </summary>
	public static byte[] Logout()
	{
		return Wrap(ClientPackets.Logout, BinaryWriter.WriteInt32(0));
	}

	public static byte[] JoinLobby()
	{
		return Empty(ClientPackets.JoinLobby);
	}

	public static byte[] PartLobby()
	{
		return Empty(ClientPackets.PartLobby);
	}

	public static byte[] ChannelJoin(string channelName)
	{
		return Wrap(ClientPackets.ChannelJoin, BinaryWriter.WriteString(channelName));
	}

	public static byte[] ChannelPart(string channelName)
	{
		return Wrap(ClientPackets.ChannelPart, BinaryWriter.WriteString(channelName));
	}

	/// <summary>Field order (Sender, Text, Recipient, SenderId) matches <see cref="BanchoMessage" /> and <c>PacketReader.ReadMessage</c> exactly.</summary>
	public static byte[] SendPublicMessage(BanchoMessage message)
	{
		var payload = Concat(
			BinaryWriter.WriteString(message.Sender),
			BinaryWriter.WriteString(message.Text),
			BinaryWriter.WriteString(message.Recipient),
			BinaryWriter.WriteInt32(message.SenderId));
		return Wrap(ClientPackets.SendPublicMessage, payload);
	}

	public static byte[] SendPrivateMessage(BanchoMessage message)
	{
		var payload = Concat(
			BinaryWriter.WriteString(message.Sender),
			BinaryWriter.WriteString(message.Text),
			BinaryWriter.WriteString(message.Recipient),
			BinaryWriter.WriteInt32(message.SenderId));
		return Wrap(ClientPackets.SendPrivateMessage, payload);
	}

	public static byte[] CreateMatch(MatchPacket match)
	{
		return Wrap(ClientPackets.CreateMatch, ServerPacketWriter.WriteMatch(match, true));
	}

	public static byte[] JoinMatch(int matchId, string password)
	{
		var payload = Concat(BinaryWriter.WriteInt32(matchId), BinaryWriter.WriteString(password));
		return Wrap(ClientPackets.JoinMatch, payload);
	}

	public static byte[] PartMatch()
	{
		return Empty(ClientPackets.PartMatch);
	}

	public static byte[] MatchChangeSlot(int slotId)
	{
		return Wrap(ClientPackets.MatchChangeSlot, BinaryWriter.WriteInt32(slotId));
	}

	public static byte[] MatchReady()
	{
		return Empty(ClientPackets.MatchReady);
	}

	public static byte[] MatchNotReady()
	{
		return Empty(ClientPackets.MatchNotReady);
	}

	public static byte[] MatchLock()
	{
		return Empty(ClientPackets.MatchLock);
	}

	public static byte[] MatchChangeSettings(MatchPacket match)
	{
		return Wrap(ClientPackets.MatchChangeSettings, ServerPacketWriter.WriteMatch(match, true));
	}

	public static byte[] MatchStart()
	{
		return Empty(ClientPackets.MatchStart);
	}

	public static byte[] MatchScoreUpdate(ScoreFrame frame)
	{
		return Wrap(ClientPackets.MatchScoreUpdate, ServerPacketWriter.WriteScoreFrame(frame));
	}

	public static byte[] MatchComplete()
	{
		return Empty(ClientPackets.MatchComplete);
	}

	public static byte[] MatchChangeMods(int mods)
	{
		return Wrap(ClientPackets.MatchChangeMods, BinaryWriter.WriteInt32(mods));
	}

	public static byte[] MatchLoadComplete()
	{
		return Empty(ClientPackets.MatchLoadComplete);
	}

	public static byte[] MatchChangeTeam()
	{
		return Empty(ClientPackets.MatchChangeTeam);
	}

	public static byte[] MatchTransferHost(int userId)
	{
		return Wrap(ClientPackets.MatchTransferHost, BinaryWriter.WriteInt32(userId));
	}

	private static byte[] Concat(params ReadOnlySpan<byte[]> parts)
	{
		var length = 0;
		foreach (var part in parts) length += part.Length;

		var result = new byte[length];
		var offset = 0;
		foreach (var part in parts)
		{
			part.CopyTo(result.AsSpan(offset));
			offset += part.Length;
		}

		return result;
	}
}
