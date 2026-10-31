using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies `PacketReader.ReadHeader` parses the type/length header and leaves the payload
///     unconsumed.
/// </summary>
public class BanchoPacketReaderHeaderTests
{
	private static (PacketReader Reader, MemoryStream Stream) Reader(string hex)
	{
		var stream = new MemoryStream(Convert.FromHexString(hex));
		return (new PacketReader(stream), stream);
	}

	[Fact]
	public void ReadHeader_PingWithNoPayload_ReturnsTypeAndZeroLength()
	{
		var (reader, stream) = Reader("04000000000000");

		var (type, length) = reader.ReadHeader();

		Assert.Equal(ClientPacketType.Ping, type);
		Assert.Equal(0, length);
		Assert.Equal(7, stream.Position);
	}

	[Fact]
	public void ReadHeader_WithPayload_LeavesPayloadUnconsumed()
	{
		var (reader, stream) = Reader("5500000400000001000000");

		var (type, length) = reader.ReadHeader();

		Assert.Equal((ClientPacketType)85, type);
		Assert.Equal(4, length);
		Assert.Equal(7, stream.Position);
		Assert.Equal(1, new BinaryReader(stream).ReadInt32());
	}

	[Fact]
	public void ReadHeader_Id256_IsNotAliasedToId0()
	{
		// id 256 as u16 little-endian: bytes 00 01. A reader that truncates the id to one
		// byte would alias 256 to 0.
		var (reader, _) = Reader("0001000000000000");

		var (type, length) = reader.ReadHeader();

		Assert.Equal(256, (int)type);
		Assert.Equal(0, length);
	}

	[Theory]
	[InlineData("00")] // 1 byte
	[InlineData("000000000000")] // 6 bytes, one short of the 7-byte header
	public void ReadHeader_ShorterThanSevenBytes_ThrowsEndOfStream(string hex)
	{
		var reader = new PacketReader(new MemoryStream(Convert.FromHexString(hex)));

		Assert.Throws<EndOfStreamException>(() => reader.ReadHeader());
	}

	[Fact]
	public void ReadHeader_NegativePayloadLength_ThrowsInvalidData()
	{
		// id 4, padding 0, length 0xffffffff (little-endian -1).
		var reader = new PacketReader(new MemoryStream(Convert.FromHexString("040000ffffffff")));

		Assert.Throws<InvalidDataException>(() => reader.ReadHeader());
	}
}