using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Lists scores for <see cref="ListScores" />.</summary>
internal sealed class ListScoresHandler(IScoreRepository scores, IBeatmapRepository beatmaps, IUserRepository users)
	: IRequestHandler<ListScores, ScoreListing>
{
	/// <inheritdoc />
	public async ValueTask<ScoreListing> Handle(ListScores request, CancellationToken ct)
	{
		Md5? hash = null;
		if (request.BeatmapId is { } beatmapId)
		{
			if (await beatmaps.GetAsync(beatmapId, ct) is not { } map) return new ScoreListing(ScoreListOutcome.NoBeatmap, null);
			hash = map.Hash;
		}

		UserRecord? player = null;
		if (request.UserId is { } userId && (player = await users.GetAsync(userId, ct)) is null)
			return new ScoreListing(ScoreListOutcome.NoUser, null);

		return new ScoreListing(ScoreListOutcome.Ok,
			await scores.ListAsync(new ScoreQuery(player, hash), request.Page, ct));
	}
}