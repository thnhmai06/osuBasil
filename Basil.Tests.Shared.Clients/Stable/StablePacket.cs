using Basil.Domain.Spectating;
using Basil.Protocol.Game.Stable.Models.Chat;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Wire.Packets;

namespace Basil.Tests.Shared.Clients.Stable;

/// <summary>A packet the server sent to an osu! client, read into a typed event.</summary>
public abstract record StablePacket
{
	/// <summary>Reads the packets of one response body.</summary>
	/// <param name="body">The bytes of the response.</param>
	/// <returns>The packets in order; an id the clients do not follow is an <see cref="OtherPacket" />.</returns>
	/// <exception cref="InvalidDataException">The body is cut inside a packet or a packet is malformed.</exception>
	public static IReadOnlyList<StablePacket> ReadAll(ReadOnlySpan<byte> body)
	{
		const int headerSize = 7;
		var packets = new List<StablePacket>();
		var offset = 0;
		while (offset < body.Length)
		{
			if (body.Length - offset < headerSize) throw new InvalidDataException("The packet header is cut.");

			var id = BitConverter.ToUInt16(body.Slice(offset, sizeof(ushort)));
			var length = BitConverter.ToInt32(body.Slice(offset + 3, sizeof(int)));
			offset += headerSize;
			if (length < 0 || body.Length - offset < length)
				throw new InvalidDataException("The packet payload is cut.");

			try
			{
				packets.Add(Read(id, body.Slice(offset, length).ToArray()));
			}
			catch (EndOfStreamException exception)
			{
				throw new InvalidDataException($"The payload of packet {id} is cut.", exception);
			}

			offset += length;
		}

		return packets;
	}

	private static StablePacket Read(ushort id, byte[] payload)
	{
		var reader = PacketReader.ForPayload(payload);
		return id > byte.MaxValue
			? new OtherPacket(id, payload)
			: (ServerPacketType)id switch
			{
				ServerPacketType.UserId => new LoginReply(reader.ReadInt32()),
				ServerPacketType.UpdateMatch => new RoomUpdated(RoomOf(reader), false),
				ServerPacketType.NewMatch => new RoomUpdated(RoomOf(reader), true),
				ServerPacketType.DisposeMatch => new RoomDisposed(reader.ReadInt32()),
				ServerPacketType.MatchJoinSuccess => new MatchJoined(RoomOf(reader)),
				ServerPacketType.MatchJoinFail => new MatchJoinFailed(),
				ServerPacketType.MatchStart => new MatchStarted(RoomOf(reader)),
				ServerPacketType.MatchAllPlayersLoaded => new MatchAllPlayersLoaded(),
				ServerPacketType.MatchComplete => new MatchCompleted(),
				ServerPacketType.MatchScoreUpdate => new MatchScoreReceived(reader.ReadScoreFrame()),
				ServerPacketType.MatchTransferHost => new MatchHostTransferred(),
				ServerPacketType.SpectatorJoined => new SpectatorJoined(reader.ReadInt32()),
				ServerPacketType.SpectatorLeft => new SpectatorLeft(reader.ReadInt32()),
				ServerPacketType.SpectateFrames => new SpectateFramesReceived(payload, BundleOf(payload)),
				ServerPacketType.SendMessage => new MessageReceived(reader.ReadMessage()),
				ServerPacketType.ChannelJoinSuccess => new ChannelJoined(reader.ReadOsuString()),
				ServerPacketType.Notification => new NotificationReceived(reader.ReadOsuString()),
				ServerPacketType.Restart => new ServerRestarting(reader.ReadInt32()),
				_ => new OtherPacket(id, payload)
			};
	}

	private static SpectatorBundle? BundleOf(byte[] payload)
	{
		try
		{
			return PacketReader.ForPayload(payload).ReadReplayFrameBundle();
		}
		catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
		{
			return null;
		}
	}

	// The wire form of a room is flat arrays; the room model has one object per slot.
	private static RoomPacket RoomOf(PacketReader reader)
	{
		var state = reader.ReadMatch();
		var slots = new List<RoomSlotPacket>(PacketWriter.SlotCount);
		var seated = 0;
		for (var index = 0; index < PacketWriter.SlotCount; index++)
		{
			var status = state.SlotStatuses[index];
			var mods = state.FreeMods ? state.SlotMods[index] : 0;
			var occupied = new RoomSlotPacket(status, state.SlotTeams[index], mods, null).HasPlayer;
			slots.Add(new RoomSlotPacket(status, state.SlotTeams[index], mods,
				occupied ? state.SlotIds[seated++] : null));
		}

		return new RoomPacket(state.Id, state.InProgress, state.Mods, state.Name, state.Password, state.MapName,
			state.MapId, state.MapMd5, slots, state.HostId, state.Mode, state.WinCondition, state.TeamType,
			state.FreeMods, state.Seed);
	}
}

/// <summary>The answer to a sign-in: the user id, or a negative failure code when the server refused it.</summary>
public sealed record LoginReply(int UserId) : StablePacket;

/// <summary>A room was announced (<paramref name="IsNew" />) or changed.</summary>
public sealed record RoomUpdated(RoomPacket Room, bool IsNew) : StablePacket;

/// <summary>A room was closed.</summary>
public sealed record RoomDisposed(int RoomId) : StablePacket;

/// <summary>The client joined or created a room.</summary>
public sealed record MatchJoined(RoomPacket Room) : StablePacket;

/// <summary>The client could not join or create a room.</summary>
public sealed record MatchJoinFailed : StablePacket;

/// <summary>The round of the client's room began.</summary>
public sealed record MatchStarted(RoomPacket Room) : StablePacket;

/// <summary>Every player of the round finished loading.</summary>
public sealed record MatchAllPlayersLoaded : StablePacket;

/// <summary>The round of the client's room ended.</summary>
public sealed record MatchCompleted : StablePacket;

/// <summary>A player of the round sent a score frame.</summary>
public sealed record MatchScoreReceived(ScoreFrame Frame) : StablePacket;

/// <summary>The host of the client's room changed.</summary>
public sealed record MatchHostTransferred : StablePacket;

/// <summary>A user began spectating the client.</summary>
public sealed record SpectatorJoined(int UserId) : StablePacket;

/// <summary>A user stopped spectating the client.</summary>
public sealed record SpectatorLeft(int UserId) : StablePacket;

/// <summary>Frames of the player the client spectates.</summary>
/// <param name="Raw">The payload as it came.</param>
/// <param name="Bundle">The payload read as a bundle, or <see langword="null" /> when it is not one.</param>
public sealed record SpectateFramesReceived(byte[] Raw, SpectatorBundle? Bundle) : StablePacket;

/// <summary>A chat message, public or private.</summary>
public sealed record MessageReceived(StableMessage Message) : StablePacket;

/// <summary>The client joined a chat channel.</summary>
public sealed record ChannelJoined(string Name) : StablePacket;

/// <summary>A notification the client shows.</summary>
public sealed record NotificationReceived(string Text) : StablePacket;

/// <summary>The server asks the client to sign in again after the given time.</summary>
public sealed record ServerRestarting(int Milliseconds) : StablePacket;

/// <summary>A packet the clients do not follow, kept by id.</summary>
public sealed record OtherPacket(int Id, byte[] Payload) : StablePacket;