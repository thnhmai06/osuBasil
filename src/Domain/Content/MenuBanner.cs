using Basil.Domain.Common;

namespace Basil.Domain.Content;

/// <summary>
///     Represents one main-menu promotional banner.
/// </summary>
public sealed class MenuBanner : IEquatable<MenuBanner>
{
	/// <summary>Gets the image shown: a canonical asset reference.</summary>
	/// <exception cref="ArgumentException">The reference is the default value.</exception>
	public required ContentReference Image
	{
		get;
		init => field = value.Value is null
			? throw new ArgumentException("The image must refer to an asset.", nameof(value))
			: value;
	}

	/// <summary>The click-through URI opened when the banner is clicked.</summary>
	/// <exception cref="ArgumentException">The address is not absolute with an http or https scheme.</exception>
	public required Uri Url
	{
		get;
		set => field = value.ThrowIfHttp();
	}

	/// <summary>
	///     The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound
	///     (already current).
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is after <see cref="EndsAt" />.</exception>
	public required DateTimeOffset? StartsAt
	{
		get;
		set
		{
			if (value > EndsAt)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A banner cannot start after it ends.");
			field = value;
		}
	}

	/// <summary>
	///     The UTC instant the banner stops being current, or <see langword="null" /> for no upper
	///     bound (never expires).
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is before <see cref="StartsAt" />.</exception>
	public required DateTimeOffset? EndsAt
	{
		get;
		set
		{
			if (value < StartsAt)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A banner cannot end before it starts.");
			field = value;
		}
	}

	/// <summary>The UTC instant the banner was created.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>Gets a value that indicates whether this banner equals another by image.</summary>
	/// <param name="other">The banner to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> is non-null and has the same
	///     <see cref="Image" /> as this banner; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(MenuBanner? other)
	{
		if (other is null) return false;
		return Image == other.Image;
	}

	/// <summary>Gets whether the banner is currently within its display window.</summary>
	/// <param name="now">The instant to check against, in UTC.</param>
	/// <returns>
	///     <see langword="true" /> from <see cref="StartsAt" /> up to, but not including, <see cref="EndsAt" />.
	/// </returns>
	public bool IsCurrent(DateTimeOffset now)
	{
		return (StartsAt is null || StartsAt <= now) && (EndsAt is null || now < EndsAt);
	}

	/// <summary>Gets a value that indicates whether this banner equals another object by image.</summary>
	/// <param name="obj">The object to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="MenuBanner" /> that
	///     equals this one; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is MenuBanner other && Equals(other);
	}

	/// <summary>Returns a hash code derived from the banner's <see cref="Image" />.</summary>
	/// <returns>A hash code consistent with the banner's value equality, which compares <see cref="Image" />.</returns>
	public override int GetHashCode()
	{
		return Image.GetHashCode();
	}
}