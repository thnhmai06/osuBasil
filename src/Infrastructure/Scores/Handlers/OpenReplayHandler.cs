using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class OpenReplayHandler(IScoreRepository scores, IReplayStorage replays)
	: IRequestHandler<OpenReplay, Stream?>
{
	public async ValueTask<Stream?> Handle(OpenReplay request, CancellationToken ct)
	{
		return await scores.GetAsync(request.ScoreId, ct) is { } score ? await replays.OpenAsync(score, ct) : null;
	}
}