using Basil.Application.Scores.Logic;
using Basil.Application.Scores.Messages;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class SearchScoresHandler(ScoreQueries queries) : IRequestHandler<SearchScores, ScoreListing>
{
	public async ValueTask<ScoreListing> Handle(SearchScores request, CancellationToken ct)
	{
		return await queries.SearchAsync(request, ct);
	}
}