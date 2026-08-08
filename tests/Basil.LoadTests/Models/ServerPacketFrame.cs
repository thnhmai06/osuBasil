using Basil.Protocol.Packets;

namespace Basil.LoadTests.Models;

/// <summary>One decoded server-to-client bancho packet: its id and raw payload bytes.</summary>
/// <param name="Type">The packet type, as declared in the 7-byte header.</param>
/// <param name="Payload">The packet's payload bytes, exactly the declared length.</param>
public readonly record struct ServerPacketFrame(ServerPackets Type, byte[] Payload);
