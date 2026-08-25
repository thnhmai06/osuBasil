namespace Basil.Domain.Content;

/// <summary>
///     Represents one main-menu promotional banner (`assets.&lt;domain&gt;/menu-content.json`).
/// </summary>
/// <param name="Id">The unique identifier of the banner.</param>
/// <param name="Source">
///     Either a locally stored filename (under `Data/Menu/Banners/`) or an external `http(s)` URL.
/// </param>
/// <param name="Url">The click-through URL opened when the banner is clicked.</param>
/// <param name="Begins">
///     The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound
///     (already current).
/// </param>
/// <param name="Expires">
///     The UTC instant the banner stops being current, or <see langword="null" /> for no upper bound
///     (never expires). A banner with both <paramref name="Begins" /> and <paramref name="Expires" />
///     unset is permanent — always current.
/// </param>
/// <param name="CreatedAt">The UTC instant the banner was created.</param>
public sealed record MenuBanner(
	int Id,
	string Source,
	string Url,
	DateTime? Begins,
	DateTime? Expires,
	DateTime CreatedAt)
{
	/// <summary>Gets whether the banner is currently within its display window.</summary>
	/// <param name="now">The instant to check against, in UTC.</param>
	public bool IsCurrent(DateTime now)
	{
		return (Begins is null || Begins <= now) && (Expires is null || now <= Expires);
	}
}