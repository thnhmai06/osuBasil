namespace OpenOsuTournament.Bancho.Application.Abstractions.Scores;

/// <summary>
///     Ported from app/services/score_submission.py's write_replay_file and
///     app/services/replays.py's fetch_replay_file's disk-read half. Each replay is a single raw
///     `.osr` file keyed by score id — no header construction (that's a separate, unbuilt API v1
///     concern), just the bytes the osu! client itself uploaded.
/// </summary>
public interface IReplayStorage
{
    Task WriteAsync(long scoreId, byte[] data, CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default);
}