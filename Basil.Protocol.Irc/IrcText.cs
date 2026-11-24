using System.Text;

namespace Basil.Protocol.Irc;

/// <summary>Fits text into IRC lines.</summary>
public static class IrcText
{
	/// <summary>Splits a text into lines of at most <see cref="IrcWire.MaxChunkBytes"/> bytes without cutting a character.</summary>
	public static IEnumerable<string> Lines(string text)
	{
		foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
		{
			var piece = new StringBuilder();
			var bytes = 0;
			foreach (var rune in line.EnumerateRunes())
			{
				var size = rune.Utf8SequenceLength;
				if (bytes + size > IrcWire.MaxChunkBytes)
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

	/// <summary>Cuts a chat message to <see cref="IrcWire.MaxMessageLength"/> characters.</summary>
	public static string Limit(string text)
	{
		return text.Length > IrcWire.MaxMessageLength ? text[..IrcWire.MaxMessageLength] : text;
	}

	/// <summary>Packs entries, such as the names of a channel, into space-separated lines of at most <see cref="IrcWire.MaxChunkBytes"/> characters.</summary>
	public static IEnumerable<string> Pack(IEnumerable<string> entries)
	{
		var line = new StringBuilder();
		foreach (var entry in entries)
		{
			if (line.Length > 0 && line.Length + entry.Length > IrcWire.MaxChunkBytes)
			{
				yield return line.ToString();
				line.Clear();
			}

			if (line.Length > 0) line.Append(' ');
			line.Append(entry);
		}

		if (line.Length > 0) yield return line.ToString();
	}
}
