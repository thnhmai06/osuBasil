using System.Text;

namespace Basil.Protocol.Irc;

/// <summary>An IRCv3 message tag: a key with an optional value.</summary>
/// <param name="Key">The tag key; a client-only tag starts with <c>+</c>.</param>
/// <param name="Value">The unescaped value, or <see langword="null" /> when the tag has none.</param>
public sealed record IrcTag(string Key, string? Value = null)
{
	/// <summary>Reads a tag from its wire form <c>key[=value]</c>.</summary>
	/// <param name="text">The tag as it appears between the semicolons of a tag section.</param>
	/// <returns>The tag; an empty value reads as no value.</returns>
	public static IrcTag Parse(string text)
	{
		var equals = text.IndexOf('=');
		if (equals < 0) return new IrcTag(text);

		var value = Unescape(text[(equals + 1)..]);
		return new IrcTag(text[..equals], value.Length == 0 ? null : value);
	}

	/// <summary>Writes the tag in its wire form <c>key[=value]</c>, escaping the value.</summary>
	/// <returns>The wire form.</returns>
	public override string ToString()
	{
		return string.IsNullOrEmpty(Value) ? Key : $"{Key}={Escape(Value)}";
	}

	private static string Escape(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var c in value)
			builder.Append(c switch
			{
				';' => "\\:",
				' ' => "\\s",
				'\\' => "\\\\",
				'\r' => "\\r",
				'\n' => "\\n",
				_ => c.ToString()
			});

		return builder.ToString();
	}

	private static string Unescape(string value)
	{
		var builder = new StringBuilder(value.Length);
		for (var i = 0; i < value.Length; i++)
		{
			if (value[i] != '\\')
			{
				builder.Append(value[i]);
				continue;
			}

			// A lone trailing backslash is dropped; a backslash before any other character is dropped too.
			if (++i == value.Length) break;
			builder.Append(value[i] switch { ':' => ';', 's' => ' ', 'r' => '\r', 'n' => '\n', var other => other });
		}

		return builder.ToString();
	}
}
