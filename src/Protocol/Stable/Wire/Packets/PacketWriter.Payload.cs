using Basil.Domain.Spectating;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Wire.Binary;

namespace Basil.Protocol.Game.Stable.Wire.Packets;

public partial class PacketWriter
{
	private void WriteMessagePayload(string sender, string text, string recipient, int senderId)
	{
		_writer.WriteOsuString(sender);
		_writer.WriteOsuString(text);
		_writer.WriteOsuString(recipient);
		_writer.Write(senderId);
	}

	private void WriteChannelPayload(string name, string topic, int playerCount)
	{
		_writer.WriteOsuString(name);
		_writer.WriteOsuString(topic);
		_writer.Write(checked((ushort)playerCount));
	}

	private void WriteMatchPayload(RoomPacket room, bool sendPassword)
	{
		if (room.Slots.Count != SlotCount)
			throw new ArgumentException($"A match must have exactly {SlotCount} slots.", nameof(room));

		_writer.Write(checked((ushort)room.Id));
		_writer.Write(room.InProgress ? (byte)1 : (byte)0);
		_writer.Write((byte)0); // room type, always 0
		_writer.Write((uint)room.Mods);

		_writer.WriteOsuString(room.Name);

		if (!string.IsNullOrEmpty(room.Password))
		{
			if (sendPassword)
				_writer.WriteOsuString(room.Password);
			else
				_writer.Write([0x0B, 0x00]);
		}
		else
		{
			_writer.Write((byte)0x00);
		}

		_writer.WriteOsuString(room.MapName);
		_writer.Write(room.MapId);
		_writer.WriteOsuString(room.MapMd5);

		foreach (var slot in room.Slots) _writer.Write(checked((byte)slot.Status));
		foreach (var slot in room.Slots) _writer.Write(checked((byte)slot.Team));

		foreach (var slot in room.Slots.Where(slot => slot.HasPlayer))
			_writer.Write((uint)slot.PlayerId!.Value);

		_writer.Write((uint)room.HostId);
		_writer.Write(checked((byte)room.Mode));
		_writer.Write(checked((byte)room.WinCondition));
		_writer.Write(checked((byte)room.TeamType));
		_writer.Write(room.FreeMods ? (byte)1 : (byte)0);

		if (room.FreeMods)
			foreach (var slot in room.Slots)
				_writer.Write((uint)slot.Mods);

		_writer.Write((uint)room.Seed);
	}

	private void WriteScoreFramePayload(ScoreFrame frame)
	{
		_writer.Write(checked((int)frame.Time.TotalMilliseconds));
		_writer.Write(checked((byte)frame.Id));
		_writer.Write(checked((ushort)frame.State.HitCounts.Num300));
		_writer.Write(checked((ushort)frame.State.HitCounts.Num100));
		_writer.Write(checked((ushort)frame.State.HitCounts.Num50));
		_writer.Write(checked((ushort)frame.State.HitCounts.NumGeki));
		_writer.Write(checked((ushort)frame.State.HitCounts.NumKatu));
		_writer.Write(checked((ushort)frame.State.HitCounts.NumMiss));
		_writer.Write(frame.State.TotalScore);
		_writer.Write(frame.State.MaxCombo);
		_writer.Write(frame.State.CurrentCombo);
		_writer.Write(frame.State.IsFullCombo ? (byte)1 : (byte)0);
		_writer.Write(checked((byte)frame.Health));
		_writer.Write(checked((byte)frame.TagByte));
		_writer.Write(frame.Sv2Portion is null ? (byte)0 : (byte)1);

		if (frame.Sv2Portion is not { } portion) return;

		_writer.Write(portion.Combo);
		_writer.Write(portion.Bonus);
	}
}