using System.Globalization;
using Basil.Application.Common;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Users;

/// <summary>Which users a user listing includes.</summary>
/// <param name="Text">Text matched against the user's id exactly or contained in the name (ignoring case and spaces), or <see langword="null" /> for any.</param>
/// <param name="Countries">The countries a user may be from, or <see langword="null" /> for any.</param>
/// <param name="Privilege">Privileges a user must all hold, or <see langword="null" /> for any.</param>
/// <param name="IncludeDeleted">Whether deleted users are included.</param>
public sealed record UserQuery(
	string? Text = null,
	IReadOnlyList<Country>? Countries = null,
	ClientPrivileges? Privilege = null,
	bool IncludeDeleted = false)
{
	/// <summary>Reads a query written in the user search syntax.</summary>
	/// <param name="text">The search text, for example <c>country=vnus privilege=4 bob</c>.</param>
	/// <param name="includeDeleted">Whether deleted users are included.</param>
	/// <returns>The query; a filter with an unknown key or a value that does not parse stays in <see cref="Text" />.</returns>
	public static UserQuery Parse(string text, bool includeDeleted = false)
	{
		if (string.IsNullOrWhiteSpace(text))
			return new UserQuery(null, null, null, includeDeleted);

		IReadOnlyList<Country>? countries = null;
		ClientPrivileges? privilege = null;

		var keywords = SearchSyntax.Parse(text, (key, op, rawValue) =>
		{
			if (op is not (":" or "=")) return false;
			switch (key)
			{
				case "country" when TryParseCountries(rawValue, out var parsedCountries):
					countries = parsedCountries;
					return true;
				case "privilege" when TryParsePrivilege(rawValue, out var parsedPrivilege):
					privilege = parsedPrivilege;
					return true;
				default:
					return false;
			}
		});

		return new UserQuery(keywords, countries, privilege, includeDeleted);

		static bool TryParseCountries(string rawValue, out IReadOnlyList<Country> parsedCountries)
		{
			parsedCountries = [];
			if (rawValue.Length == 0 || rawValue.Length % 2 != 0) return false;

			var parsed = new List<Country>(rawValue.Length / 2);
			for (var i = 0; i < rawValue.Length; i += 2)
			{
				if (!Enum.TryParse<Country>(rawValue.AsSpan(i, 2), true, out var country)) return false;
				parsed.Add(country);
			}

			parsedCountries = parsed;
			return true;
		}

		static bool TryParsePrivilege(string rawValue, out ClientPrivileges parsedPrivilege)
		{
			if (byte.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var mask))
			{
				parsedPrivilege = (ClientPrivileges)mask;
				return true;
			}

			return Enum.TryParse(rawValue, true, out parsedPrivilege);
		}
	}
}