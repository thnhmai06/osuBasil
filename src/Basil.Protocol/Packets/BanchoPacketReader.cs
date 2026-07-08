using System.Buffers.Binary;
using System.Text;
using Basil.Protocol.Multiplayer;

namespace Basil.Protocol.Packets;

/// <summary>
///     Reads Bancho packet primitives from a client request body. Ported from
///     app/packets.py's BanchoPacketReader (read-side only; the packet_map-driven
///     iteration/dispatch loop is wired up in Basil.Web during Phase 3, once handlers exist).
///     All multi-byte integers are little-endian, per the osu! protocol.
/// </summary>
public sealed class BanchoPacketReader(ReadOnlyMemory<byte> buffer)
{
    private ReadOnlyMemory<byte> _buffer = buffer;

    public int RemainingLength => _buffer.Length;

    public byte[] ReadRaw(int length)
    {
        var value = _buffer[..length].ToArray();
        _buffer = _buffer[length..];
        return value;
    }

    /// <summary>Advances past `length` bytes without allocating/copying — for skipping unhandled packet payloads.</summary>
    public void SkipRaw(int length)
    {
        _buffer = _buffer[length..];
    }

    /// <summary>Reads the 7-byte Bancho packet header (id: u16, padding: u8, length: u32). Ported from _read_header.</summary>
    public (ClientPackets Type, int Length) ReadHeader()
    {
        var type = (ClientPackets)BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span);
        var length = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span[3..]);
        _buffer = _buffer[7..];
        return (type, length);
    }

    // integral types

    public int ReadI8()
    {
        var value = _buffer.Span[0];
        _buffer = _buffer[1..];
        return unchecked((sbyte)value);
    }

    public int ReadU8()
    {
        var value = _buffer.Span[0];
        _buffer = _buffer[1..];
        return value;
    }

    public int ReadI16()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Span);
        _buffer = _buffer[2..];
        return value;
    }

    public int ReadU16()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span);
        _buffer = _buffer[2..];
        return value;
    }

    public int ReadI32()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span);
        _buffer = _buffer[4..];
        return value;
    }

    public uint ReadU32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Span);
        _buffer = _buffer[4..];
        return value;
    }

    public long ReadI64()
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Span);
        _buffer = _buffer[8..];
        return value;
    }

    public ulong ReadU64()
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Span);
        _buffer = _buffer[8..];
        return value;
    }

    // floating-point types

    public float ReadF32()
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Span);
        _buffer = _buffer[4..];
        return value;
    }

    public double ReadF64()
    {
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.Span);
        _buffer = _buffer[8..];
        return value;
    }

    // complex types

    // XXX: some osu! packets use i16 for array length, while others use i32
    public IReadOnlyList<int> ReadI32ListI16L()
    {
        var length = ReadU16();
        var values = new int[length];
        for (var i = 0; i < length; i++) values[i] = ReadI32();

        return values;
    }

    public IReadOnlyList<int> ReadI32ListI32L()
    {
        var length = (int)ReadU32();
        var values = new int[length];
        for (var i = 0; i < length; i++) values[i] = ReadI32();

        return values;
    }

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

    public BanchoMessage ReadMessage()
    {
        return new BanchoMessage(
            ReadString(),
            ReadString(),
            ReadString(),
            ReadI32());
    }

    public ReadMatchResult ReadMatch()
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

        return new ReadMatchResult(
            id, inProgress, powerplay, mods, name, password, mapName, mapId, mapMd5,
            slotStatuses, slotTeams, slotIds, hostId, mode, winCondition, teamType, freeMods,
            slotMods, seed);
    }

    public ScoreFrameData ReadScoreFrame()
    {
        // matches SCOREFRAME_FMT = struct.Struct("<iBHHHHHHiHH?BB?") — 29 bytes.
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

        return new ScoreFrameData(
            time, id, num300, num100, num50, numGeki, numKatu, numMiss, totalScore, maxCombo,
            currentCombo, perfect, currentHp, tagByte, scoreV2, comboPortion, bonusPortion);
    }

    public ReplayFrameData ReadReplayFrame()
    {
        return new ReplayFrameData(
            (Keys)ReadU8(),
            ReadU8(),
            ReadF32(),
            ReadF32(),
            ReadI32());
    }
}