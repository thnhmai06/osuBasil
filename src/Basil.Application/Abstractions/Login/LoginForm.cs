using System.Text;
using Basil.Domain.Login;

namespace Basil.Application.Abstractions.Login;

/// <summary>
///     Represents the data an osu! client sends when logging in.
/// </summary>
/// <param name="Username">The username the client sent.</param>
/// <param name="PasswordMd5">The MD5 hash of the password the client sent.</param>
/// <param name="OsuVersion">The version of the client.</param>
/// <param name="UtcOffset">The client's UTC offset, in hours.</param>
/// <param name="DisplayCity">Whether the client allows its city to be displayed.</param>
/// <param name="PmPrivate">Whether the client accepts private messages.</param>
/// <param name="ClientDetails">The client details captured from the login request.</param>
public sealed record LoginForm(
	string Username,
	string PasswordMd5,
	OsuVersion OsuVersion,
	int UtcOffset,
	bool DisplayCity,
	bool PmPrivate,
	ClientDetails ClientDetails)
{
	/// <summary>
	///     Parses the raw login request into a <see cref="LoginForm" />.
	/// </summary>
	/// <param name="data">The UTF-8 encoded login payload from the client.</param>
	/// <returns>The parsed login data.</returns>
	public static LoginForm From(byte[] data)
	{
		var decoded = Encoding.UTF8.GetString(data).TrimEnd('\n');

		var top = decoded.Split('\n', 3);
		var username = top[0];
		var passwordMd5 = top[1];
		var remainder = top[2];

		var fields = remainder.Split('|', 5);
		var osuVersion = OsuVersion.From(fields[0]);
		var utcOffset = int.Parse(fields[1]);
		var displayCity = fields[2] == "1";
		var clientHashes = fields[3];
		var pmPrivate = fields[4] == "1";
		var clientDetails = ClientDetails.From(clientHashes);

		return new LoginForm(username, passwordMd5, osuVersion, utcOffset, displayCity, pmPrivate, clientDetails);
	}
}