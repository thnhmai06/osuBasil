using Basil.Application.Shared.Storage;

namespace Basil.Infrastructure.Shared.Storage;

public static class ImageFiles
{
	private const int HeaderLength = 12;

	/// <summary>Describes a stored picture from its first bytes; a stream that is not a PNG, JPEG, GIF, WebP or BMP is closed and gives <c>null</c>.</summary>
	public static async Task<ImageFile?> DescribeAsync(Stream? content, CancellationToken ct = default)
	{
		if (content is null) return null;

		try
		{
			var header = new byte[HeaderLength];
			var read = await content.ReadAtLeastAsync(header, HeaderLength, false, ct);
			if (ContentTypeOf(header.AsSpan(0, read)) is { } type && content.CanSeek)
			{
				content.Position = 0;
				var modified = content is FileStream file ? File.GetLastWriteTimeUtc(file.Name) : DateTime.UnixEpoch;
				return new ImageFile(content, type, new DateTimeOffset(modified, TimeSpan.Zero));
			}
		}
		catch
		{
			await content.DisposeAsync();
			throw;
		}

		await content.DisposeAsync();
		return null;
	}

	private static string? ContentTypeOf(ReadOnlySpan<byte> header)
	{
		if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })) return "image/png";
		if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
		if (header.StartsWith("GIF8"u8)) return "image/gif";
		if (header.StartsWith("BM"u8)) return "image/bmp";
		if (header.Length >= HeaderLength && header.StartsWith("RIFF"u8) && header[8..].StartsWith("WEBP"u8))
			return "image/webp";
		return null;
	}
}
