using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Client;

namespace Basil.Domain.Auth;

/// <summary>
///     Represents the data an osu! client sends when logging in.
/// </summary>
/// <param name="Username">The username the client sent.</param>
/// <param name="PasswordMd5">The MD5 hash of the password the client sent.</param>
/// <param name="ClientVersion">The version of the client.</param>
/// <param name="UtcOffset">The client's UTC offset, in hours.</param>
/// <param name="DisplayCity">Whether the client allows its city to be displayed.</param>
/// <param name="AcceptPm">Whether the client accepts private messages.</param>
/// <param name="ClientDetails">The client details captured from the login request.</param>
public sealed record LoginForm(
	string Username,
	string PasswordMd5,
	ClientVersion ClientVersion,
	int UtcOffset, // not DateTimeOffset
	bool DisplayCity,
	bool AcceptPm,
	ClientDetails ClientDetails) : IParsable<LoginForm>
{
	/// <summary>
	///     Parses a raw login request into a <see cref="LoginForm" />.
	/// </summary>
	/// <param name="s">The login payload from the client.</param>
	/// <param name="provider">The format provider to use for parsing.</param>
	/// <returns>The parsed login data.</returns>
	public static LoginForm Parse(string s, IFormatProvider? provider = null)
	{
		var decoded = s.TrimEnd('\n');

		var top = decoded.Split('\n', 3);
		if (top.Length != 3) throw new FormatException("Invalid login payload.");

		var username = top[0];
		var passwordMd5 = top[1];
		var fields = top[2].Split('|', 5);
		if (fields.Length != 5) throw new FormatException("Invalid login payload.");

		var osuVersion = ClientVersion.Parse(fields[0], provider);
		if (!int.TryParse(fields[1], out var utcOffset)) throw new FormatException("Invalid UTC offset.");
		var displayCity = fields[2] == "1";
		var clientHashes = fields[3];
		var pmPrivate = fields[4] == "1";
		var clientDetails = ClientDetails.Parse(clientHashes, provider);

		return new LoginForm(username, passwordMd5, osuVersion, utcOffset,
			displayCity, pmPrivate, clientDetails);
	}

	/// <summary>
	///     Attempts to parse a raw login request into a <see cref="LoginForm" />.
	/// </summary>
	public static bool TryParse(
		[NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out LoginForm result)
	{
		if (s is null)
		{
			result = null;
			return false;
		}

		try
		{
			result = Parse(s, provider);
			return true;
		}
		catch (FormatException)
		{
			result = null;
			return false;
		}
	}
}