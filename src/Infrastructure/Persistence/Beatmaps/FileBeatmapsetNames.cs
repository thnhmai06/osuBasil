using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Common.Infrastructure.Files;

namespace Basil.Infrastructure.Persistence.Beatmaps;

/// <summary>The names the beatmapset storage gives to folders and files, and how it reads them back.</summary>
internal static class FileBeatmapsetNames
{
	/// <summary>
	///     The marker a top-level name starts with while it is being written or set aside: an upload, a file, a set
	///     folder before it is moved into place or the previous folder while its replacement is moved in. Such a name is never
	///     a set, an entry, or watched.
	/// </summary>
	internal const string IncomingMarker = ".incoming_";

	internal const string VersionFolder = "versions";
	internal const string BackgroundFolder = "backgrounds";
	internal const string AudioFolder = "audio";
	internal const string VideoFolder = "video";

	/// <summary>The name of a set's archive, stored byte for byte as it was given.</summary>
	internal const string ArchiveName = "archive.osz";

	/// <summary>The name of the archive built from <see cref="ArchiveName" /> without its video entries.</summary>
	internal const string ArchiveWithoutVideoName = "archive-novideo.osz";

	/// <summary>The name of the set's storyboard file, stored in the set folder itself.</summary>
	internal const string StoryboardName = "storyboard.osb";

	/// <summary>The prefix of an audio preview file, stored in the audio folder.</summary>
	internal const string PreviewPrefix = "preview_";

	/// <summary>
	///     The most bytes a set folder's name may take: well within the 255 of common file systems.
	/// </summary>
	private const int MaxFolderNameBytes = 150;

	private static readonly FrozenSet<char> InvalidNameChars = "\"<>:/\\|?*".ToFrozenSet();

	/// <summary>The names Windows keeps for devices, which no stored file or folder may take, whatever its extension.</summary>
	private static readonly FrozenSet<string> ReservedNames = new[]
	{
		"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
		"COM8", "COM9", "COM¹", "COM²", "COM³", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8",
		"LPT9", "LPT¹", "LPT²", "LPT³"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>Determines whether a top-level name is hidden from listing and watching.</summary>
	/// <param name="name">The name.</param>
	/// <returns><see langword="true" /> while the name is being written or set aside.</returns>
	internal static bool IsHidden(string name)
	{
		return name.StartsWith(IncomingMarker, StringComparison.Ordinal);
	}

	/// <summary>Determines whether a name is that of a beatmapset archive.</summary>
	/// <param name="name">The file name or path.</param>
	/// <returns><see langword="true" /> for an .osz archive.</returns>
	internal static bool IsArchiveName(string name)
	{
		return name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Determines whether a file name is one Windows keeps for a device, whatever its extension.</summary>
	/// <param name="name">The file name.</param>
	/// <returns><see langword="true" /> for names such as <c>CON</c>, <c>nul.txt</c> or <c>COM1</c>.</returns>
	internal static bool IsReservedName(string name)
	{
		var dot = name.IndexOf('.');
		return ReservedNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
	}

	/// <summary>Reads the beatmapset id a folder or file name starts with.</summary>
	/// <param name="name">The name to read.</param>
	/// <returns>
	///     The id formed by the leading digits when they are followed by the end of the name or a space; otherwise,
	///     <see langword="null" />.
	/// </returns>
	internal static int? LeadingId(string name)
	{
		var digits = 0;
		while (digits < name.Length && char.IsAsciiDigit(name[digits])) digits++;
		if (digits == 0 || (digits < name.Length && name[digits] != ' ')) return null;
		return int.TryParse(name.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
			? id
			: null;
	}

	/// <summary>
	///     Builds the canonical name of a set's folder: its id, artist and title as osu! names its songs, without the
	///     characters a folder name may not have.
	/// </summary>
	/// <param name="set">The beatmapset the folder holds.</param>
	/// <returns>The folder's name.</returns>
	internal static string FolderName(BeatmapsetRecord set)
	{
		var name = $"{set.Id} {set.Metadata.Artist} - {set.Metadata.Title}";
		var builder = new StringBuilder(name.Length);
		foreach (var character in name.Where(character =>
			         !char.IsControl(character) && !InvalidNameChars.Contains(character)))
			builder.Append(character);

		return FileNames.Fitted(builder.ToString().TrimEnd('.', ' '), MaxFolderNameBytes);
	}

	/// <summary>Builds the path of one file made from a set's archive.</summary>
	/// <param name="setFolder">The set folder's full path.</param>
	/// <param name="type">What the file is.</param>
	/// <param name="name">The plain file name to store it under.</param>
	/// <returns>The file's full path.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a plain file name.</exception>
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