using Basil.Shared.Utilities;

namespace Basil.Domain.Beatmaps;

/// <summary>The artist, title and mapper that name a beatmapset.</summary>
public sealed class BeatmapsetMetadata
{
	/// <summary>The artist of the song; neither empty nor containing a NUL character.</summary>
	public required string Artist
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Artist cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The title of the song; neither empty nor containing a NUL character.</summary>
	public required string Title
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Title cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The name of the mapper; neither empty nor containing a NUL character.</summary>
	public required string Creator
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Creator cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The set's name as "Artist - Title (Creator)".</summary>
	public string FullName => $"{Artist} - {Title} ({Creator})";
}