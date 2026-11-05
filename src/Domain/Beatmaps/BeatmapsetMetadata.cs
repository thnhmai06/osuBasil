using Basil.Domain.Common;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     What every difficulty of a beatmapset shares: the song and its mapper.
/// </summary>
public sealed class BeatmapsetMetadata
{
	/// <summary>The artist of the set's music.</summary>
	public required string Artist
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Artist cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The title of the set's music.</summary>
	public required string Title
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Title cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The name of the beatmapset's mapper.</summary>
	public required string Creator
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Creator cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>Gets the display name of the set.</summary>
	/// <value>An "Artist - Title" string.</value>
	public string Name => $"{Artist} - {Title}";
}