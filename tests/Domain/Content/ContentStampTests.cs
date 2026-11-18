using Basil.Domain.Content;

namespace Basil.Domain.Tests.Content;

public class ContentMetadataTests
{
	private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

	[Fact]
	public void NegativeConstructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ContentMetadata(-1, Now));
	}

	[Fact]
	public void NegativeWith_Throws()
	{
		var stamp = new ContentMetadata(10, Now);

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = stamp with { Size = -1 });
	}

	[Fact]
	public void ModifiedAtConstructor_Utc()
	{
		var local = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.FromHours(5));

		var stamp = new ContentMetadata(10, local);

		Assert.Equal(TimeSpan.Zero, stamp.ModifiedAt.Offset);
		Assert.Equal(local.ToUniversalTime(), stamp.ModifiedAt);
	}

	[Fact]
	public void ModifiedAtWith_Utc()
	{
		var local = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.FromHours(5));
		var stamp = new ContentMetadata(10, Now);

		var changed = stamp with { ModifiedAt = local };

		Assert.Equal(TimeSpan.Zero, changed.ModifiedAt.Offset);
		Assert.Equal(local.ToUniversalTime(), changed.ModifiedAt);
	}
}