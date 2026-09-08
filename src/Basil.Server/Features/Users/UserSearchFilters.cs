using Basil.Domain.Login;
using Basil.Domain.Users;

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
/// <param name="Privilege">
///     The user's privilege flags must include every flag set here (a bitwise AND-match, not
///     an exact-equality comparison against the stored value).
/// </param>
/// <param name="Silenced">
///     When set, matches only currently-silenced users (<see langword="true" />) or only
///     not-currently-silenced users (<see langword="false" />). <see langword="null" /> applies no
///     filter.
/// </param>
/// <param name="IncludeDeleted">
///     When <see langword="true" />, a soft-deleted user can match; otherwise a deleted user never
///     matches, regardless of the other filters.
/// </param>
public sealed record UserSearchFilters(
	string? Keywords = null,
	IReadOnlyList<Country>? Countries = null,
	UserPrivileges? Privilege = null,
	bool? Silenced = null,
	bool IncludeDeleted = false)
{
	/// <summary>An empty filter set: every non-deleted user matches.</summary>
	public static readonly UserSearchFilters Empty = new();
}