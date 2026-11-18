using Basil.Domain.Cryptography;

namespace Basil.Domain.Content;

public readonly record struct ContentFingerprint(ContentStamp Stamp, Md5 Hash);

public readonly record struct ContentStamp(long Size, DateTimeOffset ModifiedAt)
{
	public long Size
	{
		get;
		init => field = Check(value);
	} = Check(Size);

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