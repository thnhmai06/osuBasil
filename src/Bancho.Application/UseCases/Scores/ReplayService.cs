using Bancho.Application.Abstractions;
using Bancho.Application.Abstractions.Scores;
using Bancho.Application.Abstractions.Users;

namespace Bancho.Application.UseCases.Scores;

/// <summary>
/// Ported from app/services/replays.py's fetch_replay_file (the raw-serving half only —
/// build_full_replay's header-construction is a separate, unbuilt API v1 concern).
/// </summary>
public enum ReplayFetchResultCode
{
    Found,
    NotFound,
}

public sealed record ReplayFetchResult(ReplayFetchResultCode Code, byte[]? Data);

public sealed class ReplayService(IScoreRepository scores, IReplayStorage replayStorage, IStatsRepository stats)
{
    public async Task<ReplayFetchResult> FetchReplayFileAsync(long scoreId, int viewerId, CancellationToken cancellationToken = default)
    {
        var owner = await scores.FetchOwnerAsync(scoreId, cancellationToken);
        if (owner is null)
        {
            return new ReplayFetchResult(ReplayFetchResultCode.NotFound, null);
        }

        var data = await replayStorage.ReadAsync(scoreId, cancellationToken);
        if (data is null)
        {
            return new ReplayFetchResult(ReplayFetchResultCode.NotFound, null);
        }

        if (viewerId != owner.UserId)
        {
            await stats.IncrementReplayViewsAsync(owner.UserId, (int)owner.Mode, cancellationToken);
        }

        return new ReplayFetchResult(ReplayFetchResultCode.Found, data);
    }
}
