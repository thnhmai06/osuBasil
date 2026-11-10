using Basil.Domain.Client;
using Basil.Domain.Scores;

namespace Basil.Application.Scores.Storage;

/// <summary>Stores the replays of scores.</summary>
public interface IReplayStorage
{
	/// <summary>Stores the replay of a score, replacing any replay already stored for it.</summary>
	/// <param name="score">The score the replay belongs to.</param>
	/// <param name="clientVersion">The osu! client version that played the score.</param>
	/// <param name="content">The frames of the replay exactly as the client submitted them.</param>
	/// <remarks>The replay is kept as a complete <c>.osr</c> file, which <see cref="OpenAsync" /> returns.</remarks>
	Task SaveAsync(ScoreRecord score, OsuVersion clientVersion, Stream content);

	/// <summary>Opens the stored replay of a score as a complete <c>.osr</c> file.</summary>
	/// <param name="score">The score the replay belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's bytes, or <see langword="null" /> when no replay is stored for the score.</returns>
	Task<Stream?> OpenAsync(ScoreRecord score, CancellationToken cancellationToken = default);

	/// <summary>Opens the frames of the stored replay of a score, as the osu! client submitted them.</summary>
	/// <param name="score">The score the replay belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The frames' bytes, or <see langword="null" /> when no replay is stored for the score or the stored file is
	///     not a readable replay.
	/// </returns>
	Task<Stream?> OpenFramesAsync(ScoreRecord score, CancellationToken cancellationToken = default);
}