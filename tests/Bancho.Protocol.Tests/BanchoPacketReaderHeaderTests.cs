using Bancho.Protocol.Packets;
namespace Bancho.Protocol.Tests;

/// <summary>
/// Ported from app/packets.py's BanchoPacketReader._read_header + the packet_map-driven skip
/// behavior in __next__ ("packet type not handled, remove from internal buffer and continue").
/// </summary>
public class BanchoPacketReaderHeaderTests
{
    private static BanchoPacketReader Reader(string hex) => new(Convert.FromHexString(hex));

    [Fact]
    public void ReadHeader_PingWithNoPayload_ReturnsTypeAndZeroLength()
    {
        var reader = Reader("04000000000000");

        var (type, length) = reader.ReadHeader();

        Assert.Equal(ClientPackets.Ping, type);
        Assert.Equal(0, length);
        Assert.Equal(0, reader.RemainingLength);
    }

    [Fact]
    public void ReadHeader_WithPayload_LeavesPayloadUnconsumed()
    {
        var reader = Reader("5500000400000001000000");

        var (type, length) = reader.ReadHeader();

        Assert.Equal((ClientPackets)85, type);
        Assert.Equal(4, length);
        Assert.Equal(4, reader.RemainingLength);
        Assert.Equal(1, reader.ReadI32());
    }

    [Fact]
    public void SkipRaw_AdvancesPastUnhandledPayload()
    {
        var reader = Reader("5500000400000001000000" + "04000000000000"); // unknown-length payload then a PING header

        reader.ReadHeader(); // consume the first header (id=85, len=4)
        reader.SkipRaw(4); // skip its payload without parsing it

        var (nextType, nextLength) = reader.ReadHeader();
        Assert.Equal(ClientPackets.Ping, nextType);
        Assert.Equal(0, nextLength);
    }
}
