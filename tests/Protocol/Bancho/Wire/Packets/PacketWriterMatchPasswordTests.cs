using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the password field of the match payload: an empty password is written as the
///     single <c>0x00</c> marker byte (the "no password" form), not an osu! string and not the
///     masked osu!-string form. The bytes ride on the match payload laid out in
///     <c>ServerPacketWriterTests</c>, with the password field replaced by the marker byte.
/// </summary>
public class PacketWriterMatchPasswordTests
{
	private static RoomPacket Room()
	{
		var slots = new List<RoomSlotPacket>
		{
			new(4, 1, 64, 1001),
			new(8, 2, 16, 32)
		};
		for (var i = 0; i < 14; i++) slots.Add(new RoomSlotPacket(1, 0, 0, null));

		return new RoomPacket(
			7,
			true,
			64,
			"party",
			"",
			"Camellia - Exit This Earth's Atomosphere",
			33852,
			"60b725f10c9c85c70d97880dfe8191b3",
			slots,
			32,
			0,
			2,
			2,
			true,
			123_456);
	}

	private static byte[] Write(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	[Fact]
	public void UpdateMatch_RoomWithEmptyPassword_WritesASingleExistenceByte()
	{
		// packet id 26 (UpdateMatch, u16 LE), padding 0, payload length 0xd4 — the golden match
		// payload of ServerPacketWriterTests.UpdateMatch_SendPasswordTrue (0xdb) minus the seven
		// bytes of the masked osu! string, with the single 0x00 marker byte in its place.
		Assert.Equal(
			Convert.FromHexString(
				"1a0000d400000007000100400000000b057061727479000b2843616d656c6c6961202d2045786974205468697320456172746827732041746f6d6f7370686572653c8400000b2036306237323566313063396338356337306439373838306466653831393162330408010101010101010101010101010101020000000000000000000000000000e90300002000000020000000000202014000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000040e20100"),
			Write(writer => writer.UpdateMatch(Room())));
	}
}