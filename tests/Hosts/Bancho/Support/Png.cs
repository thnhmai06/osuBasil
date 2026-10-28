namespace Basil.Host.Bancho.Tests.Support;

/// <summary>Builds a PNG that has the given size in its header and a body the server never decodes.</summary>
internal static class Png
{
	public static byte[] WithSize(int width, int height)
	{
		var header = new List<byte>();
		header.AddRange(BigEndian(width));
		header.AddRange(BigEndian(height));
		header.AddRange(new byte[] { 8, 2, 0, 0, 0 });
		return
		[
			0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
			.. Chunk("IHDR", header.ToArray()),
			.. Chunk("IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]),
			.. Chunk("IEND", [])
		];
	}

	private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

	private static byte[] Chunk(string type, byte[] data)
	{
		var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
		return [.. BigEndian(data.Length), .. body, .. BigEndian((int)Crc32(body))];
	}

	private static uint Crc32(byte[] bytes)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var b in bytes)
		{
			crc ^= b;
			for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
		}

		return ~crc;
	}
}
