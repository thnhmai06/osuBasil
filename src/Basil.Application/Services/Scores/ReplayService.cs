using Basil.Application.Abstractions.Scores;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Identifies the outcome of a replay-file fetch.
/// </summary>
/// <remarks>
///     Carried by <see cref="ReplayFetchResult" /> to tell the caller whether replay bytes were
///     produced. The <see cref="NotFound" /> result covers both a score id that does not exist and
///     a score with no stored replay file.
/// </remarks>
public enum ReplayFetchResultCode
{
	/// <summary>A replay file was found and returned.</summary>
	Found,

	/// <summary>The score has no stored replay file.</summary>
	NotFound
}

/// <summary>
///     The result of a replay-file fetch.
/// </summary>
/// <param name="Code">The outcome of the fetch.</param>
/// <param name="Data">The raw replay bytes, or <see langword="null" /> when no file was found.</param>
public sealed record ReplayFetchResult(ReplayFetchResultCode Code, byte[]? Data);

/// <summary>
///     Serves stored replay files to the osu! client.
/// </summary>
/// <remarks>
///     This covers the raw-serving half of replay handling: given a score id it returns the stored
///     replay bytes verbatim. Constructing the "full" replay header that a replay-download API
///     would prepend is not part of Basil's scope.
/// </remarks>
public sealed class ReplayService(
	IScoreRepository scores,
	IReplayStorage replayStorage,
	ILogger<ReplayService> logger)
{
	/// <summary>
	///     Fetches the stored replay file for a score.
	/// </summary>
	/// <param name="scoreId">The id of the score whose replay file to read.</param>
	/// <param name="cancellationToken">A token that cancels the lookup.</param>
	/// <returns>
	///     A <see cref="ReplayFetchResult" /> with <see cref="ReplayFetchResultCode.Found" /> and the
	///     raw replay bytes when the score exists and has a stored file, or
	///     <see cref="ReplayFetchResultCode.NotFound" /> with a <see langword="null" /> payload
	///     otherwise.
	/// </returns>
	/// <remarks>
	///     The score is resolved through its owner first, so an unknown score id and a score without
	///     a stored replay are reported identically.
	/// </remarks>
	public async Task<ReplayFetchResult> FetchReplayFileAsync(long scoreId,
		CancellationToken cancellationToken = default)
	{
		var owner = await scores.FetchOwnerAsync(scoreId, cancellationToken);
		if (owner is null)
		{
			logger.LogDebug("Replay not found: ScoreId={ScoreId} (no owner)", scoreId);
			return new ReplayFetchResult(ReplayFetchResultCode.NotFound, null);
		}

		var data = await replayStorage.ReadAsync(scoreId, cancellationToken);
		if (data is null) logger.LogDebug("Replay not found: ScoreId={ScoreId} (no file)", scoreId);

		return data is not null
			? new ReplayFetchResult(ReplayFetchResultCode.Found, data)
			: new ReplayFetchResult(ReplayFetchResultCode.NotFound, null);
	}
}