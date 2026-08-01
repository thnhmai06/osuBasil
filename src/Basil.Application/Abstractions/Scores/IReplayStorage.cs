namespace Basil.Application.Abstractions.Scores;

/// <summary>
///     Reads and writes raw replay files on disk, keyed by score id.
/// </summary>
/// <remarks>
///     Each replay is a single raw <c>.osr</c> file keyed by score id, holding exactly the bytes the
///     osu! client uploaded. No replay-header construction happens here; that is a separate concern
///     that is not built. Score submission writes a replay, and the replay-download route reads one
///     back.
/// </remarks>
public interface IReplayStorage
{
	/// <summary>
	///     Writes the given replay data for a score.
	/// </summary>
	/// <param name="scoreId">The id of the score the replay belongs to.</param>
	/// <param name="data">The raw replay bytes to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task WriteAsync(long scoreId, byte[] data, CancellationToken cancellationToken = default);

	/// <summary>
	///     Reads the stored replay for a score.
	/// </summary>
	/// <param name="scoreId">The id of the score whose replay to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The raw replay bytes, or <see langword="null" /> when no replay is stored.</returns>
	Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default);
}