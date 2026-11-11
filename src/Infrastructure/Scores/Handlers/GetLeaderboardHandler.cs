using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Builds the leaderboard of a beatmap for <see cref="GetLeaderboard" />.</summary>
internal sealed class GetLeaderboardHandler(IBeatmapRepository beatmaps, IScoreRepository scores, IUserRepository users)
	: IRequestHandler<GetLeaderboard, Leaderboard?>
{
	private const int Size = 50;

	/// <inheritdoc />
	public async ValueTask<Leaderboard?> Handle(GetLeaderboard request, CancellationToken ct)
	{
		if (await beatmaps.GetAsync(request.BeatmapHash, ct) is not { } map) return null;

		var page = await scores.ListAsync(
			new ScoreQuery(BeatmapHash: request.BeatmapHash, Mods: request.Mods, BestPerPlayer: true),
			new PageRequest(0, Size), ct);
		var entries = new List<LeaderboardEntry>();
		foreach (var score in page.Items.Where(score => score.Value.Mode == request.Mode))
		{
			var name = score.Value.UserId is { } id && await users.GetAsync(id, ct) is { } user
				? user.Value.Name
				: "?";
			entries.Add(new LeaderboardEntry(score, name));
		}

		return new Leaderboard(map, entries);
	}
}