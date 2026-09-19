using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Binary;

namespace Basil.Protocol.Bancho.Wire.Packets;

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
		_writer.Write((ushort)playerCount);
	}

	private void WriteMatchPayload(RoomPacket room, bool sendPassword)
	{
		_writer.Write((ushort)room.Id);
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

		foreach (var slot in room.Slots) _writer.Write((byte)slot.Status);
		foreach (var slot in room.Slots) _writer.Write((byte)slot.Team);

		foreach (var slot in room.Slots)
			if (slot.HasPlayer)
				_writer.Write((uint)slot.PlayerId!.Value);

		_writer.Write((uint)room.HostId);
		_writer.Write((byte)room.Mode);
		_writer.Write((byte)room.WinCondition);
		_writer.Write((byte)room.TeamType);
		_writer.Write(room.FreeMods ? (byte)1 : (byte)0);

		if (room.FreeMods)
			foreach (var slot in room.Slots)
				_writer.Write((uint)slot.Mods);

		_writer.Write((uint)room.Seed);
	}

	private void WriteScoreFramePayload(ScoreFrame frame)
	{
		_writer.Write(frame.Time);
		_writer.Write((byte)frame.Id);
		_writer.Write((ushort)frame.Num300);
		_writer.Write((ushort)frame.Num100);
		_writer.Write((ushort)frame.Num50);
		_writer.Write((ushort)frame.NumGeki);
		_writer.Write((ushort)frame.NumKatu);
		_writer.Write((ushort)frame.NumMiss);
		_writer.Write(frame.TotalScore);
		_writer.Write((ushort)frame.MaxCombo);
		_writer.Write((ushort)frame.CurrentCombo);
		_writer.Write(frame.Perfect ? (byte)1 : (byte)0);
		_writer.Write((byte)frame.CurrentHp);
		_writer.Write((byte)frame.TagByte);
		_writer.Write(frame.ScoreV2 ? (byte)1 : (byte)0);

		if (!frame.ScoreV2) return;

		_writer.Write(frame.ComboPortion ?? 0.0);
		_writer.Write(frame.BonusPortion ?? 0.0);
	}
}