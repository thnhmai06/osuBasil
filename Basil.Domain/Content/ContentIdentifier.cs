using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Content;

/// <summary>Identifies one version of a piece of stored content by its size, modification time and hash.</summary>
/// <param name="Metadata">The size and modification time of the content.</param>
/// <param name="Hash">The MD5 hash of the content.</param>
public readonly record struct ContentIdentifier(ContentMetadata Metadata, Md5Hash Hash);

/// <summary>The size and modification time of a piece of stored content.</summary>
/// <param name="Size">The size in bytes.</param>
/// <param name="ModifiedAt">The moment the content last changed.</param>
public readonly record struct ContentMetadata(long Size, DateTimeOffset ModifiedAt)
{
	/// <summary>The size of the content in bytes.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public long Size
	{
		get;
		init => field = Check(value);
	} = Check(Size);

	/// <summary>The moment the content last changed, as a UTC time.</summary>
	public DateTimeOffset ModifiedAt
	{
		get;
		init => field = value.ToUniversalTime();
	} = ModifiedAt.ToUniversalTime();

	private static long Check(long value)
	{
		return value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Content size cannot be negative.");
	}
}