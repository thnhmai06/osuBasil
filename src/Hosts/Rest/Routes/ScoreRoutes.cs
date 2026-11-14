using Basil.Application.Scores.Messages;
using Basil.Application.Shared.Paging;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

/// <summary>Scores and their replays.</summary>
internal static class ScoreRoutes
{
	/// <summary>Maps the Scores routes.</summary>
	public static void MapScores(this RouteGroupBuilder v1)
	{
		var scores = v1.MapGroup("/scores").WithTags("Scores");
		scores.MapGet("/", ListAsync).Admitted(WorkClass.Listing).WithSummary("List scores.")
			.WithDescription(
				"Filter by `beatmap` (a beatmap id) and `user` (a user id). Newest first, paged with `offset` and `limit`.");
		scores.MapGet("/{id:int}", GetAsync).WithSummary("Get a score.");
		scores.MapGet("/{id:int}/replay", ReplayAsync).RequireToken().WithSummary("Download the replay of a score.");
	}

	private static async Task<IResult> ListAsync(HttpContext context, int? beatmap, int? user, int? offset, int? limit,
		ISender sender)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var listing = await sender.Send(new ListScores(beatmap, user, page!), context.RequestAborted);
		return listing.Outcome switch
		{
			ScoreListOutcome.NoBeatmap => Api.NotFound("No such beatmap."),
			ScoreListOutcome.NoUser => Api.NotFound("No such user."),
			_ => Api.Paged(
				new Page<ScoreView>([.. listing.Scores!.Items.Select(ScoreView.Of)], listing.Scores.HasNext), page!)
		};
	}

	private static async Task<IResult> GetAsync(HttpContext context, int id, ISender sender)
	{
		return await sender.Send(new GetScore(id), context.RequestAborted) is { } score
			? Api.Ok(ScoreView.Of(score))
			: Api.NotFound("No such score.");
	}

	private static async Task<IResult> ReplayAsync(HttpContext context, int id, ISender sender)
	{
		if (await sender.Send(new GetScore(id), context.RequestAborted) is null) return Api.NotFound("No such score.");

		var stream = await sender.Send(new OpenReplay(id), context.RequestAborted);
		return stream is null
			? Api.NotFound("The score has no replay.")
			: Results.Stream(stream, "application/octet-stream", $"{id}.osr");
	}
}