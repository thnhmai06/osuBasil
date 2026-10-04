using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Binary;

namespace Basil.Protocol.Bancho.Wire.Packets;

public partial class PacketWriter
{
	/// <summary>Writes a chat message payload: sender, text, and recipient as osu! strings, then the sender's ID as a 32-bit integer.</summary>
	/// <param name="sender">The sender's username.</param>
	/// <param name="text">The message body.</param>
	/// <param name="recipient">The receiving channel name or username.</param>
	/// <param name="senderId">The sender's user ID.</param>
	private void WriteMessagePayload(string sender, string text, string recipient, int senderId)
	{
		_writer.WriteOsuString(sender);
		_writer.WriteOsuString(text);
		_writer.WriteOsuString(recipient);
		_writer.Write(senderId);
	}

	/// <summary>Writes a channel payload: name and topic as osu! strings, then the player count as a 16-bit unsigned integer.</summary>
	/// <param name="name">The channel name.</param>
	/// <param name="topic">The channel topic.</param>
	/// <param name="playerCount">The number of users currently in the channel.</param>
	private void WriteChannelPayload(string name, string topic, int playerCount)
	{
		_writer.WriteOsuString(name);
		_writer.WriteOsuString(topic);
		_writer.Write((ushort)playerCount);
	}

	/// <summary>
	///     Writes a match payload describing a multiplayer room.
	/// </summary>
	/// <remarks>
	///     Layout, in order: the match ID as u16; an in-progress flag as u8; a room type byte
	///     (always <c>0</c>); the active mods as u32; the room name as an osu! string; a password
	///     field; the beatmap name, ID (i32), and MD5 as osu! string, i32, and osu! string; a
	///     status byte and a team byte for every slot; the player ID as u32 for every occupied
	///     slot; the host ID, mode, win condition, and team type each as u8; a free-mods flag as
	///     u8; per-slot mods as u32 only when free mods are enabled; and the random seed as u32.
	///     <para>
	///         The password field is a single <c>0x00</c> byte when the room has no password.
	///         Otherwise it is written as the real password osu! string when
	///         <paramref name="sendPassword" /> is <c>true</c>, or as the masked bytes
	///         <c>0x0B 0x00</c> (an empty osu! string with the existence byte) when hidden.
	///     </para>
	/// </remarks>
	/// <param name="room">The room state to serialize.</param>
	/// <param name="sendPassword">
	///     Whether the real password is included; <c>false</c> writes the masked form so clients
	///     see an empty password.
	/// </param>
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

	/// <summary>
	///     Writes an in-game score frame payload.
	/// </summary>
	/// <remarks>
	///     Layout, in order: the frame time as i32; the frame ID as u8; the 300, 100, 50, geki,
	///     katu, and miss hit counts each as u16; the total score as i32; the max and current
	///     combo each as u16; a perfect flag, the current HP, and the tag byte each as u8; and a
	///     ScoreV2 flag as u8. When ScoreV2 is set, the combo portion and bonus portion are
	///     appended as two doubles; otherwise the payload ends after the flag byte.
	/// </remarks>
	/// <param name="frame">The score frame to serialize.</param>
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