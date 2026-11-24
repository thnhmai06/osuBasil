namespace Basil.Host.Shared.Http;

/// <summary>The kind of content an upload must begin with.</summary>
public enum UploadSignature : byte
{
	/// <summary>Any content: the bytes are opaque to the server.</summary>
	None,

	/// <summary>A ZIP archive, such as a beatmapset's <c>.osz</c>.</summary>
	Archive,

	/// <summary>A PNG, JPEG, GIF or WebP picture.</summary>
	Image
}

internal static class UploadSignatures
{
	/// <summary>How many leading bytes decide whether content has the signature.</summary>
	public const int HeaderLength = 12;

	public static bool Matches(this UploadSignature signature, ReadOnlySpan<byte> header)
	{
		return signature switch
		{
			UploadSignature.None => true,
			UploadSignature.Archive => header.StartsWith("PK\x03\x04"u8),
			_ => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
			     header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ||
			     header.StartsWith("GIF8"u8) ||
			     (header.Length >= HeaderLength && header.StartsWith("RIFF"u8) && header[8..].StartsWith("WEBP"u8))
		};
	}
}
