using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the UserStats packet (id 11) field encodings reached through
///     <c>PacketWriter.UserStats</c>: accuracy as a 32-bit float of accuracy / 100, and out-of-range
///     fields throwing.
/// </summary>
public class PacketWriterUserStatsTests
{
	private static byte[] Write(
		int action = 0, double accuracy = 0.0, int mode = 0, int pp = 0)
	{
		using var stream = new MemoryStream();
		new PacketWriter(stream).UserStats(0, action, "", "", 0, mode, 0, 0, accuracy, 0, 0, 0, pp);
		return stream.ToArray();
	}

	[Fact]
	public void UserStats_Accuracy_WritesFloatOfAccuracyOver100()
	{
		// Layout per the osu! UserStats packet: i32 userId, u8 action, osu! string info text,
		// osu! string map md5, u32 mods, u8 mode, i32 mapId, u64 ranked score, f32 accuracy/100,
		// u32 plays, u64 total score, i32 global rank, u16 pp. Accuracy 75 -> 0.75f = 0x3F400000.
		var expected = new List<byte>
		{
			0x0B, 0x00, 0x00, 0x2E, 0x00, 0x00, 0x00, // id 11 u16 LE, padding, length 46 u32 LE
			0, 0, 0, 0, // userId 0
			0x00, // action 0
			0x00, // info text ""
			0x00, // map md5 ""
			0, 0, 0, 0, // mods 0
			0x00, // mode 0
			0, 0, 0, 0, // mapId 0
			0, 0, 0, 0, 0, 0, 0, 0, // ranked score 0
			0x00, 0x00, 0x40, 0x3F, // 0.75f little-endian
			0, 0, 0, 0, // plays 0
			0, 0, 0, 0, 0, 0, 0, 0, // total score 0
			0, 0, 0, 0, // global rank 0
			0x00, 0x00 // pp 0
		};

		Assert.Equal(expected, Write(accuracy: 75));
	}

	[Fact]
	public void UserStats_PpAboveU16Maximum_ThrowsOverflow()
	{
		Assert.Throws<OverflowException>(() => Write(pp: 65536));
	}

	[Fact]
	public void UserStats_ActionAboveByteMaximum_ThrowsOverflow()
	{
		Assert.Throws<OverflowException>(() => Write(256));
	}

	[Fact]
	public void UserStats_ModeAboveByteMaximum_ThrowsOverflow()
	{
		Assert.Throws<OverflowException>(() => Write(mode: 256));
	}
}