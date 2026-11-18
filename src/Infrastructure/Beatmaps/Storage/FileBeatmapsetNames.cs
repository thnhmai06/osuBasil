using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;

using Basil.Domain.Mechanics;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Beatmaps.Storage;

internal static class FileBeatmapsetNames
{
	internal const string IncomingMarker = ".incoming_";

	internal const string VersionFolder = "versions";
	internal const string BackgroundFolder = "backgrounds";
	internal const string AudioFolder = "audio";
	internal const string VideoFolder = "video";

	internal const string AttributesFolder = "attributes";

	internal const string ArchiveName = "archive.osz";

	internal const string ArchiveWithoutVideoName = "archive-novideo.osz";

	internal const string StoryboardName = "storyboard.osb";

	internal const string PreviewPrefix = "preview_";

	private const int MaxFolderNameBytes = 150;

	private static readonly FrozenSet<char> InvalidNameChars = "\"<>:/\\|?*".ToFrozenSet();

	private static readonly FrozenSet<string> ReservedNames = new[]
	{
		"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
		"COM8", "COM9", "COM¹", "COM²", "COM³", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8",
		"LPT9", "LPT¹", "LPT²", "LPT³"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	internal static bool IsHidden(string name)
	{
		return name.StartsWith(IncomingMarker, StringComparison.Ordinal);
	}

	internal static bool IsArchiveName(string name)
	{
		return name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool IsReservedName(string name)
	{
		var dot = name.IndexOf('.');
		return ReservedNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
	}

	internal static int? LeadingId(string name)
	{
		var digits = 0;
		while (digits < name.Length && char.IsAsciiDigit(name[digits])) digits++;
		if (digits == 0 || (digits < name.Length && name[digits] != ' ')) return null;
		return int.TryParse(name.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
			? id
			: null;
	}

	internal static string FolderName(BeatmapsetRecord set)
	{
		var name = $"{set.Id} {set.Metadata.Artist} - {set.Metadata.Title}";
		var builder = new StringBuilder(name.Length);
		foreach (var character in name.Where(character =>
			         !char.IsControl(character) && !InvalidNameChars.Contains(character)))
			builder.Append(character);

		return FileNames.Fitted(builder.ToString().TrimEnd('.', ' '), MaxFolderNameBytes);
	}

	internal static string AttributesPath(string setFolder, Md5 hash, GameMode mode, GameMods mask)
	{
		return Path.Combine(setFolder, AttributesFolder, hash.HashValue,
			((int)mode).ToString(CultureInfo.InvariantCulture),
			$"{(int)mask}.json");
	}

	internal static string PathOf(string setFolder, BeatmapsetFileType type, string name)
	{
		// The storyboard has one fixed name, so the name given with it means nothing.
		if (type == BeatmapsetFileType.Storyboard)
			return Path.Combine(setFolder, StoryboardName);

		if (string.IsNullOrEmpty(name))
			throw new ArgumentException("The file name cannot be empty.", nameof(name));
		if (name != Path.GetFileName(name))
			throw new ArgumentException("The file name must not contain a path.", nameof(name));
		if (name.Any(char.IsControl) || name.Any(InvalidNameChars.Contains))
			throw new ArgumentException("The file name contains invalid characters.", nameof(name));
		if (IsReservedName(name) || name.EndsWith('.') || name.EndsWith(' '))
			throw new ArgumentException("The file name is not one a file may have.", nameof(name));

		return type switch
		{
			BeatmapsetFileType.Preview => Path.Combine(setFolder, AudioFolder, PreviewPrefix + name),
			BeatmapsetFileType.Version => Path.Combine(setFolder, VersionFolder, name),
			BeatmapsetFileType.Background => Path.Combine(setFolder, BackgroundFolder, name),
			BeatmapsetFileType.Audio => Path.Combine(setFolder, AudioFolder, name),
			BeatmapsetFileType.Video => Path.Combine(setFolder, VideoFolder, name),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		};
	}
}