using System.Drawing;
using Basil.Domain.Game.Scores;
using Basil.Domain.Game.Spectating;
using Basil.Protocol.Game.Stable.Models.Beatmaps;
using Basil.Protocol.Game.Stable.Models.Chat;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Models.Users;
using Basil.Protocol.Game.Stable.Wire.Binary;

namespace Basil.Protocol.Game.Stable.Wire.Packets;

public sealed class PacketReader(Stream stream)
{
	private readonly BinaryReader _reader = new(stream);

	public (ClientPacketType Type, int Length) ReadHeader()
	{
		var type = (ClientPacketType)_reader.ReadUInt16();
		_reader.ReadByte(); // padding
		var length = _reader.ReadInt32();

		return length < 0
			? throw new InvalidDataException("The packet length is negative.")
			: (type, length);
	}

	public StableMessage ReadMessage()
	{
		return new StableMessage(_reader.ReadOsuString(), _reader.ReadOsuString(), _reader.ReadOsuString(),
			_reader.ReadInt32());
	}

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

		ScoreV2Portion? scoreV2Portion = null;
		if (scoreV2)
			scoreV2Portion = new ScoreV2Portion(_reader.ReadDouble(), _reader.ReadDouble());

		return new ScoreFrame(TimeSpan.FromMilliseconds(time), id,
			new ScoreState(new HitCounts(num300, num100, num50, numGeki, numKatu, numMiss), totalScore, maxCombo,
				currentCombo, perfect),
			currentHp, tagByte, scoreV2Portion);
	}

	public ReplayFrame ReadReplayFrame()
	{
		return new ReplayFrame(
			(KeyState)_reader.ReadByte(),
			(TaikoDrumState)_reader.ReadByte(),
			new PointF(_reader.ReadSingle(), _reader.ReadSingle()),
			TimeSpan.FromMilliseconds(_reader.ReadInt32()));
	}

	public SpectatorBundle ReadReplayFrameBundle()
	{
		_reader.ReadInt32(); // the leading extra value, which nothing uses
		var frameCount = _reader.ReadUInt16();
		var frames = new ReplayFrame[frameCount];
		for (var i = 0; i < frameCount; i++)
			frames[i] = ReadReplayFrame();

		var action = (SpectatorAction)_reader.ReadByte();
		var scoreFrame = ReadScoreFrame();
		var sequence = _reader.ReadUInt16();

		return new SpectatorBundle(action, frames, scoreFrame, sequence);
	}

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

	public static PacketReader ForPayload(byte[] payload)
	{
		return new PacketReader(new MemoryStream(payload, false));
	}

	public byte ReadByte()
	{
		return _reader.ReadByte();
	}

	public int ReadInt32()
	{
		return _reader.ReadInt32();
	}

	public uint ReadUInt32()
	{
		return _reader.ReadUInt32();
	}

	public string ReadOsuString()
	{
		return _reader.ReadOsuString();
	}

	public IReadOnlyList<int> ReadInt32List()
	{
		return _reader.ReadInt32List();
	}

	public ChangeAction ReadChangeAction()
	{
		return new ChangeAction(_reader.ReadByte(), _reader.ReadOsuString(), _reader.ReadOsuString(),
			_reader.ReadUInt32(), _reader.ReadByte(), _reader.ReadInt32());
	}

	public JoinMatchRequest ReadJoinMatch()
	{
		return new JoinMatchRequest(_reader.ReadInt32(), _reader.ReadOsuString());
	}

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