using System.Text;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Spells text with ASCII characters only, for a terminal started with <c>--ascii</c>.</summary>
internal static class AsciiText
{
	public static string Of(string text)
	{
		if (text.All(char.IsAscii)) return text;

		var result = new StringBuilder(text.Length);
		foreach (var character in text)
			result.Append(character switch
			{
				'…' => "...",
				'×' => "x",
				'←' => "<",
				'→' => ">",
				'↑' => "^",
				'↓' => "v",
				'–' or '—' => "-",
				'›' => ">",
				'•' => "*",
				'µ' => "u",
				'·' => "|",
				'✓' => "+",
				'✗' => "x",
				'❯' => ">",
				'⌕' => "?",
				'↻' => "@",
				'▾' => "v",
				'▸' => ">",
				'█' => "#",
				'▌' => ">",
				'\u0332' => string.Empty,
				_ => char.IsAscii(character) ? character.ToString() : "?"
			});

		return result.ToString();
	}
}
