namespace Basil.Protocol.Bancho.Wire.Packets;

/// <summary>One packet of a client request: its type and exactly the payload bytes its header declared.</summary>
/// <param name="Type">The packet type.</param>
/// <param name="Payload">The payload, never longer than the declared length.</param>
public sealed record ClientPacket(ClientPacketType Type, byte[] Payload);
