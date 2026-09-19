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

	public required DateTimeOffset? Expires { get; set; }

	/// <summary>The UTC instant the banner was created.</summary>
	public DateTimeOffset CreatedAt { get; init; }

	/// <summary>Gets whether the banner is currently within its display window.</summary>
	/// <param name="now">The instant to check against, in UTC.</param>
	public bool IsCurrent(DateTimeOffset now)
	{
		return (Begins is null || Begins <= now) && (Expires is null || now <= Expires);
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