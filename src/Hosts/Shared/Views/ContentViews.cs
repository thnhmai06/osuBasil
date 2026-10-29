using Basil.Application.Content.Persistence;
using Basil.Domain.Content;

namespace Basil.Host.Shared.Views;

/// <summary>A FAQ entry.</summary>
public sealed record FaqView(string Entry, string Text);

/// <summary>A main-menu banner; <c>Image</c> names the image on the assets host under <c>/menu/banners</c>.</summary>
public sealed record BannerView(string Image, string Url, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Current)
{
	/// <summary>Describes a banner at a moment.</summary>
	public static BannerView Of(MenuBanner banner, DateTimeOffset now)
	{
		return new BannerView(Path.GetFileName(banner.Image.Value), banner.Url.AbsoluteUri, banner.StartsAt, banner.EndsAt,
			banner.IsCurrent(now));
	}
}

/// <summary>The main-menu icon.</summary>
public sealed record IconView(string? Image, string? Url);

/// <summary>A stored screenshot.</summary>
public sealed record ScreenshotView(string Id);

/// <summary>The locks as one flag each.</summary>
public sealed record LocksView(bool Accounts, bool Rooms, bool Beatmapsets, bool Profiles)
{
	/// <summary>Reads the locks as booleans.</summary>
	public static LocksView Of(Locks locks)
	{
		return new LocksView(locks.HasFlag(Locks.Accounts), locks.HasFlag(Locks.Rooms),
			locks.HasFlag(Locks.Beatmapsets), locks.HasFlag(Locks.Profiles));
	}

	/// <summary>Turns the booleans into the flags.</summary>
	public Locks ToLocks()
	{
		return (Accounts ? Locks.Accounts : 0) | (Rooms ? Locks.Rooms : 0) | (Beatmapsets ? Locks.Beatmapsets : 0) |
		       (Profiles ? Locks.Profiles : 0);
	}
}
