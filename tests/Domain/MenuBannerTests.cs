using Basil.Domain.Content;

namespace Basil.Domain.Tests;

public class MenuBannerTests
{
	private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

	private static MenuBanner Make(DateTimeOffset? begins, DateTimeOffset? expires)
	{
		return new MenuBanner
		{
			Image = new ContentReference("banner.png"),
			Url = new Uri("https://example.test"),
			StartsAt = begins,
			EndsAt = expires,
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
}