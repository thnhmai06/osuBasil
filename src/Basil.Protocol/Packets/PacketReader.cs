using System.Buffers.Binary;
using System.Text;
using Basil.Protocol.Multiplayer;

namespace Basil.Protocol.Packets;

/// <summary>
///     Reads Bancho packet primitives from a client request body. This is the read side of the
///     Bancho binary protocol; the packet-id driven iteration and dispatch loop is wired up in
///     Basil.Web. All multibyte integers are little-endian, per the osu! protocol.
/// </summary>
/// <param name="buffer">The request body to read from.</param>
public sealed class PacketReader(ReadOnlyMemory<byte> buffer)
{
	private ReadOnlyMemory<byte> _buffer = buffer;

	/// <summary>Gets the number of bytes not yet consumed from the buffer.</summary>
	/// <value>The count of remaining bytes.</value>
	public int RemainingLength => _buffer.Length;

	/// <summary>Reads and copies the next <paramref name="length" /> bytes from the buffer.</summary>
	/// <param name="length">The number of bytes to read.</param>
	/// <returns>A new byte array containing the next <paramref name="length" /> bytes.</returns>
	public byte[] ReadRaw(int length)
	{
		var value = _buffer[..length].ToArray();
		_buffer = _buffer[length..];
		return value;
	}

	/// <summary>Advances past `length` bytes without allocating/copying, for skipping unhandled packet payloads.</summary>
	public void SkipRaw(int length)
	{
		_buffer = _buffer[length..];
	}

	/// <summary>Reads the 7-byte Bancho packet header (id: u16, padding: u8, length: u32).</summary>
	/// <returns>The packet type and the declared payload length.</returns>
	public (ClientPackets Type, int Length) ReadHeader()
	{
		var type = (ClientPackets)BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span);
		var length = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span[3..]);
		_buffer = _buffer[7..];
		return (type, length);
	}

	// integral types

	/// <summary>Reads a signed 8-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public int ReadI8()
	{
		var value = _buffer.Span[0];
		_buffer = _buffer[1..];
		return unchecked((sbyte)value);
	}

	/// <summary>Reads an unsigned 8-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public int ReadU8()
	{
		var value = _buffer.Span[0];
		_buffer = _buffer[1..];
		return value;
	}

	/// <summary>Reads a signed 16-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public int ReadI16()
	{
		var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Span);
		_buffer = _buffer[2..];
		return value;
	}

	/// <summary>Reads an unsigned 16-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public int ReadU16()
	{
		var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span);
		_buffer = _buffer[2..];
		return value;
	}

	/// <summary>Reads a signed 32-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public int ReadI32()
	{
		var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span);
		_buffer = _buffer[4..];
		return value;
	}

	/// <summary>Reads an unsigned 32-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public uint ReadU32()
	{
		var value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Span);
		_buffer = _buffer[4..];
		return value;
	}

	/// <summary>Reads a signed 64-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public long ReadI64()
	{
		var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Span);
		_buffer = _buffer[8..];
		return value;
	}

	/// <summary>Reads an unsigned 64-bit integer from the buffer.</summary>
	/// <returns>The value read.</returns>
	public ulong ReadU64()
	{
		var value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Span);
		_buffer = _buffer[8..];
		return value;
	}

	// floating-point types

	/// <summary>Reads a single-precision floating-point number from the buffer.</summary>
	/// <returns>The value read.</returns>
	public float ReadF32()
	{
		var value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Span);
		_buffer = _buffer[4..];
		return value;
	}

	/// <summary>Reads a double-precision floating-point number from the buffer.</summary>
	/// <returns>The value read.</returns>
	public double ReadF64()
	{
		var value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.Span);
		_buffer = _buffer[8..];
		return value;
	}

	// complex types

	// XXX: some osu! packets use i16 for array length, while others use i32
	/// <summary>Reads a list of 32-bit integers prefixed by a 16-bit unsigned count.</summary>
	/// <returns>The list of values read.</returns>
	public IReadOnlyList<int> ReadI32ListI16L()
	{
		var length = ReadU16();
		var values = new int[length];
		for (var i = 0; i < length; i++) values[i] = ReadI32();

		return values;
	}

	/// <summary>Reads a list of 32-bit integers prefixed by a 32-bit unsigned count.</summary>
	/// <returns>The list of values read.</returns>
	public IReadOnlyList<int> ReadI32ListI32L()
	{
		var length = (int)ReadU32();
		var values = new int[length];
		for (var i = 0; i < length; i++) values[i] = ReadI32();

		return values;
	}

	/// <summary>Reads an osu!-format string: an existence byte, a ULEB128 length, then the UTF-8 bytes.</summary>
	/// <returns>The string value read, or an empty string when the existence byte is <c>0x00</c>.</returns>
	public string ReadString()
	{
		var exists = _buffer.Span[0] == 0x0B;
		_buffer = _buffer[1..];

		if (!exists) return "";

		var length = 0;
		var shift = 0;

		while (true)
		{
			var b = _buffer.Span[0];
			_buffer = _buffer[1..];

			length |= (b & 0x7F) << shift;
			if ((b & 0x80) == 0) break;

			shift += 7;
		}

		var value = Encoding.UTF8.GetString(_buffer.Span[..length]);
		_buffer = _buffer[length..];
		return value;
	}

	// custom osu! types

	/// <summary>Reads a <see cref="BanchoMessage" /> from the buffer.</summary>
	/// <returns>The message read.</returns>
	public BanchoMessage ReadMessage()
	{
		return new BanchoMessage(
			ReadString(),
			ReadString(),
			ReadString(),
			ReadI32());
	}

	/// <summary>Reads a multiplayer match from the buffer into a <see cref="MatchState" />.</summary>
	/// <returns>The match state read.</returns>
	public MatchState ReadMatch()
	{
		var id = ReadI16();
		var inProgress = ReadI8() == 1;
		var powerplay = ReadI8();
		var mods = ReadI32();
		var name = ReadString();
		var password = ReadString();
		var mapName = ReadString();
		var mapId = ReadI32();
		var mapMd5 = ReadString();

		var slotStatuses = new int[16];
		for (var i = 0; i < 16; i++) slotStatuses[i] = ReadI8();

		var slotTeams = new int[16];
		for (var i = 0; i < 16; i++) slotTeams[i] = ReadI8();

		var slotIds = new List<int>();
		foreach (var status in slotStatuses)
			if (MatchSlotStatusMask.HasPlayer(status))
				slotIds.Add(ReadI32());

		var hostId = ReadI32();
		var mode = ReadI8();
		var winCondition = ReadI8();
		var teamType = ReadI8();
		var freeMods = ReadI8() == 1;

		var slotMods = Array.Empty<int>();
		if (freeMods)
		{
			slotMods = new int[16];
			for (var i = 0; i < 16; i++) slotMods[i] = ReadI32();
		}

		var seed = ReadI32();

		return new MatchState(
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
		var span = _buffer.Span;

		var time = BinaryPrimitives.ReadInt32LittleEndian(span);
		var id = span[4];
		var num300 = BinaryPrimitives.ReadUInt16LittleEndian(span[5..]);
		var num100 = BinaryPrimitives.ReadUInt16LittleEndian(span[7..]);
		var num50 = BinaryPrimitives.ReadUInt16LittleEndian(span[9..]);
		var numGeki = BinaryPrimitives.ReadUInt16LittleEndian(span[11..]);
		var numKatu = BinaryPrimitives.ReadUInt16LittleEndian(span[13..]);
		var numMiss = BinaryPrimitives.ReadUInt16LittleEndian(span[15..]);
		var totalScore = BinaryPrimitives.ReadInt32LittleEndian(span[17..]);
		var maxCombo = BinaryPrimitives.ReadUInt16LittleEndian(span[21..]);
		var currentCombo = BinaryPrimitives.ReadUInt16LittleEndian(span[23..]);
		var perfect = span[25] != 0;
		var currentHp = span[26];
		var tagByte = span[27];
		var scoreV2 = span[28] != 0;

		_buffer = _buffer[29..];

		double? comboPortion = null;
		double? bonusPortion = null;
		if (scoreV2)
		{
			comboPortion = ReadF64();
			bonusPortion = ReadF64();
		}

		return new ScoreFrame(
			time, id, num300, num100, num50, numGeki, numKatu, numMiss, totalScore, maxCombo,
			currentCombo, perfect, currentHp, tagByte, scoreV2, comboPortion, bonusPortion);
	}

	/// <summary>Reads a single replay frame from the buffer.</summary>
	/// <returns>The replay frame read.</returns>
	public ReplayFrame ReadReplayFrame()
	{
		return new ReplayFrame(
			(Keys)ReadU8(),
			(TaikoByte)ReadU8(),
			ReadF32(),
			ReadF32(),
			ReadI32());
	}

	/// <summary>Reads a complete replay frame bundle, including its frames, action, score frame, and sequence number.</summary>
	/// <returns>The replay frame bundle read.</returns>
	public SpectateFrameBundle ReadReplayFrameBundle()
	{
		var extra = ReadI32();
		var frameCount = ReadU16();
		var frames = new ReplayFrame[frameCount];
		for (var i = 0; i < frameCount; i++) frames[i] = ReadReplayFrame();

		var action = (ReplayAction)ReadU8();
		var scoreFrame = ReadScoreFrame();
		var sequence = ReadU16();

		return new SpectateFrameBundle(frames, scoreFrame, action, extra, sequence);
	}
}