using System.Text;

namespace Basil.Testing.Clients;

public sealed class ClientPacketWriter
{
	private readonly MemoryStream _bytes = new();

	public byte[] ToArray()
	{
		return _bytes.ToArray();
	}

	public ClientPacketWriter Packet(int id, byte[]? payload = null)
	{
		payload ??= [];
		_bytes.Write(BitConverter.GetBytes((ushort)id));
		_bytes.WriteByte(0);
		_bytes.Write(BitConverter.GetBytes(payload.Length));
		_bytes.Write(payload);
		return this;
	}

	public ClientPacketWriter Int(int id, int value)
	{
		return Packet(id, BitConverter.GetBytes(value));
	}

	public ClientPacketWriter Text(int id, string value)
	{
		return Packet(id, OsuString(value));
	}

	public ClientPacketWriter IntList(int id, IEnumerable<int> values)
	{
		var list = values.ToList();
		return Packet(id, [.. BitConverter.GetBytes((ushort)list.Count), .. list.SelectMany(BitConverter.GetBytes)]);
	}

	public static byte[] OsuString(string value)
	{
		if (value.Length == 0) return [0];
		var utf8 = Encoding.UTF8.GetBytes(value);
		var length = new List<byte>();
		var rest = utf8.Length;
		do
		{
			var chunk = (byte)(rest & 0x7f);
			rest >>= 7;
			length.Add(rest > 0 ? (byte)(chunk | 0x80) : chunk);
		} while (rest > 0);

		return [0x0b, .. length, .. utf8];
	}

	public static byte[] Message(string text, string recipient)
	{
		return [.. OsuString(string.Empty), .. OsuString(text), .. OsuString(recipient), .. BitConverter.GetBytes(0)];
	}

	public static byte[] JoinMatch(int matchId, string password)
	{
		return [.. BitConverter.GetBytes(matchId), .. OsuString(password)];
	}

	public static byte[] ScoreFrame(int time, int totalScore, int combo)
	{
		var bytes = new List<byte>();
		bytes.AddRange(BitConverter.GetBytes(time));
		bytes.Add(0); // slot id, set by the server
		for (var index = 0; index < 6; index++) bytes.AddRange(BitConverter.GetBytes((ushort)(index == 0 ? 10 : 0)));
		bytes.AddRange(BitConverter.GetBytes(totalScore));
		bytes.AddRange(BitConverter.GetBytes((ushort)combo));
		bytes.AddRange(BitConverter.GetBytes((ushort)combo));
		bytes.AddRange("\0d\0\0"u8.ToArray()); // perfect, hp, tag byte, score v2
		return [.. bytes];
	}

	public static byte[] FrameBundle(int action, int sequence, byte[] scoreFrame)
	{
		return
		[
			.. BitConverter.GetBytes(0), .. BitConverter.GetBytes((ushort)0), (byte)action, .. scoreFrame,
			.. BitConverter.GetBytes((ushort)sequence)
		];
	}

	public static byte[] Match(string name, string password, int mode = 0, int mods = 0, string mapMd5 = "",
		int mapId = 0, string mapName = "", bool freeMods = false, int teamType = 0, int winCondition = 0)
	{
		var bytes = new List<byte>();
		bytes.AddRange(BitConverter.GetBytes((ushort)0)); // id
		bytes.Add(0); // in progress
		bytes.Add(0); // powerplay
		bytes.AddRange(BitConverter.GetBytes(mods));
		bytes.AddRange(OsuString(name));
		bytes.AddRange(OsuString(password));
		bytes.AddRange(OsuString(mapName));
		bytes.AddRange(BitConverter.GetBytes(mapId));
		bytes.AddRange(OsuString(mapMd5));
		for (var index = 0; index < 16; index++) bytes.Add(1); // slot statuses: open
		for (var index = 0; index < 16; index++) bytes.Add(0); // slot teams
		bytes.AddRange(BitConverter.GetBytes(0)); // host id
		bytes.Add((byte)mode);
		bytes.Add((byte)winCondition);
		bytes.Add((byte)teamType);
		bytes.Add(freeMods ? (byte)1 : (byte)0);
		bytes.AddRange(BitConverter.GetBytes(0)); // seed
		return [.. bytes];
	}
}