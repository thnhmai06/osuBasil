using System.Globalization;
using System.Text;
using Basil.Domain.Client;
using Basil.Protocol.Game.Stable.Models.Client;

namespace Basil.Protocol.Game.Stable.Models.Authentication;

public sealed record LoginRequest(
	string Username,
	string PasswordMd5,
	OsuVersion OsuVersion,
	int UtcOffset,
	bool DisplayCity,
	ClientFingerprint Fingerprint,
	bool BlockNonFriendDms)
{
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

	public static LoginRequest Parse(string body)
	{
		var lines = body.TrimEnd('\n').Split('\n', 3);
		if (lines.Length != 3) throw new FormatException("The login body needs three lines.");

		var fields = lines[2].Split('|', 5);
		if (fields.Length != 5) throw new FormatException("The login body's third line needs five fields.");

		if (!int.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var utcOffset))
			throw new FormatException("The login body's UTC offset is not an integer.");

		return new LoginRequest(lines[0], lines[1], OsuVersionExtensions.Parse(fields[0]), utcOffset, fields[2] == "1",
			ClientFingerprintExtensions.Parse(fields[3]), fields[4] == "1");
	}
}