using Basil.Domain.Content;

namespace Basil.Domain.Tests.Content;

public class MenuBannerTests
{
	private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

	private static MenuBanner Make(DateTimeOffset? begins, DateTimeOffset? expires, Uri? url = null)
	{
		return new MenuBanner
		{
			Image = new ContentReference("banner.png"),
			Url = url ?? new Uri("https://example.test"),
			StartsAt = begins,
			EndsAt = expires,
			CreatedAt = Now
		};
	}

	private static MenuBanner MakeEndsFirst(DateTimeOffset? begins, DateTimeOffset? expires)
	{
		return new MenuBanner
		{
			Image = new ContentReference("banner.png"),
			Url = new Uri("https://example.test"),
			EndsAt = expires,
			StartsAt = begins,
			CreatedAt = Now
		};
	}

	[Fact]
	public void IsCurrent_BothBoundsNull_AlwaysCurrent()
	{
		Assert.True(Make(null, null).IsCurrent(Now));
	}

	[Fact]
	public void IsCurrent_BeginsNull_OnlyExpiresChecked()
	{
		var now = Now;
		Assert.True(Make(null, now.AddDays(1)).IsCurrent(now));
		Assert.False(Make(null, now.AddDays(-1)).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_ExpiresNull_OnlyBeginsChecked()
	{
		var now = Now;
		Assert.True(Make(now.AddDays(-1), null).IsCurrent(now));
		Assert.False(Make(now.AddDays(1), null).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_BothBoundsSet_WithinWindow_ReturnsTrue()
	{
		var now = Now;
		Assert.True(Make(now.AddDays(-1), now.AddDays(1)).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_BothBoundsSet_OutsideWindow_ReturnsFalse()
	{
		var now = Now;
		Assert.False(Make(now.AddDays(1), now.AddDays(2)).IsCurrent(now));
		Assert.False(Make(now.AddDays(-2), now.AddDays(-1)).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_ExactlyAtEnd_IsNotCurrent()
	{
		var endsAt = Now.AddDays(1);

		Assert.False(Make(Now.AddDays(-1), endsAt).IsCurrent(endsAt));
	}

	[Fact]
	public void IsCurrent_ExactlyAtStart_IsCurrent()
	{
		var startsAt = Now.AddDays(1);

		Assert.True(Make(startsAt, Now.AddDays(2)).IsCurrent(startsAt));
	}

	[Fact]
	public void Url_Relative_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(null, null, new Uri("/relative", UriKind.Relative)));
	}

	[Fact]
	public void Url_NotHttp_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(null, null, new Uri("javascript:alert(1)")));
	}

	[Fact]
	public void Url_FileScheme_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(null, null, new Uri("file:///tmp/banner.png")));
	}

	[Fact]
	public void StartsAt_AfterEndsAt_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(Now.AddDays(1), Now));
	}

	[Fact]
	public void EndsAt_BeforeStartsAt_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => MakeEndsFirst(Now.AddDays(1), Now));
	}
}