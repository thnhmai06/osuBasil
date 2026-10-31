using Basil.Protocol.Bancho.Models.Beatmaps;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the server packets with ids 10, 45, 62 and 69 and <c>PacketWriter.Build</c> against hand-written
///     bytes (header: u16 id, padding, u32 length; strings: 0x0B, ULEB128 length, UTF-8).
/// </summary>
public class PacketWriterParityPacketsTests
{
	private static byte[] Hex(string hex)
	{
		return Convert.FromHexString(hex);
	}

	[Fact]
	public void Build_OnePacket_ReturnsItsBytes()
	{
		Assert.Equal(Hex("08000000000000"), PacketWriter.Build(w => w.Pong()));
	}

	[Fact]
	public void Build_TwoPackets_ReturnsThemInOrder()
	{
		Assert.Equal(Hex("08000000000000" + "13000000000000"), PacketWriter.Build(w =>
		{
			w.Pong();
			w.VersionUpdate();
		}));
	}

	[Fact]
	public void HandleIrcQuit_WritesName()
	{
		Assert.Equal(Hex("0a000003000000" + "0b0161"), PacketWriter.Build(w => w.HandleIrcQuit("a")));
	}

	[Fact]
	public void AllPlayersLoaded_WritesEmptyPacket()
	{
		Assert.Equal(Hex("2d000000000000"), PacketWriter.Build(w => w.AllPlayersLoaded()));
	}

	[Fact]
	public void Unauthorized_WritesEmptyPacket()
	{
		Assert.Equal(Hex("3e000000000000"), PacketWriter.Build(w => w.Unauthorized()));
	}

	[Fact]
	public void BeatmapInfoReply_NoMaps_WritesZeroCount()
	{
		Assert.Equal(Hex("45000004000000" + "00000000"), PacketWriter.Build(w => w.BeatmapInfoReply([])));
	}

	[Fact]
	public void BeatmapInfoReply_OneMap_WritesFieldsInOrder()
	{
		// i32 count, then i16 index, i32 map id, i32 set id, i32 thread id, u8 status, i8 osu, catch, taiko, mania, string md5
		var map = new BeatmapInfoReplyEntry(1, 2, 3, 4, 5, 6, 7, 8, -1, "ab");

		Assert.Equal(
			Hex("4500001b000000" + "01000000" + "0100" + "02000000" + "03000000" + "04000000" + "05" + "060708ff" +
			    "0b026162"),
			PacketWriter.Build(w => w.BeatmapInfoReply([map])));
	}
}