namespace Basil.Protocol.Game.Stable.Wire.Packets;

public sealed record ClientPacket(ClientPacketType Type, byte[] Payload);