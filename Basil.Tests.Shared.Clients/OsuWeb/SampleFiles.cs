namespace Basil.Tests.Shared.Clients.OsuWeb;

/// <summary>Small files the clients upload: a picture and a replay.</summary>
public static class SampleFiles
{
	private const int MinimumBytes = 24;

	/// <summary>A one-pixel PNG picture, accepted by the screenshot and avatar uploads.</summary>
	public static byte[] Png { get; } = Convert.FromBase64String(
		"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

	/// <summary>Makes the replay file of a score submission: an LZMA header followed by random bytes.</summary>
	/// <remarks>The server keeps the bytes as they are and serves them back; it never unpacks them, so the body is not a decodable LZMA stream.</remarks>
	/// <param name="random">The source of the bytes.</param>
	/// <param name="size">The size of the file; at least 24 bytes, below which the server keeps no replay.</param>
	public static byte[] Replay(Random random, int size = 2048)
	{
		var file = new byte[Math.Max(size, MinimumBytes)];
		random.NextBytes(file);
		file[0] = 0x5D; // the properties byte of the usual LZMA settings
		BitConverter.TryWriteBytes(file.AsSpan(1, sizeof(int)), 1 << 16);
		BitConverter.TryWriteBytes(file.AsSpan(5, sizeof(long)), -1L);
		return file;
	}
}
