using System.Buffers.Binary;
using System.Text;

namespace Basil.Protocol.Binary;

/// <summary>Provides low-level binary encoding primitives for the Bancho protocol.</summary>
public static class BinaryWriter
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

	/// <summary>Encodes a byte as a single 8-bit value.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded byte.</returns>
	public static byte[] WriteByte(byte value)
	{
		return [value];
	}

	/// <summary>Encodes a 16-bit integer as two little-endian bytes.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteInt16(short value)
	{
		var result = new byte[2];
		BinaryPrimitives.WriteInt16LittleEndian(result, value);
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

	/// <summary>Encodes a 64-bit integer as eight little-endian bytes.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteInt64(long value)
	{
		var result = new byte[8];
		BinaryPrimitives.WriteInt64LittleEndian(result, value);
		return result;
	}

	/// <summary>Encodes a double-precision floating-point value as eight little-endian bytes.</summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteDouble(double value)
	{
		var result = new byte[8];
		BinaryPrimitives.WriteDoubleLittleEndian(result, value);
		return result;
	}

	/// <summary>Encodes a byte array as an Int32 little-endian length prefix followed by the raw bytes.</summary>
	/// <param name="value">The bytes to encode.</param>
	/// <returns>The encoded bytes.</returns>
	public static byte[] WriteByteArray(byte[] value)
	{
		var result = new byte[4 + value.Length];
		BinaryPrimitives.WriteInt32LittleEndian(result, value.Length);
		value.CopyTo(result.AsSpan(4));
		return result;
	}
}