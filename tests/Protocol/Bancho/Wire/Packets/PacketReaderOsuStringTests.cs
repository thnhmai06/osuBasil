using System.Text;
using Basil.Protocol.Bancho.Models.Chat;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the read side of the osu! string wire format (<c>ReadOsuString</c>, reached through
///     <c>PacketReader.ReadMessage</c>, whose payload is three osu! strings followed by a 32-bit sender
///     id): a null marker <c>0x00</c> or an empty <c>0x0B</c> string reads as <c>""</c>, the length is a
///     ULEB128 byte count, and malformed or truncated lengths are rejected.
/// </summary>
public class PacketReaderOsuStringTests
{
	/// <summary>
	///     Builds a ReadMessage payload per the osu! wire format: for each string the existence byte
	///     <c>0x0B</c>, its ULEB128 byte length, then the UTF-8 bytes; followed by the sender id as an
	///     i32 (little-endian).
	/// </summary>
	private static byte[] Message(string sender, string body, string recipient, int senderId = 0)
	{
		var bytes = new List<byte>
		{
			0x0B
		};
		Length(Encoding.UTF8.GetBytes(sender).Length, bytes);
		bytes.AddRange(Encoding.UTF8.GetBytes(sender));

		bytes.Add(0x0B);
		Length(Encoding.UTF8.GetBytes(body).Length, bytes);
		bytes.AddRange(Encoding.UTF8.GetBytes(body));

		bytes.Add(0x0B);
		Length(Encoding.UTF8.GetBytes(recipient).Length, bytes);
		bytes.AddRange(Encoding.UTF8.GetBytes(recipient));

		bytes.AddRange(
		[
			(byte)senderId,
			(byte)(senderId >> 8),
			(byte)(senderId >> 16),
			(byte)(senderId >> 24)
		]);
		return [.. bytes];

		static void Length(int length, List<byte> into)
		{
			// ULEB128 per the osu! string format: seven data bits, low group first, high bit as the
			// continuation flag.
			do
			{
				var b = (byte)(length & 0x7F);
				length >>= 7;
				if (length != 0) b |= 0x80;
				into.Add(b);
			} while (length != 0);
		}
	}

	[Fact]
	public void ReadMessage_MissingMarkerByte_YieldsEmptyStrings()
	{
		// three 0x00 existence bytes (each reads as "") followed by sender id 0.
		var message = new PacketReader(new MemoryStream(Convert.FromHexString("00000000000000"))).ReadMessage();

		Assert.Equal(new BanchoMessage("", "", "", 0), message);
	}

	[Fact]
	public void ReadMessage_ExistsMarkerWithZeroLength_YieldsEmptyStrings()
	{
		// three "0x0B + ULEB128 length 0" strings followed by sender id 0.
		var message = new PacketReader(new MemoryStream(Convert.FromHexString("0b000b000b0000000000"))).ReadMessage();

		Assert.Equal(new BanchoMessage("", "", "", 0), message);
	}

	[Theory]
	[InlineData(127)]
	[InlineData(128)]
	[InlineData(16383)]
	[InlineData(16384)]
	public void ReadMessage_StringOfDocumentedByteLength_RoundTrips(int byteLength)
	{
		var value = new string('a', byteLength);

		var message = new PacketReader(new MemoryStream(Message(value, value, value))).ReadMessage();

		Assert.Equal(new BanchoMessage(value, value, value, 0), message);
	}

	[Theory]
	[InlineData("héllo")]
	[InlineData("日本語")]
	public void ReadMessage_MultibyteString_RoundTrips(string value)
	{
		var message = new PacketReader(new MemoryStream(Message(value, value, value))).ReadMessage();

		Assert.Equal(new BanchoMessage(value, value, value, 0), message);
	}

	[Theory]
	[InlineData("0b")] // length byte itself missing
	[InlineData("0b0561")] // length 5, one UTF-8 byte present
	public void ReadMessage_StringTruncatedInsideItsBytes_ThrowsEndOfStream(string hex)
	{
		var reader = new PacketReader(new MemoryStream(Convert.FromHexString(hex)));

		Assert.Throws<EndOfStreamException>(() => reader.ReadMessage());
	}

	[Fact]
	public void ReadMessage_StringLengthNeedingSixUleb128Bytes_ThrowsInvalidData()
	{
		// five continuation bytes (0x80) with no terminator: a ULEB128 length that never ends.
		var reader = new PacketReader(new MemoryStream(Convert.FromHexString("0b8080808080")));

		Assert.Throws<InvalidDataException>(() => reader.ReadMessage());
	}

	[Fact]
	public void ReadMessage_StringLengthAboveDocumentedMaximum_ThrowsInvalidData()
	{
		// 0x0B, then ULEB128 0x81 0x80 0x40 0x00 = 64 * 16,384 + 1 = 1,048,577, one above MaxStringBytes.
		var reader = new PacketReader(new MemoryStream(Convert.FromHexString("0b81804000")));

		Assert.Throws<InvalidDataException>(() => reader.ReadMessage());
	}

	[Fact]
	public void ReadMessage_NegativeStringLength_ThrowsInvalidData()
	{
		// 0x0B, then ULEB128 bytes 0x80 0x80 0x80 0x80 0x08: a five-byte length whose final
		// seven data bits hold 8 shifted 28, i.e. 0x80000000 — negative when formed into an i32.
		var reader = new PacketReader(new MemoryStream(Convert.FromHexString("0b8080808008")));

		Assert.Throws<InvalidDataException>(() => reader.ReadMessage());
	}
}