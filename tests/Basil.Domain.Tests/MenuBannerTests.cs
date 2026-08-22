using Basil.Domain.Content;

namespace Basil.Domain.Tests;

public class MenuBannerTests
{
	private static MenuBanner Make(DateTime? begins, DateTime? expires)
	{
		return new MenuBanner(1, "banner.png", "https://example.test", begins, expires, DateTime.UtcNow);
	}

	[Fact]
	public void IsCurrent_BothBoundsNull_AlwaysCurrent()
	{
		Assert.True(Make(null, null).IsCurrent(DateTime.UtcNow));
	}

	[Fact]
	public void IsCurrent_BeginsNull_OnlyExpiresChecked()
	{
		var now = DateTime.UtcNow;
		Assert.True(Make(null, now.AddDays(1)).IsCurrent(now));
		Assert.False(Make(null, now.AddDays(-1)).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_ExpiresNull_OnlyBeginsChecked()
	{
		var now = DateTime.UtcNow;
		Assert.True(Make(now.AddDays(-1), null).IsCurrent(now));
		Assert.False(Make(now.AddDays(1), null).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_BothBoundsSet_WithinWindow_ReturnsTrue()
	{
		var now = DateTime.UtcNow;
		Assert.True(Make(now.AddDays(-1), now.AddDays(1)).IsCurrent(now));
	}

	[Fact]
	public void IsCurrent_BothBoundsSet_OutsideWindow_ReturnsFalse()
	{
		var now = DateTime.UtcNow;
		Assert.False(Make(now.AddDays(1), now.AddDays(2)).IsCurrent(now));
		Assert.False(Make(now.AddDays(-2), now.AddDays(-1)).IsCurrent(now));
	}
}