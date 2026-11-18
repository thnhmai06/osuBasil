using System.Text;

namespace Basil.Protocol.Irc.Models;

public sealed record IrcTag(string Key, string? Value = null)
{
	public static IrcTag Parse(string text)
	{
		var equals = text.IndexOf('=');
		if (equals < 0) return new IrcTag(text);

		var value = Unescape(text[(equals + 1)..]);
		return new IrcTag(text[..equals], value.Length == 0 ? null : value);
	}

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
				'\\' => @"\\",
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