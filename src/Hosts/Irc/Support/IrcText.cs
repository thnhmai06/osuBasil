using System.Text;

namespace Basil.Host.Irc.Support;

/// <summary>Cuts message text into the lines an IRC client can read.</summary>
internal static class IrcText
{
	/// <summary>Splits text at line breaks and cuts long lines, so each piece fits one IRC line.</summary>
	/// <param name="text">The message text, possibly with CR and LF.</param>
	/// <returns>The non-empty pieces, each at most <see cref="IrcReplies.MaxChunkBytes" /> bytes of UTF-8.</returns>
	public static IEnumerable<string> Lines(string text)
	{
		foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
		{
			var piece = new StringBuilder();
			var bytes = 0;
			foreach (var rune in line.EnumerateRunes())
			{
				var size = rune.Utf8SequenceLength;
				if (bytes + size > IrcReplies.MaxChunkBytes)
				{
					yield return piece.ToString();
					piece.Clear();
					bytes = 0;
				}

				piece.Append(rune.ToString());
				bytes += size;
			}

			if (piece.Length > 0) yield return piece.ToString();
		}
	}

	/// <summary>Cuts a chat message to the longest text the server accepts.</summary>
	/// <param name="text">The text the client sent.</param>
	/// <returns>The text, at most <see cref="IrcReplies.MaxMessageLength" /> characters.</returns>
	public static string Limit(string text)
	{
		return text.Length > IrcReplies.MaxMessageLength ? text[..IrcReplies.MaxMessageLength] : text;
	}
}