namespace Basil.Application.Contracts.Beatmaps;

/// <summary>Reads what a beatmapset archive contains.</summary>
public interface IBeatmapArchiveReader
{
	/// <summary>Reads a beatmapset archive.</summary>
	/// <param name="archive">The archive content.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>What the archive contains, or <see langword="null" /> when it is not a readable beatmapset archive.</returns>
	Task<BeatmapArchive?> ReadAsync(Stream archive, CancellationToken cancellationToken = default);
}

/// <summary>What a beatmapset archive contains.</summary>
/// <param name="OnlineSetId">
///     The beatmapset id the difficulties declare, or <see langword="null" /> when they declare
///     none.
/// </param>
/// <param name="Artist">The artist.</param>
/// <param name="Title">The title.</param>
/// <param name="Creator">The mapper's name.</param>
/// <param name="Difficulties">The difficulties.</param>
public sealed record BeatmapArchive(
	int? OnlineSetId,
	string Artist,
	string Title,
	string Creator,
	IReadOnlyList<BeatmapArchiveDifficulty> Difficulties);

/// <summary>One difficulty of a beatmapset archive.</summary>
/// <param name="OnlineId">The beatmap id the difficulty declares, or <see langword="null" /> when it declares none.</param>
/// <param name="Version">The difficulty name.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Content">The difficulty file's bytes.</param>
public sealed record BeatmapArchiveDifficulty(
	int? OnlineId,
	string Version,
	GameMode Mode,
	byte[] Content);