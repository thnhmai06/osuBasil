using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Scores;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints for listing, querying, and downloading scores.
/// </summary>
/// <remarks>
///     Score endpoints are publicly readable. Scores are only created by the in-game
///     score-submission pipeline, so there are no write endpoints.
/// </remarks>
internal static class ScoreRoutes
{
	/// <summary>
	///     Registers the `/scores` list, detail, and replay-download routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapScoreRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/scores", async ([FromQuery] int? page, [FromQuery] int? pageSize, IScoreRepository scores,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, IBeatmapRepository beatmaps,
				CancellationToken cancellationToken) =>
			{
				var (p, ps) = Pagination.Normalize(page, pageSize);
				var overqueried = await scores.FetchPageAsync((p - 1) * ps, ps + 1, cancellationToken);
				var totalRecords = await scores.FetchCountAsync(cancellationToken);
				var trimmed = Pagination.Trim(overqueried, p, ps, totalRecords);
				var views = new List<ScoreDetailView>(trimmed.Items.Count);
				foreach (var row in trimmed.Items)
					views.Add(await BuildDetailView(row, gameRegistry, ircRegistry, users, beatmaps, cancellationToken));
				return Results.Json(new PagedResult<ScoreDetailView>(trimmed.Page, trimmed.PageSize,
					trimmed.TotalRecords, views));
			})
			.WithGroupName("basilapi")
			.WithName("listScores")
			.WithSummary("List scores.")
			.WithDescription("""
			                 Returns a page of scores, newest first, each item the same shape as `GET /scores/{scoreId}`.

			                 Query params: `page` (default 1) and `pageSize` (default 50).
			                 """)
			.WithTags("Scores")
			.Produces<PagedResult<ScoreDetailView>>()
			.WithExample(StatusCodes.Status200OK, new PagedResult<ScoreDetailView>(1, 50, 1, [SampleScoreDetail()]));

		group.MapGet("/scores/{scoreId:long}", async (long scoreId, IScoreRepository scores,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, IBeatmapRepository beatmaps,
				CancellationToken cancellationToken) =>
			{
				var score = await scores.FetchByIdAsync(scoreId, cancellationToken);
				return score is null
					? Results.NotFound()
					: Results.Json(await BuildDetailView(score, gameRegistry, ircRegistry, users, beatmaps,
						cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getScore")
			.WithSummary("Get a score.")
			.WithDescription("""
			                 Returns the score's mode-specific hit counts, combo, total score, mods, the submitting `user`, and the played `beatmap`.

			                 `beatmap` is null when the played beatmap is no longer available on the server.

			                 Returns `404 Not Found` if no score with this id exists.
			                 """)
			.WithTags("Scores")
			.Produces<ScoreDetailView>()
			.WithExample(StatusCodes.Status200OK, SampleScoreDetail())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/scores/{scoreId:long}/replay", async (long scoreId, ReplayService replayService,
				CancellationToken cancellationToken) =>
			{
				var result = await replayService.FetchReplayFileAsync(scoreId, cancellationToken);
				return result.Code == ReplayFetchResultCode.NotFound
					? Results.NotFound()
					: Results.Bytes(result.Data!, "application/x-osu-replay");
			})
			.WithGroupName("basilapi")
			.WithName("downloadScoreReplay")
			.WithSummary("Download a score replay.")
			.WithDescription("""
			                 Serves the `.osr` replay file for the given score id, as `application/x-osu-replay`.

			                 This is the admin-friendly equivalent of the osu! client's own `GET /web/osu-getreplay.php`, which instead requires client-style login credentials.

			                 Returns `404 Not Found` if the score doesn't exist or has no stored replay.
			                 """)
			.WithTags("Scores")
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>
	///     Builds the public <see cref="ScoreDetailView" /> for a score row, resolving the submitting
	///     user's embed and the played beatmap's embed (null once the stored md5 no longer resolves).
	/// </summary>
	private static async Task<ScoreDetailView> BuildDetailView(ScoreRow row,
		ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
		IUserRepository users, IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var user = await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(row.UserId, gameRegistry, ircRegistry, users,
			cancellationToken);
		var beatmap = await MatchLiveSnapshotBuilder.ResolveBeatmapAsync(row.MapMd5, beatmaps, cancellationToken);

		return new ScoreDetailView(row.Id, user, beatmap!, row.Mode, row.Mods, row.Score, row.Accuracy,
			row.MaxCombo, row.N300, row.N100, row.N50, row.NGeki, row.NKatu, row.NMiss,
			Enum.Parse<Grade>(row.Grade), row.Perfect, row.PlayTime, row.SubmittedAt, row.RoundId, row.Team);
	}

	private static ScoreDetailView SampleScoreDetail()
	{
		var row = new ScoreRow(999, 3, MatchTeam.Red, "d41d8cd98f00b204e9800998ecf8427e", 4_850_213, 98.42, 1234,
			Mods.HardRock | Mods.Hidden, 720, 45, 3, 2, 12, 5, "A", GameMode.Standard,
			DateTime.Parse("2026-07-20T12:34:56Z"), 185, ClientFlags.Clean, 9, false,
			"3f2504e04f8964dfa8807de37b2c73e1", DateTime.Parse("2026-07-20T12:38:01Z"));

		var beatmapset = new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC",
			DateTime.Parse("2026-06-01T10:00:00Z"), DateTime.Parse("2026-06-01T10:00:00Z"), false, false,
			BeatmapStatus.Loved, 1);
		var beatmap = new BeatmapDetail(row.MapMd5, 654, "Extreme",
			new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42),
			new OsuBeatmapObjectCounts { Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 },
			false,
			beatmapset);

		return new ScoreDetailView(row.Id, new UserBrief(9, "Carol", Country.Us), beatmap, row.Mode, row.Mods,
			row.Score, row.Accuracy, row.MaxCombo, row.N300, row.N100, row.N50, row.NGeki, row.NKatu, row.NMiss,
			Enum.Parse<Grade>(row.Grade), row.Perfect, row.PlayTime, row.SubmittedAt, row.RoundId, row.Team);
	}
}