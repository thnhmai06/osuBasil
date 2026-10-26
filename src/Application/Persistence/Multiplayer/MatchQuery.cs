using System.Diagnostics.CodeAnalysis;
using Basil.Application.Persistence.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Persistence.Multiplayer;

/// <summary>Which matches a match listing includes.</summary>
/// <param name="Ended">
///     Whether only ended (<see langword="true" />) or only running (<see langword="false" />) matches are
///     included, or <see langword="null" /> for both.
/// </param>
/// <param name="Private">
///     Whether only private matches (<see langword="true" />) or only public ones (<see langword="false" />) are
///     included, or <see langword="null" /> for both; a user's search is narrowed with <see cref="LimitTo" />.
/// </param>
/// <param name="VisibleTo">
///     The id of the user the listing is for, or <see langword="null" /> for no restriction. A private match is then
///     included only when the user took part in it or manages it (its creator, or a referee that is still one); public
///     matches are included regardless. It is not part of the search syntax.
/// </param>
public sealed record MatchQuery(bool? Ended = null, bool? Private = null, int? VisibleTo = null)
	: IParsable<MatchQuery>
{
	/// <summary>Reads a query written in the match search syntax.</summary>
	/// <param name="s">
	///     The search text, for example <c>ended=0 private=*</c>; a state takes <c>1</c>, <c>0</c> or <c>*</c> for
	///     both, and anything else in the text is ignored.
	/// </param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <returns>The query.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
	public static MatchQuery Parse(string s, IFormatProvider? provider = null)
	{
		ArgumentNullException.ThrowIfNull(s);

		bool? ended = null;
		bool? isPrivate = null;
		SearchSyntax.Parse(s, (key, op, rawValue) =>
		{
			if (op is not (":" or "=") || !SearchSyntax.TryParseState(rawValue, out var state)) return false;
			switch (key)
			{
				case "ended":
					ended = state;
					return true;
				case "private":
					isPrivate = state;
					return true;
				default:
					return false;
			}
		});

		return new MatchQuery(ended, isPrivate);
	}

	/// <summary>Reads a query written in the match search syntax.</summary>
	/// <param name="s">The search text.</param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <param name="result">The query, or <see langword="null" /> when <paramref name="s" /> is <see langword="null" />.</param>
	/// <returns><see langword="true" /> unless <paramref name="s" /> is <see langword="null" />; any text reads as a query.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out MatchQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	/// <summary>Narrows the query to what a user with the given permissions may see.</summary>
	/// <param name="effective">The asker's effective permissions.</param>
	/// <param name="viewer">The asker.</param>
	/// <returns>
	///     The query as asked when the asker may view private matches (<see cref="Permissions.RoomViewPrivate" />);
	///     otherwise a query whose private matches are only the ones the asker took part in or manages.
	/// </returns>
	public MatchQuery LimitTo(Permissions effective, UserRecord viewer)
	{
		ArgumentNullException.ThrowIfNull(viewer);
		return effective.Allows(Permissions.RoomViewPrivate) ? this : this with { VisibleTo = viewer.Id };
	}
}
