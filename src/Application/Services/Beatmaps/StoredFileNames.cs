using System.Text;
using System.Text.RegularExpressions;
using Basil.Application.Storage.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>Chooses the names the files of a beatmapset are stored under.</summary>
internal static partial class StoredFileNames
{
	/// <summary>The name passed when a storyboard file is stored or looked for; its kind decides where it lies.</summary>
	public const string Storyboard = "storyboard.osb";

	/// <summary>The names Windows keeps for devices, whatever their extension.</summary>
	private static readonly Regex ReservedName = ReservedPattern();

	/// <summary>Chooses the plain name each file of one kind is stored under.</summary>
	/// <param name="files">The files of one kind, in difficulty order; a file used by several difficulties comes once.</param>
	/// <returns>One chosen name per file; two different files never get the same name.</returns>
	public static Dictionary<BeatmapsetContentFile, string> ChooseNames(IEnumerable<BeatmapsetContentFile?> files)
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

	/// <summary>A file's plain name, with every character a file name may not have replaced.</summary>
	/// <param name="name">The file's name relative to the set.</param>
	/// <returns>The plain name, safe to store a file under; a name left empty by the replacement is <c>file</c>.</returns>
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