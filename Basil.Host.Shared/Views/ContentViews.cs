using Basil.Application.Shared.Settings;
using Basil.Domain.Content;

namespace Basil.Host.Shared.Views;

public sealed record FaqView(string Entry, string Text);

public sealed record BannerView(
	string Image,
	string Url,
	DateTimeOffset? StartsAt,
	DateTimeOffset? EndsAt,
	bool Current)
{
	public static BannerView Of(MenuBanner banner, DateTimeOffset now)
	{
		return new BannerView(Path.GetFileName(banner.Image.Value), banner.Url.AbsoluteUri, banner.StartsAt,
			banner.EndsAt,
			banner.IsCurrent(now));
	}
}

public sealed record IconView(string? Image, string? Url);

public sealed record ScreenshotView(string Id);

public sealed record LocksView(bool Accounts, bool Rooms, bool Beatmapsets, bool Profiles)
{
	public static LocksView Of(Locks locks)
	{
		return new LocksView(locks.HasFlag(Locks.Accounts), locks.HasFlag(Locks.Rooms),
			locks.HasFlag(Locks.Beatmapsets), locks.HasFlag(Locks.Profiles));
	}

	public Locks ToLocks()
	{
		return (Accounts ? Locks.Accounts : 0) | (Rooms ? Locks.Rooms : 0) | (Beatmapsets ? Locks.Beatmapsets : 0) |
		       (Profiles ? Locks.Profiles : 0);
	}
}