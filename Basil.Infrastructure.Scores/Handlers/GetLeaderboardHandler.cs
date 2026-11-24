using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class GetLeaderboardHandler(IBeatmapRepository beatmaps, IScoreRepository scores, IUserRepository users)
	: IRequestHandler<GetLeaderboard, Leaderboard?>
{
	private const int Size = 50;

	public async ValueTask<Leaderboard?> Handle(GetLeaderboard request, CancellationToken ct)
	{
		if (await beatmaps.GetAsync(request.BeatmapHash, ct) is not { } map) return null;

		var page = await scores.ListAsync(
			new ScoreQuery(BeatmapHash: request.BeatmapHash, Mods: request.Mods, BestPerPlayer: true,
				Mode: request.Mode),
			new PageRequest(0, Size), ct);
		var ids = page.Items.Select(score => score.Value.UserId).OfType<int>().Distinct().ToArray();
		var names = (await users.GetManyAsync(ids, ct)).ToDictionary(user => user.Id, user => user.Value.Name);
		var entries = page.Items.Select(score => new LeaderboardEntry(score,
			score.Value.UserId is { } id && names.TryGetValue(id, out var name) ? name : "?")).ToList();

		return new Leaderboard(map, entries);
	}
}