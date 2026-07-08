using Basil.Protocol.Packets;

namespace Basil.Protocol.Tests;

/// <summary>
///     Ported from the write-side helpers exercised inline in bancho.py's tests/unit/packets_test.py
///     (write_string, write_i32_list are used there to build expected payloads for composite packets).
/// </summary>
public class PacketWriterTests
{
    [Fact]
    public void WriteUleb128_Zero_ReturnsSingleZeroByte()
    {
        Assert.Equal(new byte[] { 0x00 }, PacketWriter.WriteUleb128(0));
    }

    [Fact]
    public void WriteUleb128_SmallValue_EncodesDirectly()
    {
        Assert.Equal(new byte[] { 0x05 }, PacketWriter.WriteUleb128(5));
    }

    [Fact]
    public void WriteUleb128_ValueAbove127_UsesContinuationBit()
    {
        // 300 = 0b1_0010_1100 -> low 7 bits: 0101100 (0x2C) with continuation, then 0b10 (0x02)
        Assert.Equal(new byte[] { 0xAC, 0x02 }, PacketWriter.WriteUleb128(300));
    }

    [Fact]
    public void WriteString_EmptyString_ReturnsSingleZeroByte()
    {
        Assert.Equal(new byte[] { 0x00 }, PacketWriter.WriteString(""));
    }

    [Fact]
    public void WriteString_NonEmpty_PrefixesExistsMarkerAndUleb128Length()
    {
        Assert.Equal(Convert.FromHexString("0b05636d797569"), PacketWriter.WriteString("cmyui"));
    }

    [Fact]
    public void WriteI32List_Empty_ReturnsZeroLengthPrefix()
    {
        Assert.Equal(Convert.FromHexString("0000"), PacketWriter.WriteI32List([]));
    }

    [Fact]
    public void WriteI32List_NonEmpty_PrefixesU16LengthThenEachAsI32LittleEndian()
    {
        Assert.Equal(
            Convert.FromHexString("03000100000004000000e9030000"),
            PacketWriter.WriteI32List([1, 4, 1001]));
    }

    [Fact]
    public void Wrap_AssemblesHeaderThenPayload()
    {
        var payload = PacketWriter.WriteInt32(0);

        Assert.Equal(
            Convert.FromHexString("0500000400000000000000"),
            PacketWriter.Wrap(ServerPackets.UserId, payload));
    }
}