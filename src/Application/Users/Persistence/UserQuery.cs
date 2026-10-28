using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Application.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

/// <summary>Which users a user listing includes.</summary>
/// <param name="Text">
///     Text matched against the user's id exactly or contained in the name (ignoring case and spaces), or
///     <see langword="null" /> for any.
/// </param>
/// <param name="Countries">The countries a user may be from, or <see langword="null" /> for any.</param>
/// <param name="Permissions">Permissions a user must all be granted, or <see langword="null" /> for any.</param>
/// <param name="Deleted">
///     Whether only deleted (<see langword="true" />) or only existing (<see langword="false" />) users are
///     included, or <see langword="null" /> for both; a user's search is narrowed with <see cref="LimitTo" />.
/// </param>
public sealed record UserQuery(
	string? Text = null,
	IReadOnlyList<Country>? Countries = null,
	UserPermissions? Permissions = null,
	bool? Deleted = null) : IParsable<UserQuery>
{
	/// <summary>Reads a query written in the user search syntax.</summary>
	/// <param name="s">
	///     The search text, for example <c>country=usvn permission=4 deleted=* bob</c>; a state takes <c>1</c>,
	///     <c>0</c> or <c>*</c> for both.
	/// </param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <returns>The query; a filter with an unknown key or a value that does not parse stays in <see cref="Text" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
	public static UserQuery Parse(string s, IFormatProvider? provider = null)
	{
		ArgumentNullException.ThrowIfNull(s);

		IReadOnlyList<Country>? countries = null;
		UserPermissions? permissions = null;
		bool? deleted = null;

		var keywords = SearchSyntax.Parse(s, (key, op, rawValue) =>
		{
			if (op is not (":" or "=")) return false;
			switch (key)
			{
				case "country" when TryParseCountries(rawValue, out var parsedCountries):
					countries = parsedCountries;
					return true;
				case "permission" when ulong.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture,
					out var mask):
					permissions = (UserPermissions)mask;
					return true;
				case "deleted" when SearchSyntax.TryParseState(rawValue, out var state):
					deleted = state;
					return true;
				default:
					return false;
			}
		});

		return new UserQuery(keywords, countries, permissions, deleted);

		static bool TryParseCountries(string rawValue, out IReadOnlyList<Country> parsedCountries)
		{
			parsedCountries = [];
			if (rawValue.Length == 0 || rawValue.Length % 2 != 0) return false;

			var parsed = new List<Country>(rawValue.Length / 2);
			for (var i = 0; i < rawValue.Length; i += 2)
			{
				if (!char.IsAsciiLetter(rawValue[i]) || !char.IsAsciiLetter(rawValue[i + 1])
				                                     || !Enum.TryParse<Country>(rawValue.AsSpan(i, 2), true,
					                                     out var country)) return false;
				parsed.Add(country);
			}

			parsedCountries = parsed;
			return true;
		}
	}

	/// <summary>Reads a query written in the user search syntax.</summary>
	/// <param name="s">The search text.</param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <param name="result">The query, or <see langword="null" /> when <paramref name="s" /> is <see langword="null" />.</param>
	/// <returns><see langword="true" /> unless <paramref name="s" /> is <see langword="null" />; any text reads as a query.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out UserQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	/// <summary>Narrows the query to what a user with the given permissions may see.</summary>
	/// <param name="effective">The asker's effective permissions.</param>
	/// <returns>The query, which leaves deleted users out unless the asker may see them.</returns>
	public UserQuery LimitTo(UserPermissions effective)
	{
		return effective.Allows(UserPermissions.UserManageViewDeleted) ? this : this with { Deleted = false };
	}
}