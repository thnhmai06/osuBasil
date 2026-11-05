using System.Globalization;
using System.Text;
using Basil.Domain.Client;
using Basil.Protocol.Stable.Models.Client;

namespace Basil.Protocol.Stable.Models.Authentication;

/// <summary>
///     The body of a stable login request: the username and password hash on the first two lines, then
///     <c>osuversion|utc_offset|display_city|client_hash|pm_private</c> on the third.
/// </summary>
/// <param name="Username">The username the client sent.</param>
/// <param name="PasswordMd5">The md5 hex digest of the password the client sent.</param>
/// <param name="OsuVersion">The client version.</param>
/// <param name="UtcOffset">The client's UTC offset in hours.</param>
/// <param name="DisplayCity">Whether the client asks to show the user's city.</param>
/// <param name="Fingerprint">The client fingerprint.</param>
/// <param name="BlockNonFriendDms">Whether the client asks to block direct messages from non-friends.</param>
public sealed record LoginRequest(
	string Username,
	string PasswordMd5,
	OsuVersion OsuVersion,
	int UtcOffset,
	bool DisplayCity,
	OsuFingerprint Fingerprint,
	bool BlockNonFriendDms)
{
	/// <summary>Parses a login request body from its UTF-8 bytes.</summary>
	/// <param name="body">The raw request body.</param>
	/// <returns>The fields of the body.</returns>
	/// <exception cref="FormatException">The body is not valid UTF-8 or does not have the login layout.</exception>
	public static LoginRequest Parse(ReadOnlySpan<byte> body)
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
	public static LoginRequest Parse(string body)
	{
		var lines = body.TrimEnd('\n').Split('\n', 3);
		if (lines.Length != 3) throw new FormatException("The login body needs three lines.");

		var fields = lines[2].Split('|', 5);
		if (fields.Length != 5) throw new FormatException("The login body's third line needs five fields.");

		if (!int.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var utcOffset))
			throw new FormatException("The login body's UTC offset is not an integer.");

		return new LoginRequest(lines[0], lines[1], OsuVersionExtensions.Parse(fields[0]), utcOffset, fields[2] == "1",
			OsuFingerprintExtensions.Parse(fields[3]), fields[4] == "1");
	}
}