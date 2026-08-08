using Basil.LoadTests.Models;
using Basil.Protocol.Packets;

namespace Basil.LoadTests.Client;

/// <summary>
///     Decodes a bancho response body into its individual packets, reusing
///     <see cref="PacketReader" /> for the header/payload framing exactly as the server's own
///     <c>PacketDispatcher</c> does for the opposite direction.
/// </summary>
public static class ServerPacketStream
{
	/// <summary>Splits a response body into its constituent packets.</summary>
	/// <param name="body">The raw response body bytes.</param>
	/// <returns>Every packet found, in order. Empty when the body is empty (nothing was queued).</returns>
	public static IReadOnlyList<ServerPacketFrame> ReadFrames(ReadOnlyMemory<byte> body)
	{
		var frames = new List<ServerPacketFrame>();
		var reader = new PacketReader(body);

		while (reader.RemainingLength > 0)
		{
			// PacketReader.ReadHeader() is typed to ClientPackets because the production reader
			// only ever decodes client->server traffic; the raw u16 id value is reinterpreted as
			// ServerPackets here, the read-side counterpart of the single cast in ClientPacketWriter.
			var (type, length) = reader.ReadHeader();
			var payload = reader.ReadRaw(length);
			frames.Add(new ServerPacketFrame((ServerPackets)(byte)type, payload));
		}

		return frames;
	}
}
