using Basil.Shared.Utilities;

namespace Basil.Domain.Content;

/// <summary>A banner of the main menu: an image that links to an address, shown during an optional time window.</summary>
/// <remarks>Two banners are equal when their images are equal.</remarks>
public sealed class MenuBanner : IEquatable<MenuBanner>
{
	/// <summary>The image of the banner.</summary>
	/// <exception cref="ArgumentException">The value is a default reference.</exception>
	public required ContentReference Image
	{
		get;
		init => field = value.Value is null
			? throw new ArgumentException("The image must refer to an asset.", nameof(value))
			: value;
	}

	/// <summary>The address the banner links to.</summary>
	/// <exception cref="InvalidAddressException">The value is not an absolute http or https address.</exception>
	public required Uri Url
	{
		get;
		set => field = value.IsHttp() ? value : throw new InvalidAddressException();
	}

	/// <summary>The moment the banner starts to show, or <see langword="null" /> when it has no start.</summary>
	/// <exception cref="InvalidBannerWindowException">The value is after <see cref="EndsAt" />.</exception>
	public required DateTimeOffset? StartsAt
	{
		get;
		set
		{
			if (!IsValidWindow(value, EndsAt))
				throw new InvalidBannerWindowException(value, EndsAt);
			field = value;
		}
	}

	/// <summary>The moment the banner stops showing, which is itself no longer included, or <see langword="null" /> when it has no end.</summary>
	/// <exception cref="InvalidBannerWindowException">The value is before <see cref="StartsAt" />.</exception>
	public required DateTimeOffset? EndsAt
	{
		get;
		set
		{
			if (!IsValidWindow(StartsAt, value))
				throw new InvalidBannerWindowException(StartsAt, value);
			field = value;
		}
	}

	/// <summary>The moment the banner was created.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>Checks whether the banner shows at a moment.</summary>
	/// <param name="now">The moment to check.</param>
	/// <returns><see langword="true" /> when the moment is inside the window.</returns>
	public bool IsCurrent(DateTimeOffset now)
	{
		return (StartsAt is null || StartsAt <= now) && (EndsAt is null || now < EndsAt);
	}

	private static bool IsValidWindow(DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		return !(startsAt > endsAt);
	}

	public bool Equals(MenuBanner? other)
	{
		if (other is null) return false;
		return Image == other.Image;
	}

	public override bool Equals(object? obj)
	{
		return obj is MenuBanner other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Image.GetHashCode();
	}
}