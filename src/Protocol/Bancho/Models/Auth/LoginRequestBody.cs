using System.Globalization;
using System.Text;

namespace Basil.Protocol.Bancho.Models.Auth;

/// <summary>
///     The body of a bancho login request: the username and password hash on the first two lines, then
///     <c>osuversion|utc_offset|display_city|client_hash|pm_private</c> on the third.
/// </summary>
/// <param name="Username">The username the client sent.</param>
/// <param name="PasswordMd5">The md5 hex digest of the password the client sent.</param>
/// <param name="OsuVersion">The client version text, parseable with <see cref="ClientBuild" />.</param>
/// <param name="UtcOffset">The client's UTC offset in hours.</param>
/// <param name="DisplayCity">Whether the client asks to show the user's city.</param>
/// <param name="ClientHash">The client hash fields.</param>
/// <param name="BlockNonFriendDms">Whether the client asks to block direct messages from non-friends.</param>
public sealed record LoginRequestBody(
	string Username,
	string PasswordMd5,
	string OsuVersion,
	int UtcOffset,
	bool DisplayCity,
	ClientHash ClientHash,
	bool BlockNonFriendDms)
{
	/// <summary>Parses a login request body from its UTF-8 bytes.</summary>
	/// <param name="body">The raw request body.</param>
	/// <returns>The fields of the body.</returns>
	/// <exception cref="FormatException">The body is not valid UTF-8 or does not have the login layout.</exception>
	public static LoginRequestBody Parse(ReadOnlySpan<byte> body)
	{
		try
		{
			return Parse(new UTF8Encoding(false, true).GetString(body));
		}
		catch (DecoderFallbackException e)
		{
			throw new FormatException("The login body is not valid UTF-8.", e);
		}
	}

	/// <summary>Parses a login request body.</summary>
	/// <param name="body">The request body text; trailing line feeds are ignored.</param>
	/// <returns>The fields of the body.</returns>
	/// <exception cref="FormatException">The body does not have the login layout.</exception>
	public static LoginRequestBody Parse(string body)
	{
		var lines = body.TrimEnd('\n').Split('\n', 3);
		if (lines.Length != 3) throw new FormatException("The login body needs three lines.");

		var fields = lines[2].Split('|', 5);
		if (fields.Length != 5) throw new FormatException("The login body's third line needs five fields.");

		if (!int.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var utcOffset))
			throw new FormatException("The login body's UTC offset is not an integer.");

		return new LoginRequestBody(lines[0], lines[1], fields[0], utcOffset, fields[2] == "1",
			ClientHash.Parse(fields[3]), fields[4] == "1");
	}
}