using System.Buffers.Binary;

namespace Basil.Protocol.Packets;

/// <summary>
///     Low-level binary primitives for the Bancho protocol: ULEB128 lengths, osu!-format strings,
///     32-bit integer lists, and the 7-byte packet header. All multibyte integers are little-endian,
///     per the osu! protocol.
/// </summary>
public static class PacketWriter
{
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