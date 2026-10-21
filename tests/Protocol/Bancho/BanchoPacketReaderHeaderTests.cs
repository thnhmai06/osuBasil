using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests;

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
}
