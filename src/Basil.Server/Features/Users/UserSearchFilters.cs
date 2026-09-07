using Basil.Domain.Login;

namespace Basil.Server.Features.Users;

/// <summary>
///     A parsed user search query: a free-text id/username portion plus zero or more structured
///     filters, in the same <c>key&lt;operator&gt;value</c> style as
///     <see cref="Basil.Server.Features.Beatmaps.BeatmapsetSearchFilters" />.
/// </summary>
/// <param name="Keywords">
///     The free-text portion of the query. Matched against a numeric user id exactly, or a substring
///     of the username (case- and space-insensitive), whichever applies.
/// </param>
/// <param name="Countries">
///     The user's country must equal one of these values (an OR match) -- the query names more than
///     one by concatenating their two-letter codes, e.g. <c>country=vnusuk</c> for Vietnam, the US, or
///     the UK.
/// </param>
/// <param name="PrivilegeMask">
///     The user's privilege flags must include every bit set in this mask (a bitwise AND-match, not
///     an exact-equality comparison against the stored value).
/// </param>
public sealed record UserSearchFilters(
	string? Keywords = null,
	IReadOnlyList<Country>? Countries = null,
	ushort? PrivilegeMask = null)
{
	/// <summary>An empty filter set: every user matches.</summary>
	public static readonly UserSearchFilters Empty = new();
}