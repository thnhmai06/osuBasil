using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Scores.Logic;

/// <summary>Answers the score queries: the leaderboard of a beatmap and the search over scores.</summary>
internal sealed class ScoreQueries(IBeatmapRepository beatmaps, IScoreRepository scores, IUserRepository users)
{
	private const int LeaderboardSize = 50;

	/// <summary>Gets the best scores of a beatmap, one per player, or <see langword="null"/> when the beatmap is unknown.</summary>
	public async Task<Leaderboard?> LeaderboardAsync(GetLeaderboard request, CancellationToken ct)
	{
		if (await beatmaps.GetAsync(request.BeatmapHash, ct) is not { } map) return null;

		var page = await scores.ListAsync(
			new ScoreQuery(BeatmapHash: request.BeatmapHash, Mods: request.Mods, BestPerPlayer: true,
				Mode: request.Mode),
			new PageRequest(0, LeaderboardSize), ct);
		var ids = page.Items.Select(score => score.Value.UserId).OfType<int>().Distinct().ToArray();
		var names = (await users.GetManyAsync(ids, ct)).ToDictionary(user => user.Id, user => user.Value.Name);
		var entries = page.Items.Select(score => new LeaderboardEntry(score,
			score.Value.UserId is { } id && names.TryGetValue(id, out var name) ? name : "?")).ToList();

		return new Leaderboard(map, entries);
	}

	/// <summary>Lists the scores matching a player and a beatmap filter, or says which of the two was not found.</summary>
	public async Task<ScoreListing> SearchAsync(SearchScores request, CancellationToken ct)
	{
		HashSet<Md5Hash>? hashes = null;
		if (request.BeatmapId is { } beatmapId)
		{
			if (await beatmaps.GetAsync(beatmapId, ct) is not { } map)
				return new ScoreListing(ScoreListResult.NoBeatmap, null);
			hashes = [map.Hash];
		}

		if (request.Beatmaps is { } query)
		{
			var found = (await beatmaps.ListAsync(query, new PageRequest(0, ListLimits.Ceiling), ct)).Items
				.Select(beatmap => beatmap.Hash).ToHashSet();
			hashes = hashes is null ? found : [.. hashes.Where(found.Contains)];
		}

		HashSet<int>? userIds = null;
		if (request.UserId is not null || request.UserName is not null)
		{
			var user = request.UserId is { } id
				? await users.GetAsync(id, ct)
				: await users.GetByNameAsync(request.UserName!, ct);
			if (user is null) return new ScoreListing(ScoreListResult.NoUser, null);
			userIds = [user.Id];
		}

		return new ScoreListing(ScoreListResult.Ok,
			await scores.ListAsync(new ScoreQuery(UserIds: userIds, BeatmapHashes: hashes), request.Page, ct));
	}
}