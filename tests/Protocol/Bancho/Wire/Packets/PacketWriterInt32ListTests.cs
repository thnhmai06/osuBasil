using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies <c>WriteInt32List</c> (reached through <c>PacketWriter.FriendsList</c>, whose payload
///     is exactly one u16-prefixed i32 list): count as u16 little-endian followed by each value as i32
///     little-endian, and an <see cref="OverflowException" /> when the count leaves the u16 range.
/// </summary>
/// <remarks>
///     The read side of this list has no public surface in <see cref="PacketReader" />, so the list is
///     pinned by golden bytes instead of a round-trip.
/// </remarks>
public class PacketWriterInt32ListTests
{
	private static byte[] Write(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	[Fact]
	public void FriendsList_OneElement_WritesCountThenValue()
	{
		// packet id 72 (u16 LE), padding 0, payload length 6, payload: count u16 LE = 1, then
		// -2147483648 = 0x80000000 as i32 LE (00 00 00 80).
		Assert.Equal(
			Convert.FromHexString("48000006000000010000000080"),
			Write(writer => writer.FriendsList([-2147483648])));
	}

	[Fact]
	public void FriendsList_MaximumCount_WritesU16CountPrefixWithEveryValue()
	{
		// 65535 elements: the exact upper bound of the u16 count prefix. Elements are the literal
		// sequence 0..65534, written as i32 little-endian per the osu! list format.
		var friends = Enumerable.Range(0, 65535).ToList();

		var bytes = Write(writer => writer.FriendsList(friends));

		Assert.Equal(7 + 2 + (65535 * 4), bytes.Length);
		Assert.Equal(0xFF, bytes[7]); // count low byte
		Assert.Equal(0xFF, bytes[8]); // count high byte
		Assert.Equal([0x00, 0x00, 0x00, 0x00], bytes[9..13]); // first element, 0
		var last = friends[^1];
		Assert.Equal(new byte[] { (byte)last, (byte)(last >> 8), 0x00, 0x00 }, bytes[^4..]); // 65534
	}

	[Fact]
	public void FriendsList_EmptyList_WritesOnlyTheCountPrefix()
	{
		// packet id 72 (u16 LE), padding 0, payload length 2, payload: count u16 LE = 0.
		Assert.Equal(
			Convert.FromHexString("480000020000000000"),
			Write(writer => writer.FriendsList([])));
	}

	[Fact]
	public void FriendsList_CountAboveU16Maximum_ThrowsOverflow()
	{
		var friends = new int[65536];

		Assert.Throws<OverflowException>(() => Write(writer => writer.FriendsList(friends)));
	}
}