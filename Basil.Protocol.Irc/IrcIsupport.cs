namespace Basil.Protocol.Irc;

/// <summary>Writes the <c>ISUPPORT</c> tokens a server announces.</summary>
public static class IrcIsupport
{
	/// <summary>The tokens a server announces.</summary>
	/// <param name="network">The network name.</param>
	/// <param name="nickLength">The longest nickname the server takes.</param>
	public static string[] Tokens(string network, int nickLength)
	{
		return
		[
			"CASEMAPPING=ascii", "CHANTYPES=#", "PREFIX=(ov)@+", "CHANMODES=,,,", $"NETWORK={network}",
			$"NICKLEN={nickLength}", "MODES=1", "MAXTARGETS=1", $"LINELEN={IrcWire.MaxBodyBytes}"
		];
	}
}
