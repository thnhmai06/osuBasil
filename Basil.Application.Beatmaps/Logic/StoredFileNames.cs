using System.Text;
using System.Text.RegularExpressions;
using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Logic;

internal static partial class StoredFileNames
{
	public const string Storyboard = "storyboard.osb";

	private static readonly Regex ReservedName = ReservedPattern();

	public static IReadOnlyDictionary<BeatmapsetContentFile, string> ChooseNames(
		IEnumerable<BeatmapsetContentFile?> files)
	{
		var names = new Dictionary<BeatmapsetContentFile, string>();
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in files)
		{
			if (file is null || names.ContainsKey(file)) continue;

			var plain = PlainName(file.Name);
			var stem = Path.GetFileNameWithoutExtension(plain);
			var extension = Path.GetExtension(plain);
			var name = plain;
			for (var suffix = 2; !taken.Add(name); suffix++)
				name = $"{stem}-{suffix}{extension}";

			names[file] = name;
		}

		return names;
	}

	public static string PlainName(string name)
	{
		var plain = Path.GetFileName(name);
		var builder = new StringBuilder(plain.Length);
		foreach (var character in plain)
			builder.Append(character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' or < ' '
				? '_'
				: character);

		// Windows drops trailing dots and spaces and keeps device names such as CON or NUL.txt for itself.
		var result = builder.ToString().TrimEnd('.', ' ');
		if (result.Length == 0) return "file";
		var dot = result.IndexOf('.');
		return ReservedName.IsMatch(dot < 0 ? result : result[..dot]) ? "_" + result : result;
	}

	[GeneratedRegex(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³]) *$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ReservedPattern();
}