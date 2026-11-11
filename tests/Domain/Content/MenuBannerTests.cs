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
	public void BothBoundsNull_Current()
	{
		Assert.True(Make(null, null).IsCurrent(Now));
	}

	[Fact]
	public void BeginsNull_ExpiresChecked()
	{
		var now = Now;
		Assert.True(Make(null, now.AddDays(1)).IsCurrent(now));
		Assert.False(Make(null, now.AddDays(-1)).IsCurrent(now));
	}

	[Fact]
	public void ExpiresNull_BeginsChecked()
	{
		var now = Now;
		Assert.True(Make(now.AddDays(-1), null).IsCurrent(now));
		Assert.False(Make(now.AddDays(1), null).IsCurrent(now));
	}

	[Fact]
	public void BoundsSetWithin_Current()
	{
		var now = Now;
		Assert.True(Make(now.AddDays(-1), now.AddDays(1)).IsCurrent(now));
	}

	[Fact]
	public void BoundsSetOutside_NotCurrent()
	{
		var now = Now;
		Assert.False(Make(now.AddDays(1), now.AddDays(2)).IsCurrent(now));
		Assert.False(Make(now.AddDays(-2), now.AddDays(-1)).IsCurrent(now));
	}

	[Fact]
	public void ExactlyAtEnd_NotCurrent()
	{
		var endsAt = Now.AddDays(1);

		Assert.False(Make(Now.AddDays(-1), endsAt).IsCurrent(endsAt));
	}

	[Fact]
	public void ExactlyAtStart_Current()
	{
		var startsAt = Now.AddDays(1);

		Assert.True(Make(startsAt, Now.AddDays(2)).IsCurrent(startsAt));
	}

	[Fact]
	public void RelativeUrl_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(null, null, new Uri("/relative", UriKind.Relative)));
	}

	[Fact]
	public void NonHttpUrl_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(null, null, new Uri("javascript:alert(1)")));
	}

	[Fact]
	public void FileSchemeUrl_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(null, null, new Uri("file:///tmp/banner.png")));
	}

	[Fact]
	public void StartsAfterEnds_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(Now.AddDays(1), Now));
	}

	[Fact]
	public void EndsBeforeStarts_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => MakeEndsFirst(Now.AddDays(1), Now));
	}

	[Fact]
	public void IsValidWindow_NoBounds_IsTrue()
	{
		Assert.True(MenuBanner.IsValidWindow(null, null));
	}

	[Fact]
	public void IsValidWindow_OneBound_IsTrue()
	{
		Assert.True(MenuBanner.IsValidWindow(Now, null));
		Assert.True(MenuBanner.IsValidWindow(null, Now));
	}

	[Fact]
	public void IsValidWindow_StartBeforeOrEqualToEnd_IsTrue()
	{
		Assert.True(MenuBanner.IsValidWindow(Now, Now.AddDays(1)));
		Assert.True(MenuBanner.IsValidWindow(Now, Now));
	}

	[Fact]
	public void IsValidWindow_StartAfterEnd_IsFalse()
	{
		Assert.False(MenuBanner.IsValidWindow(Now.AddDays(1), Now));
	}
}