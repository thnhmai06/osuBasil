using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Opens the replay frames of a score for <see cref="OpenReplayFrames" />.</summary>
internal sealed class OpenReplayFramesHandler(IScoreRepository scores, IReplayStorage replays)
	: IRequestHandler<OpenReplayFrames, Stream?>
{
	/// <inheritdoc />
	public async ValueTask<Stream?> Handle(OpenReplayFrames request, CancellationToken ct)
	{
		return await scores.GetAsync(request.ScoreId, ct) is { } score
			? await replays.OpenFramesAsync(score, ct)
			: null;
	}
}