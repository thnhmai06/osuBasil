using Basil.Protocol.Stable.Models.Multiplayer;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Protocol.Stable.Tests.Wire.Packets;

/// <summary>
///     Verifies the match payload guards shared by <c>UpdateMatch</c>, <c>NewMatch</c> and
///     <c>MatchJoinSuccess</c>: exactly 16 slots required, and the room id (u16 on the wire) cannot
///     exceed 65535.
/// </summary>
public class PacketWriterMatchPayloadGuardTests
{
	private static RoomPacket Room(int slotCount, int id)
	{
		var slots = new List<RoomSlotPacket>();
		for (var i = 0; i < slotCount; i++) slots.Add(new RoomSlotPacket(1, 0, 0, null));

		return new RoomPacket(id, false, 0, "room", "", "map", 0, "md5", slots, 0, 0, 0, 0, false, 0);
	}

	private static byte[] Write(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	[Fact]
	public void Update15Slots_Throws()
	{
		Assert.Throws<ArgumentException>(() => Write(writer => writer.UpdateMatch(Room(15, 7))));
	}

	[Fact]
	public void New15Slots_Throws()
	{
		Assert.Throws<ArgumentException>(() => Write(writer => writer.NewMatch(Room(15, 7))));
	}

	[Fact]
	public void Join15Slots_Throws()
	{
		Assert.Throws<ArgumentException>(() => Write(writer => writer.MatchJoinSuccess(Room(15, 7))));
	}

	[Fact]
	public void UpdateId65536_Overflow()
	{
		Assert.Throws<OverflowException>(() => Write(writer => writer.UpdateMatch(Room(16, 65536))));
	}

	[Fact]
	public void NewId65536_Overflow()
	{
		Assert.Throws<OverflowException>(() => Write(writer => writer.NewMatch(Room(16, 65536))));
	}

	[Fact]
	public void JoinId65536_Overflow()
	{
		Assert.Throws<OverflowException>(() => Write(writer => writer.MatchJoinSuccess(Room(16, 65536))));
	}
}