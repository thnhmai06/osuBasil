namespace Basil.Protocol.Irc;

/// <summary>The IRCv3 capability negotiation and SASL encodings.</summary>
public static class IrcCapabilities
{
	/// <summary>The SASL mechanism the server speaks.</summary>
	public const string SaslMechanism = "PLAIN";

	/// <summary>The capability name of SASL.</summary>
	public const string Sasl = "sasl";

	/// <summary>The longest <c>AUTHENTICATE</c> chunk; a chunk of exactly this length says more follows.</summary>
	public const int AuthenticateChunk = 400;

	private const int ValuesSince = 302;

	/// <summary>Writes the capability list of a <c>CAP LS</c> answer.</summary>
	/// <param name="capabilities">The capabilities on offer.</param>
	/// <param name="version">The version the client gave with <c>LS</c>; from 302 on, capabilities carry their values.</param>
	public static string Offer(IEnumerable<string> capabilities, int version)
	{
		return string.Join(' ', capabilities.Select(cap =>
			cap == Sasl && version >= ValuesSince ? $"{Sasl}={SaslMechanism}" : cap));
	}

	/// <summary>Decodes the data of a SASL <c>PLAIN</c> exchange.</summary>
	/// <param name="payload">The base64 text the client sent, chunks joined.</param>
	/// <param name="account">The account name it carried.</param>
	/// <param name="key">The password it carried.</param>
	/// <returns><see langword="true"/> when the data is well formed and names an account and a password.</returns>
	public static bool TryDecodePlain(string payload, out string account, out string key)
	{
		account = key = string.Empty;
		try
		{
			var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
			if (parts.Length != 3) return false;
			(account, key) = (parts[1], parts[2]);
			return account.Length > 0 && key.Length > 0;
		}
		catch (FormatException)
		{
			return false;
		}
	}
}
