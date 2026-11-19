using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class GetScoreHandler(IScoreRepository scores) : IRequestHandler<GetScore, ScoreRecord?>
{
	public async ValueTask<ScoreRecord?> Handle(GetScore request, CancellationToken ct)
	{
		return await scores.GetAsync(request.ScoreId, ct);
	}
}