using Basil.Domain.Common;

namespace Basil.Domain.Content;

/// <summary>What identifies stored content: its stamp and the MD5 hash of its bytes.</summary>
/// <param name="Stamp">The content's size and modification time.</param>
/// <param name="Hash">The MD5 hash of the content's bytes.</param>
/// <remarks>
///     Two contents with the same hash are the same content; the stamp only tells, without reading, whether content
///     may have changed since its fingerprint was taken.
/// </remarks>
public readonly record struct ContentFingerprint(ContentStamp Stamp, Md5 Hash);

/// <summary>What can be told about stored content without reading it: its size and when it last changed.</summary>
/// <param name="Size">The content's size in bytes.</param>
/// <param name="ModifiedAt">When the content last changed, in UTC.</param>
/// <remarks>Content with a different stamp may have changed; content with the same stamp is taken as unchanged.</remarks>
public readonly record struct ContentStamp(long Size, DateTimeOffset ModifiedAt)
{
	/// <summary>Gets the content's size in bytes.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public long Size
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	} = Size;

	/// <summary>Gets when the content last changed, in UTC.</summary>
	public DateTimeOffset ModifiedAt { get; init; } = ModifiedAt.ToUniversalTime();
}