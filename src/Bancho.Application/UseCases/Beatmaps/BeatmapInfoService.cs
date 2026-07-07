using Bancho.Application.Abstractions;
using Bancho.Domain;
using Bancho.Application.Abstractions.Beatmaps;
using Bancho.Application.Abstractions.Scores;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Scores;

namespace Bancho.Application.UseCases.Beatmaps;

/// <summary>Ported from app/services/maps.py's BeatmapInfo.</summary>
public sealed record BeatmapInfoRow(int Index, int Id, int SetId, string Md5, int Status, IReadOnlyList<string> Grades);

/// <summary>
/// Ported from app/services/maps.py's BeatmapInfoService.fetch_beatmap_info, backing
/// osu-getbeatmapinfo.php — grades reflect the requesting player's best score per map, in their
/// currently-selected vanilla mode only (osu! only allows sending one grade per gamemode slot).
/// </summary>
public sealed class BeatmapInfoService(IMapRepository maps, IScoreRepository scores)
{
    private static readonly Dictionary<RankedStatus, int> BanchoToOsuApiStatus = new()
    {
        [RankedStatus.Pending] = 0,
        [RankedStatus.Ranked] = 1,
        [RankedStatus.Approved] = 2,
        [RankedStatus.Qualified] = 3,
        [RankedStatus.Loved] = 4,
    };

    public async Task<IReadOnlyList<BeatmapInfoRow>> FetchBeatmapInfoAsync(
        IReadOnlyList<string> filenames, int playerId, GameMode vanillaMode, CancellationToken cancellationToken = default)
    {
        var result = new List<BeatmapInfoRow>();

        for (var index = 0; index < filenames.Count; index++)
        {
            var beatmap = await maps.FetchOneAsync(filename: filenames[index], cancellationToken: cancellationToken);
            if (beatmap is null)
            {
                continue;
            }

            var grades = new[] { "N", "N", "N", "N" };
            var best = await scores.FetchPersonalBestLeaderboardScoreAsync(beatmap.Md5, vanillaMode, playerId, cancellationToken);
            if (best is not null)
            {
                grades[(int)vanillaMode] = best.Grade;
            }

            result.Add(new BeatmapInfoRow(index, beatmap.Id, beatmap.SetId, beatmap.Md5, BanchoToOsuApiStatus[beatmap.Status], grades));
        }

        return result;
    }
}
