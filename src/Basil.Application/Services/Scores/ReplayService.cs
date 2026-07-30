using Basil.Application.Abstractions.Scores;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Ported from app/services/replays.py's fetch_replay_file (the raw-serving half only —
///     build_full_replay's header-construction is a separate, unbuilt API v1 concern).
/// </summary>
public enum ReplayFetchResultCode
{
	Found,
	NotFound
}

public sealed record ReplayFetchResult(ReplayFetchResultCode Code, byte[]? Data);

public sealed class ReplayService(
	IScoreRepository scores,
	IReplayStorage replayStorage,
	ILogger<ReplayService> logger)
{
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