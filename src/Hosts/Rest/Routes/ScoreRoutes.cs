using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Common;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Application.Users.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;

namespace Basil.Host.Rest.Routes;

/// <summary>Scores and their replays.</summary>
internal static class ScoreRoutes
{
	public static void MapScores(this RouteGroupBuilder v1)
	{
		var scores = v1.MapGroup("/scores").WithTags("Scores");
		scores.MapGet("/", ListAsync).WithSummary("List scores.")
			.WithDescription(
				"Filter by `beatmap` (a beatmap id) and `user` (a user id). Newest first, paged with `offset` and `limit`.");
		scores.MapGet("/{id:int}", GetAsync).WithSummary("Retrieve a score.");
		scores.MapGet("/{id:int}/replay", ReplayAsync).RequireToken().WithSummary("Download the replay of a score.");
	}

	private static async Task<IResult> ListAsync(HttpContext context, int? beatmap, int? user, int? offset, int? limit,
		IScoreRepository scores, IBeatmapRepository beatmaps, IUserRepository users)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var ct = context.RequestAborted;
		Md5? hash = null;
		if (beatmap is { } beatmapId)
		{
			if (await beatmaps.GetAsync(beatmapId, ct) is not { } map) return Api.NotFound("No such beatmap.");
			hash = map.Hash;
		}

		UserRecord? player = null;
		if (user is { } userId && (player = await users.GetAsync(userId, ct)) is null)
			return Api.NotFound("No such user.");

		var found = await scores.ListAsync(new ScoreQuery(player, hash), page!, ct);
		return Api.Paged(new Page<ScoreView>([.. found.Items.Select(ScoreView.Of)], found.Total), page!);
	}

	private static async Task<IResult> GetAsync(HttpContext context, int id, IScoreRepository scores)
	{
		return await scores.GetAsync(id, context.RequestAborted) is { } score
			? Api.Ok(ScoreView.Of(score))
			: Api.NotFound("No such score.");
	}

	private static async Task<IResult> ReplayAsync(HttpContext context, int id, IScoreRepository scores,
		IReplayStorage replays)
	{
		if (await scores.GetAsync(id, context.RequestAborted) is not { } score) return Api.NotFound("No such score.");

		var stream = await replays.OpenAsync(score, context.RequestAborted);
		return stream is null
			? Api.NotFound("The score has no replay.")
			: Results.Stream(stream, "application/octet-stream", $"{id}.osr");
	}
}