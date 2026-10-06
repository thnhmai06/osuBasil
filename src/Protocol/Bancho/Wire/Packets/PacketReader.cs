using Basil.Protocol.Bancho.Models.Chat;
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Binary;

namespace Basil.Protocol.Bancho.Wire.Packets;

/// <summary>
///     Reads Bancho packet primitives from a client request body. This is the read side of the
///     Bancho binary protocol; all multibyte integers are little-endian, per the osu! protocol.
/// </summary>
/// <param name="stream">The stream to read the packet body from.</param>
public sealed class PacketReader(Stream stream)
{
	private readonly BinaryReader _reader = new(stream);

	/// <summary>Reads the 7-byte Bancho packet header (id: u16, padding: u8, length: u32).</summary>
	/// <returns>The packet type and the declared payload length.</returns>
	public (ClientPacketType Type, int Length) ReadHeader()
	{
		var type = (ClientPacketType)_reader.ReadUInt16();
		_reader.ReadByte(); // padding
		var length = _reader.ReadInt32();

		return (type, length);
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
		var id = _reader.ReadInt16();
		var inProgress = _reader.ReadSByte() == 1;
		var powerplay = _reader.ReadSByte();
		var mods = _reader.ReadInt32();
		var name = _reader.ReadOsuString();
		var password = _reader.ReadOsuString();
		var mapName = _reader.ReadOsuString();
		var mapId = _reader.ReadInt32();
		var mapMd5 = _reader.ReadOsuString();

		var slotStatuses = new int[16];
		for (var i = 0; i < 16; i++) slotStatuses[i] = _reader.ReadSByte();

		var slotTeams = new int[16];
		for (var i = 0; i < 16; i++) slotTeams[i] = _reader.ReadSByte();

		var slotIds =
			(
				from status in slotStatuses
				where RoomSlotPacket.IsHasPlayerStatus(status)
				select _reader.ReadInt32())
			.ToList();

		var hostId = _reader.ReadInt32();
		var mode = _reader.ReadSByte();
		var winCondition = _reader.ReadSByte();
		var teamType = _reader.ReadSByte();
		var freeMods = _reader.ReadSByte() == 1;

		var slotMods = Array.Empty<int>();
		if (freeMods)
		{
			slotMods = new int[16];
			for (var i = 0; i < 16; i++) slotMods[i] = _reader.ReadInt32();
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
}