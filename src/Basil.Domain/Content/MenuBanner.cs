namespace Basil.Domain.Content;

/// <summary>
///     Represents one main-menu promotional banner (`assets.&lt;domain&gt;/menu-content.json`).
/// </summary>
public sealed class MenuBanner : IEquatable<MenuBanner>
{
	/// <summary>The unique identifier of the banner.</summary>
	public required int Id { get; init; }

	/// <summary>
	///     Either a locally stored filename (under `Data/Menu/Banners/`) or an external `http(s)` URL.
	/// </summary>
	public required string Image { get; set; }

	/// <summary>The click-through URL opened when the banner is clicked.</summary>
	public required string Url { get; set; }

	/// <summary>
	///     The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound
	///     (already current).
	/// </summary>
	public required DateTimeOffset? Begins { get; set; }

	public required DateTimeOffset? Expires { get; set; }

	/// <summary>The UTC instant the banner was created.</summary>
	public DateTimeOffset CreatedAt { get; init; }

	public bool Equals(MenuBanner? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>Gets whether the banner is currently within its display window.</summary>
	/// <param name="now">The instant to check against, in UTC.</param>
	public bool IsCurrent(DateTimeOffset now)
	{
		return (Begins is null || Begins <= now) && (Expires is null || now <= Expires);
	}

	public override bool Equals(object? obj)
	{
		return obj is MenuBanner other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}