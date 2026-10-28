using Basil.Protocol.Bancho.Models.Beatmaps;
using Basil.Protocol.Bancho.Models.Chat;
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Presence;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Binary;

namespace Basil.Protocol.Bancho.Wire.Packets;

/// <summary>
///     Reads Bancho packets from a client stream positioned at a packet boundary. This is the read side of the
///     Bancho binary protocol; all multibyte integers are little-endian, per the osu! protocol.
/// </summary>
/// <remarks>
///     The reader decodes wire shapes only and gives no business meaning to the values it reads. A stream that ends
///     before a value is complete throws <see cref="EndOfStreamException" />.
/// </remarks>
/// <param name="stream">The stream to read the packets from.</param>
public sealed class PacketReader(Stream stream)
{
	private readonly BinaryReader _reader = new(stream);

	/// <summary>Reads the 7-byte Bancho packet header (id: u16, padding: u8, length: u32).</summary>
	/// <returns>The packet type and the declared payload length.</returns>
	/// <exception cref="InvalidDataException">The declared payload length is negative.</exception>
	public (ClientPacketType Type, int Length) ReadHeader()
	{
		var type = (ClientPacketType)_reader.ReadUInt16();
		_reader.ReadByte(); // padding
		var length = _reader.ReadInt32();

		return length < 0
			? throw new InvalidDataException("The packet length is negative.")
			: (type, length);
	}

	/// <summary>Reads a <see cref="BanchoMessage" /> from the buffer.</summary>
	/// <returns>The message read.</returns>
	public BanchoMessage ReadMessage()
	{
		return new BanchoMessage(_reader.ReadOsuString(), _reader.ReadOsuString(), _reader.ReadOsuString(),
			_reader.ReadInt32());
	}

	/// <summary>Reads a multiplayer match from the buffer into a <see cref="RoomStatePacket" />.</summary>
	/// <returns>The match state read.</returns>
	public RoomStatePacket ReadMatch()
	{
		var id = _reader.ReadUInt16();
		var inProgress = _reader.ReadSByte() == 1;
		var powerplay = _reader.ReadSByte();
		var mods = _reader.ReadInt32();
		var name = _reader.ReadOsuString();
		var password = _reader.ReadOsuString();
		var mapName = _reader.ReadOsuString();
		var mapId = _reader.ReadInt32();
		var mapMd5 = _reader.ReadOsuString();

		var slotStatuses = new int[PacketWriter.SlotCount];
		for (var index = 0; index < PacketWriter.SlotCount; index++)
			slotStatuses[index] = _reader.ReadByte();

		var slotTeams = new int[PacketWriter.SlotCount];
		for (var index = 0; index < PacketWriter.SlotCount; index++)
			slotTeams[index] = _reader.ReadByte();

		var slotIds = new List<int>();
		for (var index = 0; index < PacketWriter.SlotCount; index++)
			if (RoomSlotPacket.HasPlayerStatus(slotStatuses[index]))
				slotIds.Add(_reader.ReadInt32());

		var hostId = _reader.ReadInt32();
		var mode = _reader.ReadSByte();
		var winCondition = _reader.ReadSByte();
		var teamType = _reader.ReadSByte();
		var freeMods = _reader.ReadSByte() == 1;

		var slotMods = Array.Empty<int>();
		if (freeMods)
		{
			slotMods = new int[PacketWriter.SlotCount];
			for (var index = 0; index < PacketWriter.SlotCount; index++)
				slotMods[index] = _reader.ReadInt32();
		}

		var seed = _reader.ReadInt32();

		return new RoomStatePacket(
			id, inProgress, powerplay, mods, name, password, mapName, mapId, mapMd5,
			slotStatuses, slotTeams, slotIds, hostId, mode, winCondition, teamType, freeMods,
			slotMods, seed);
	}

	/// <summary>
	///     Reads a fixed 29-byte score frame, plus two doubles when score v2 is active, into a
	///     <see cref="ScoreFrame" />.
	/// </summary>
	/// <returns>The score frame read.</returns>
	public ScoreFrame ReadScoreFrame()
	{
		// 29-byte scoreframe layout: i32 time, u8 id, six u16 counts, i32 total, u16 max/current
		// combo, u8 perfect, u8 hp, u8 tag, u8 scoreV2.
		var time = _reader.ReadInt32();
		var id = _reader.ReadByte();
		var num300 = _reader.ReadUInt16();
		var num100 = _reader.ReadUInt16();
		var num50 = _reader.ReadUInt16();
		var numGeki = _reader.ReadUInt16();
		var numKatu = _reader.ReadUInt16();
		var numMiss = _reader.ReadUInt16();
		var totalScore = _reader.ReadInt32();
		var maxCombo = _reader.ReadUInt16();
		var currentCombo = _reader.ReadUInt16();
		var perfect = _reader.ReadByte() != 0;
		var currentHp = _reader.ReadByte();
		var tagByte = _reader.ReadByte();
		var scoreV2 = _reader.ReadByte() != 0;

		double? comboPortion = null;
		double? bonusPortion = null;
		if (scoreV2)
		{
			comboPortion = _reader.ReadDouble();
			bonusPortion = _reader.ReadDouble();
		}

		return new ScoreFrame
		{
			Time = time,
			Id = id,
			Num300 = num300,
			Num100 = num100,
			Num50 = num50,
			NumGeki = numGeki,
			NumKatu = numKatu,
			NumMiss = numMiss,
			TotalScore = totalScore,
			MaxCombo = maxCombo,
			CurrentCombo = currentCombo,
			Perfect = perfect,
			CurrentHp = currentHp,
			TagByte = tagByte,
			ScoreV2 = scoreV2,
			ComboPortion = comboPortion,
			BonusPortion = bonusPortion
		};
	}

	/// <summary>Reads a single replay frame from the buffer.</summary>
	/// <returns>The replay frame read.</returns>
	public ReplayFrame ReadReplayFrame()
	{
		return new ReplayFrame(
			(Keys)_reader.ReadByte(),
			(TaikoByte)_reader.ReadByte(),
			_reader.ReadSingle(),
			_reader.ReadSingle(),
			_reader.ReadInt32());
	}

	/// <summary>Reads a complete replay frame bundle, including its frames, action, score frame, and sequence number.</summary>
	/// <returns>The replay frame bundle read.</returns>
	public ReplayFrameBundle ReadReplayFrameBundle()
	{
		var extra = _reader.ReadInt32();
		var frameCount = _reader.ReadUInt16();
		var frames = new ReplayFrame[frameCount];
		for (var i = 0; i < frameCount; i++)
			frames[i] = ReadReplayFrame();

		var action = (ReplayAction)_reader.ReadByte();
		var scoreFrame = ReadScoreFrame();
		var sequence = _reader.ReadUInt16();

		return new ReplayFrameBundle(frames, scoreFrame, action, extra, sequence);
	}

	/// <summary>
	///     Splits a client request body into its packets. Each packet carries exactly the payload its header declared, so
	///     a reader over it can never run into the next packet.
	/// </summary>
	/// <param name="body">The request body: zero or more packets, each a 7-byte header and its payload.</param>
	/// <returns>The packets of known types, in order. Packets of unknown types are skipped by their declared length.</returns>
	/// <exception cref="EndOfStreamException">The body ends inside a header or before a declared payload is complete.</exception>
	public static IReadOnlyList<ClientPacket> Split(ReadOnlySpan<byte> body)
	{
		const int headerSize = 7;
		var packets = new List<ClientPacket>();
		var offset = 0;

		while (offset < body.Length)
		{
			if (body.Length - offset < headerSize) throw new EndOfStreamException();

			var type = (ClientPacketType)BitConverter.ToUInt16(body.Slice(offset, sizeof(ushort)));
			var length = BitConverter.ToInt32(body.Slice(offset + 3, sizeof(int)));
			offset += headerSize;

			if (length < 0) throw new InvalidDataException("The packet length is negative.");
			if (body.Length - offset < length) throw new EndOfStreamException();

			if (Enum.IsDefined(type)) packets.Add(new ClientPacket(type, body.Slice(offset, length).ToArray()));
			offset += length;
		}

		return packets;
	}

	/// <summary>Creates a reader over the payload of one packet.</summary>
	/// <param name="payload">The payload bytes.</param>
	/// <returns>A reader positioned at the start of <paramref name="payload" />.</returns>
	public static PacketReader ForPayload(byte[] payload)
	{
		return new PacketReader(new MemoryStream(payload, false));
	}

	/// <summary>Reads an unsigned 8-bit integer.</summary>
	/// <returns>The value read.</returns>
	public byte ReadByte()
	{
		return _reader.ReadByte();
	}

	/// <summary>Reads a signed 32-bit integer.</summary>
	/// <returns>The value read.</returns>
	public int ReadInt32()
	{
		return _reader.ReadInt32();
	}

	/// <summary>Reads an unsigned 32-bit integer.</summary>
	/// <returns>The value read.</returns>
	public uint ReadUInt32()
	{
		return _reader.ReadUInt32();
	}

	/// <summary>Reads an osu! string; an absent string reads as empty.</summary>
	/// <returns>The string read.</returns>
	public string ReadOsuString()
	{
		return _reader.ReadOsuString();
	}

	/// <summary>Reads a list of 32-bit integers prefixed by a 16-bit unsigned count.</summary>
	/// <returns>The values read.</returns>
	public IReadOnlyList<int> ReadInt32List()
	{
		return _reader.ReadInt32List();
	}

	/// <summary>Reads the payload of a change-action packet.</summary>
	/// <returns>The status read.</returns>
	public ChangeAction ReadChangeAction()
	{
		return new ChangeAction(_reader.ReadByte(), _reader.ReadOsuString(), _reader.ReadOsuString(),
			_reader.ReadUInt32(), _reader.ReadByte(), _reader.ReadInt32());
	}

	/// <summary>Reads the payload of a join-match packet.</summary>
	/// <returns>The room id and password read.</returns>
	public JoinMatchRequest ReadJoinMatch()
	{
		return new JoinMatchRequest(_reader.ReadInt32(), _reader.ReadOsuString());
	}

	/// <summary>
	///     Reads the payload of a beatmap-info-request packet: a 32-bit count of file names with the names, then a 32-bit
	///     count of ids with the ids.
	/// </summary>
	/// <returns>The file names and ids read.</returns>
	/// <exception cref="InvalidDataException">A count is negative or above 65535.</exception>
	public BeatmapInfoRequest ReadBeatmapInfoRequest()
	{
		var filenames = new string[ReadCount()];
		for (var i = 0; i < filenames.Length; i++)
			filenames[i] = _reader.ReadOsuString();

		var ids = new int[ReadCount()];
		for (var i = 0; i < ids.Length; i++)
			ids[i] = _reader.ReadInt32();

		return new BeatmapInfoRequest(filenames, ids);
	}

	private int ReadCount()
	{
		var count = _reader.ReadInt32();
		// a count larger than the payload ends in EndOfStreamException while reading, so only allocation needs a cap
		return count is < 0 or > ushort.MaxValue ? throw new InvalidDataException("The count is out of range.") : count;
	}
}