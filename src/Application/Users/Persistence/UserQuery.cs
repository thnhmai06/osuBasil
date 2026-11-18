using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Application.Shared.Paging;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Users.Persistence;

public sealed record UserQuery(
	string? Text = null,
	IReadOnlyList<Country>? Countries = null,
	UserPrivileges? Privileges = null,
	bool? Deleted = null) : IParsable<UserQuery>
{
	public static UserQuery Parse(string s, IFormatProvider? provider = null)
	{
		ArgumentNullException.ThrowIfNull(s);

		IReadOnlyList<Country>? countries = null;
		UserPrivileges? privileges = null;
		bool? deleted = null;

		var keywords = SearchSyntax.Parse(s, (key, op, rawValue) =>
		{
			if (op is not (":" or "=")) return false;
			switch (key)
			{
				case "country" when TryParseCountries(rawValue, out var parsedCountries):
					countries = parsedCountries;
					return true;
				case "privilege" when ulong.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture,
					out var mask):
					privileges = (UserPrivileges)mask;
					return true;
				case "deleted" when SearchSyntax.TryParseState(rawValue, out var state):
					deleted = state;
					return true;
				default:
					return false;
			}
		});

		return new UserQuery(keywords, countries, privileges, deleted);
	}

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out UserQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	private static bool TryParseCountries(string rawValue, out IReadOnlyList<Country> parsedCountries)
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

	public UserQuery LimitTo(UserPrivileges effective)
	{
		return effective.Allows(UserPrivileges.Owner) ? this : this with { Deleted = false };
	}
}