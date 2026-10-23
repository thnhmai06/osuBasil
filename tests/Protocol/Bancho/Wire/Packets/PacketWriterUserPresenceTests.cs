using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the UserPresence packet (id 83) field encodings reached through
///     <c>PacketWriter.UserPresence</c>: the UTC offset is written plus 24, the game mode occupies the
///     high bits of the privilege byte, and out-of-range fields throw.
/// </summary>
public class PacketWriterUserPresenceTests
{
	/// <summary>
	///     Expected bytes for <c>UserPresence(userId: 0, name: "", ..., long/lat: 0, rank: 0)</c> with the
	///     given offset byte and privilege byte, hand-laid-out per the osu! UserPresence layout: i32 id,
	///     osu! string name, u8 offset+24, u8 country, u8 privileges|mode&lt;&lt;5, f32 longitude, f32 latitude,
	///     i32 rank (payload of 20 bytes).
	/// </summary>
	private static byte[] Expected(byte offsetByte, byte privilegeByte)
	{
		return new byte[]
		{
			0x53, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, // id 83 u16 LE, padding, length 20 u32 LE
			0, 0, 0, 0, // userId 0
			0x00, // name ""
			offsetByte, // UTC offset + 24
			0x00, // country code 0
			privilegeByte, // privileges | mode << 5
			0, 0, 0, 0, // longitude 0.0f
			0, 0, 0, 0, // latitude 0.0f
			0, 0, 0, 0 // global rank 0
		};
	}

	private static byte[] Write(int utcOffset, int banchoPrivileges, int mode)
	{
		using var stream = new MemoryStream();
		new PacketWriter(stream).UserPresence(0, "", utcOffset, 0, banchoPrivileges, mode, 0.0, 0.0, 0);
		return stream.ToArray();
	}

	[Theory]
	[InlineData(-12, 0x0C)] // -12 + 24 = 12; the wire byte is always non-negative
	[InlineData(14, 0x26)] // 14 + 24 = 38
	public void UserPresence_UtcOffset_WritesOffsetPlus24(int utcOffset, byte expectedByte)
	{
		Assert.Equal(Expected(expectedByte, 0x00), Write(utcOffset, 0, 0));
	}

	[Theory]
	[InlineData(1, 0, 0x01)] // privileges only occupy the low bits
	[InlineData(0, 7, 0xE0)] // mode 7 << 5 = 224
	[InlineData(12, 3, 0x6C)] // 12 | (3 << 5) = 108
	[InlineData(31, 7, 0xFF)] // both fields fill the byte together
	public void UserPresence_Mode_SitsInTheHighBitsOfThePrivilegeByte(
		int banchoPrivileges, int mode, byte expectedByte)
	{
		Assert.Equal(Expected(0x18, expectedByte), Write(0, banchoPrivileges, mode));
	}

	[Fact]
	public void UserPresence_UtcOffsetOutsideByteRange_ThrowsOverflow()
	{
		// -12 + 24 to 38 fit the byte; 250 + 24 = 274 does not.
		Assert.Throws<OverflowException>(() => Write(250, 0, 0));
	}

	[Fact]
	public void UserPresence_ModeAboveFiveBits_ThrowsOverflow()
	{
		// mode 8 << 5 = 256 does not fit the privilege byte.
		Assert.Throws<OverflowException>(() => Write(0, 0, 8));
	}
}