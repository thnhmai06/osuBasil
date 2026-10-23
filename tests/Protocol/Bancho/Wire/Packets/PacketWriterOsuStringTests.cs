using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the write side of the osu! string wire format (<c>WriteOsuString</c>, reached through
///     <c>PacketWriter.MatchChangePassword</c>, whose payload is exactly one osu! string): existence
///     byte <c>0x0B</c>, ULEB128 byte length, UTF-8 bytes, and a single <c>0x00</c> for <c>null</c> and
///     <c>""</c>.
/// </summary>
public class PacketWriterOsuStringTests
{
	private static byte[] Write(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	[Theory]
	[InlineData(127, "0b7f", 129)] // ULEB128 length 127 = 1 byte 0x7f (no continuation)
	[InlineData(128, "0b8001", 131)] // 128 = 2 bytes (0x80 with continuation, 0x01)
	[InlineData(16383, "0bff7f", 16386)] // 16383 = 2 bytes
	[InlineData(16384, "0b808001", 16388)] // 16384 = 3 bytes
	public void WriteOsuString_StringOfDocumentedByteLength_WritesUleb128LengthPrefix(
		int byteLength, string ulebHex, int payloadLength)
	{
		// packet id 91 (MatchChangePassword, u16 little-endian), padding byte 0, payload length u32
		// little-endian, payload: 0x0B, the ULEB128 length, then byteLength UTF-8 'a' bytes.
		var expected = new List<byte>
		{
			0x5B,
			0x00,
			0x00,
			(byte)payloadLength,
			(byte)(payloadLength >> 8),
			(byte)(payloadLength >> 16),
			(byte)(payloadLength >> 24)
		};
		expected.AddRange(Convert.FromHexString(ulebHex));
		expected.AddRange(Enumerable.Repeat((byte)'a', byteLength));

		var actual = Write(writer => writer.MatchChangePassword(new string('a', byteLength)));

		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData("héllo", "5b0000080000000b0668c3a96c6c6f")] // 6 UTF-8 bytes, not 5 characters
	[InlineData("日本語", "5b00000b0000000b09e697a5e69cace8aa9e")] // 9 UTF-8 bytes, not 3 characters
	public void WriteOsuString_MultibyteString_LengthCountsBytesNotCharacters(string value, string expectedHex)
	{
		Assert.Equal(Convert.FromHexString(expectedHex), Write(writer => writer.MatchChangePassword(value)));
	}

	[Fact]
	public void WriteOsuString_NullString_WritesOnlyTheMarkerByte()
	{
		// WriteOsuString documents that a null string is written as the single 0x00 existence byte,
		// exactly like "" — same bytes as writer.MatchChangePassword("").
		string? password = null;

		Assert.Equal(Convert.FromHexString("5b00000100000000"), Write(writer => writer.MatchChangePassword(password!)));
	}
}