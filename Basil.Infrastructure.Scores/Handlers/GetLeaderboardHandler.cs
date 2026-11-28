using Basil.Application.Scores.Logic;
using Basil.Application.Scores.Messages;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class GetLeaderboardHandler(ScoreQueries queries) : IRequestHandler<GetLeaderboard, Leaderboard?>
{
	public async ValueTask<Leaderboard?> Handle(GetLeaderboard request, CancellationToken ct)
	{
		return await queries.LeaderboardAsync(request, ct);
	}
}