using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Searches scores for <see cref="SearchScores" />.</summary>
internal sealed class SearchScoresHandler(IScoreRepository scores, IBeatmapRepository beatmaps, IUserRepository users)
	: IRequestHandler<SearchScores, ScoreListing>
{
	/// <inheritdoc />
	public async ValueTask<ScoreListing> Handle(SearchScores request, CancellationToken ct)
	{
		HashSet<Md5>? hashes = null;
		if (request.BeatmapId is { } beatmapId)
		{
			if (await beatmaps.GetAsync(beatmapId, ct) is not { } map)
				return new ScoreListing(ScoreListOutcome.NoBeatmap, null);
			hashes = [map.Hash];
		}

		if (request.Beatmaps is { } query)
		{
			var found = (await beatmaps.ListAsync(query, new PageRequest(0, ListLimits.Ceiling), ct)).Items
				.Select(beatmap => beatmap.Hash).ToHashSet();
			hashes = hashes is null ? found : [.. hashes.Where(found.Contains)];
		}

		HashSet<int>? userIds = null;
		if (request.User is { } reference)
		{
			var user = reference.Id is { } id ? await users.GetAsync(id, ct) : await users.GetByNameAsync(reference.Name!, ct);
			if (user is null) return new ScoreListing(ScoreListOutcome.NoUser, null);
			userIds = [user.Id];
		}

		return new ScoreListing(ScoreListOutcome.Ok,
			await scores.ListAsync(new ScoreQuery(UserIds: userIds, BeatmapHashes: hashes), request.Page, ct));
	}
}
