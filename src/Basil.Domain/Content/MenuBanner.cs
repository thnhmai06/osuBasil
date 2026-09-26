namespace Basil.Domain.Content;

/// <summary>
///     Represents one main-menu promotional banner (`assets.&lt;domain&gt;/menu-content.json`).
/// </summary>
public sealed class MenuBanner : IEquatable<MenuBanner>
{
	/// <summary>
	///     The image source which is displayed.
	/// </summary>
	public required Uri Image { get; init; }

	/// <summary>The click-through URI opened when the banner is clicked.</summary>
	public required Uri Url { get; set; }

	/// <summary>
	///     The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound
	///     (already current).
	/// </summary>
	public required DateTimeOffset? Begins { get; set; }

	/// <summary>
	///     The UTC instant the banner stops being current, or <see langword="null" /> for no upper
	///     bound (never expires).
	/// </summary>
	public required DateTimeOffset? Expires { get; set; }

	/// <summary>The UTC instant the banner was created.</summary>
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

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
	public bool IsCurrent(DateTimeOffset now)
	{
		return (Begins is null || Begins <= now) && (Expires is null || now <= Expires);
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