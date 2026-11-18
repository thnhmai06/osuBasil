using System.Text;

namespace Basil.Testing.Clients;

public sealed record ServerPacket(int Id, byte[] Payload)
{
	public static IReadOnlyList<ServerPacket> Split(byte[] body)
	{
		var packets = new List<ServerPacket>();
		var position = 0;
		while (position + 7 <= body.Length)
		{
			var id = BitConverter.ToUInt16(body, position);
			var length = BitConverter.ToInt32(body, position + 3);
			packets.Add(new ServerPacket(id, body[(position + 7)..(position + 7 + length)]));
			position += 7 + length;
		}

		return packets;
	}

	public int AsInt()
	{
		return BitConverter.ToInt32(Payload, 0);
	}

	public IReadOnlyList<string> Strings(int count)
	{
		var strings = new List<string>();
		var position = 0;
		for (var index = 0; index < count; index++) strings.Add(ReadString(ref position));
		return strings;
	}

	public string ReadString(ref int position)
	{
		if (Payload[position++] == 0) return string.Empty;
		var length = 0;
		var shift = 0;
		byte current;
		do
		{
			current = Payload[position++];
			length |= (current & 0x7f) << shift;
			shift += 7;
		} while ((current & 0x80) != 0);

		var text = Encoding.UTF8.GetString(Payload, position, length);
		position += length;
		return text;
	}
}