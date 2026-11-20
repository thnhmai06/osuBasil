using System.Text;

namespace Basil.Host.Irc.Support;

internal static class IrcText
{
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

	public static string Limit(string text)
	{
		return text.Length > IrcReplies.MaxMessageLength ? text[..IrcReplies.MaxMessageLength] : text;
	}
}