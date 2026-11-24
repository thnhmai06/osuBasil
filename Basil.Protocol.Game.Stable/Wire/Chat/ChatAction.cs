namespace Basil.Protocol.Game.Stable.Wire.Chat;

/// <summary>Reads and writes the CTCP <c>ACTION</c> framing the osu! client uses for "/me" messages.</summary>
public static class ChatAction
{
	private const char Marker = '\u0001';

	private const string Prefix = "\u0001ACTION ";

	/// <summary>Reads the text of an action message.</summary>
	/// <param name="text">The message text as it came over the wire.</param>
	/// <param name="body">The text without the framing.</param>
	/// <returns><see langword="true"/> when <paramref name="text"/> is an action.</returns>
	public static bool TryRead(string text, out string body)
	{
		if (!text.StartsWith(Prefix, StringComparison.Ordinal))
		{
			body = text;
			return false;
		}

		body = text[Prefix.Length..].TrimEnd(Marker);
		return true;
	}

	/// <summary>Frames the text of an action message for the wire.</summary>
	/// <param name="body">The text of the action.</param>
	public static string Write(string body)
	{
		return Prefix + body + Marker;
	}
}
