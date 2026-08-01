using System.Buffers.Binary;
using System.Text;

namespace Basil.Protocol.Packets;

/// <summary>
///     Low-level binary primitives for the Bancho protocol: ULEB128 lengths, osu!-format strings,
///     32-bit integer lists, and the 7-byte packet header. All multi-byte integers are little-endian,
///     per the osu! protocol.
/// </summary>
public static class PacketWriter
{
	/// <summary>Encodes an integer as a ULEB128 variable-length byte sequence.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The ULEB128 bytes for the value.</returns>
	public static byte[] WriteUleb128(int value)
	{
		if (value == 0) return [0x00];

		var bytes = new List<byte>();
		var remaining = (uint)value;

		while (remaining != 0)
		{
			var b = (byte)(remaining & 0x7F);
			remaining >>= 7;
			if (remaining != 0) b |= 0x80;

			bytes.Add(b);
		}

		return [.. bytes];
	}

	/// <summary>Encodes a string in the osu! wire format: an existence byte, a ULEB128 length, then the UTF-8 bytes.</summary>
	/// <param name="value">The string to encode.</param>
	/// <returns>The encoded bytes, or a single <c>0x00</c> byte when the string is empty or <see langword="null" />.</returns>
	public static byte[] WriteString(string value)
	{
		if (string.IsNullOrEmpty(value)) return [0x00];

		var encoded = Encoding.UTF8.GetBytes(value);
		var length = WriteUleb128(encoded.Length);

		var result = new byte[1 + length.Length + encoded.Length];
		result[0] = 0x0B;
		length.CopyTo(result.AsSpan(1));
		encoded.CopyTo(result.AsSpan(1 + length.Length));
		return result;
	}

	/// <summary>Encodes a list of 32-bit integers with a 16-bit unsigned count prefix.</summary>
	/// <param name="values">The values to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteI32List(IReadOnlyList<int> values)
	{
		var result = new byte[2 + values.Count * 4];
		BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)values.Count);

		for (var i = 0; i < values.Count; i++)
			BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2 + i * 4), values[i]);

		return result;
	}

	/// <summary>Encodes a 32-bit integer as four little-endian bytes.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteInt32(int value)
	{
		var result = new byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(result, value);
		return result;
	}

	/// <summary>Encodes an unsigned 32-bit integer as four little-endian bytes.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteUInt32(uint value)
	{
		var result = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(result, value);
		return result;
	}

	/// <summary>Wraps a payload with the 7-byte Bancho packet header (id: u16, padding: u8, length: u32).</summary>
	/// <param name="packetId">The server packet id to place in the header.</param>
	/// <param name="payload">The payload to place after the header.</param>
	/// <returns>The complete packet bytes, header followed by payload.</returns>
	public static byte[] Wrap(ServerPackets packetId, ReadOnlySpan<byte> payload)
	{
		var result = new byte[7 + payload.Length];
		BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)packetId);
		// result[2] is padding, always 0
		BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(3), payload.Length);
		payload.CopyTo(result.AsSpan(7));
		return result;
	}
}