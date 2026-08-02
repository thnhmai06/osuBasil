using Basil.Domain.Login;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Scores;

/// <summary>
///     Reads and writes full <c>.osr</c> replay files on disk, keyed by score id.
/// </summary>
/// <remarks>
///     Each replay is a single complete <c>.osr</c> file keyed by score id: the implementation builds
///     the <c>.osr</c> header (mode, version, beatmap/player/replay md5, hit counts, score, combo,
///     mods, timestamp, online score id) around the raw LZMA replay bytes the client uploaded. Score
///     submission writes a replay, and the replay-download route reads one back.
/// </remarks>
public interface IReplayStorage
{
	/// <summary>
	///     Writes the given replay for a score as a complete <c>.osr</c> file.
	/// </summary>
	/// <param name="scoreId">The id of the score the replay belongs to.</param>
	/// <param name="score">The submitted score whose stats the <c>.osr</c> header records.</param>
	/// <param name="playerName">The name of the player who submitted the score.</param>
	/// <param name="osuVersion">The game version the replay belongs to.</param>
	/// <param name="replayData">The raw LZMA replay bytes from the client's submission.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task WriteAsync(long scoreId, Submission score, string playerName, OsuVersion osuVersion, byte[] replayData,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Reads the stored replay for a score.
	/// </summary>
	/// <param name="scoreId">The id of the score whose replay to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The full <c>.osr</c> bytes, or <see langword="null" /> when no replay is stored.</returns>
	Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default);
}