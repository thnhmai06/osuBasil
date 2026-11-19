using Basil.Shared.Utilities;

namespace Basil.Domain.Beatmaps;

public sealed class BeatmapsetMetadata
{
	public required string Artist
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Artist cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	public required string Title
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Title cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	public required string Creator
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Creator cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	public string FullName => $"{Artist} - {Title} ({Creator})";
}