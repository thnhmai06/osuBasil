namespace Basil.Protocol.Stable.Wire.Packets;

public sealed record ClientPacket(ClientPacketType Type, byte[] Payload);