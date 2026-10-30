using System.Text.Json;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Runtime;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;

namespace Basil.Host.Rest.Sse;

/// <summary>Match reports: the list for managers and the live report of one match.</summary>
internal static class MatchRoutes
{
	/// <summary>The full report of a match: the match and its entries in time order.</summary>
	public sealed record ReportView(MatchView Match, IReadOnlyList<object> Entries);

	public static void MapMatches(this RouteGroupBuilder v1)
	{
		var matches = v1.MapGroup("/matches").WithTags("Matches");
		matches.MapGet("/", ListAsync).RequireToken().WithSummary("List matches.")
			.WithDescription("For room managers only. `query` filters with `ended:` and `private:`. Paged with `offset` and `limit`.");
		matches.MapGet("/{id:int}", ReportAsync).Sse().WithSummary("Watch the report of a match.")
			.WithDescription("""
				The response is streamed using Server-Sent Events and needs no token; a private match is visible only to those who may view it, and everyone else gets 404.

				The first event is the full report with its entries, in time order. A match that has ended ends the stream after that event; a match in progress keeps sending bundles with the new entries. Each event carries the position of its last entry as `id`; reconnecting with the `Last-Event-ID` of the last entry of an ended match answers 204, which stops a browser from reconnecting. Data is up to about one second old.
				""");
	}

	private static async Task<IResult> ListAsync(HttpContext context, string? query, int? offset, int? limit, Lobby lobby)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var (result, found) = await lobby.ScopeTo(context.Connection()).ListMatchesAsync(
			MatchQuery.Parse(query ?? string.Empty), page!, context.RequestAborted);
		return result.IsOk
			? Api.Paged(new Page<MatchView>([.. found!.Items.Select(MatchView.Of)], found.Total), page!)
			: result.Refusal();
	}

	private static async Task<object> EntryOf(IMatchEntry entry, IRoundScoreRepository roundScores, CancellationToken ct)
	{
		switch (entry)
		{
			case Round round:
				var scores = await roundScores.ListAsync(round, ct);
				return new
				{
					type = "round", index = round.Index, timestamp = entry.Timestamp, beatmapHash = round.BeatmapHash.HashValue,
					mode = round.Settings.Mode, mods = round.Settings.Mods.ToString(), teamType = round.Settings.TeamType,
					aborted = round.Aborted, startedAt = round.StartedAt, endedAt = round.EndedAt,
					verdict = round.Decision is { } v
						? new { v.ScoreOrder, v.WinnerId, v.Winner, v.Margin, v.IsDraw, v.RedScore, v.BlueScore }
						: null,
					scores = scores.Select(s => new
					{
						userId = s.Score.Value.UserId, team = s.Team, totalScore = s.Score.Value.TotalScore,
						maxCombo = s.Score.Value.MaxCombo, accuracy = s.Score.Value.Accuracy, grade = s.Score.Value.Grade,
						passed = s.Score.Value.IsPassed, scoreId = s.Score.Id
					})
				};
			case MatchEvent e:
				return new
				{
					type = "event", eventType = e.Type, timestamp = e.Timestamp, actor = e.Actor is null ? null : UserRef.Of(e.Actor),
					target = e.Target is null ? null : UserRef.Of(e.Target), detail = e.Detail
				};
			default:
				throw new InvalidOperationException("Unknown match entry.");
		}
	}

	private static async Task<IResult> ReportAsync(HttpContext context, int id, IMatchRepository matches,
		IMatchReportRepository reports, IRoundScoreRepository roundScores, Lobby lobby)
	{
		var ct = context.RequestAborted;
		if (await matches.GetAsync(id, ct) is not { } match) return Api.NotFound("No such match.");

		if (!await IsVisibleAsync(context, lobby, match, ct)) return Api.NotFound("No such match.");

		var report = await reports.GetAsync(match, ct);
		var entries = report.Records.OrderBy(r => r.Timestamp).ToList();
		var lastId = entries.Count == 0 ? null : (entries.Count - 1).ToString();

		if (match.Value.EndedAt is not null) return await EndedAsync(context, reports, roundScores, match, lastId, ct);

		var sent = entries.Count;
		return await context.StreamAsync($"match:{id}", async token =>
		{
			var (data, snapshotId) = await SnapshotAsync(reports, roundScores, match, token);
			sent = ((ReportView)data).Entries.Count;
			return (data, snapshotId);
		}, null, async token =>
		{
			var (poll, total) = await PollAsync(reports, roundScores, match, sent, token);
			sent = total;
			return poll;
		});
	}

	private static async Task<bool> IsVisibleAsync(HttpContext context, Lobby lobby, MatchRecord match, CancellationToken ct)
	{
		var connection = context.ConnectionOrNull();
		return connection is null
			? !match.Value.IsPrivate
			: await lobby.ScopeTo(connection).CanViewAsync(match, ct);
	}

	private static async Task<(object, string?)> SnapshotAsync(IMatchReportRepository reports, IRoundScoreRepository roundScores,
		MatchRecord match, CancellationToken token)
	{
		var current = await reports.GetAsync(match, token);
		var list = new List<object>();
		foreach (var entry in current.Records.OrderBy(r => r.Timestamp)) list.Add(await EntryOf(entry, roundScores, token));
		return (new ReportView(MatchView.Of(match), list), list.Count == 0 ? null : (list.Count - 1).ToString());
	}

	// An ended match has no root: its report never changes, so the snapshot is built and the stream ends.
	private static async Task<IResult> EndedAsync(HttpContext context, IMatchReportRepository reports,
		IRoundScoreRepository roundScores, MatchRecord match, string? lastId, CancellationToken ct)
	{
		if (lastId is not null && context.Request.Headers["Last-Event-ID"].ToString() == lastId)
			return Results.StatusCode(StatusCodes.Status204NoContent);

		var (data, snapshotId) = await SnapshotAsync(reports, roundScores, match, ct);
		return new SnapshotOnlyResult(SseHub.Frame("snapshot", snapshotId, JsonSerializer.SerializeToUtf8Bytes(data, Api.Json)));
	}

	private static async Task<(SsePoll Poll, int Total)> PollAsync(IMatchReportRepository reports,
		IRoundScoreRepository roundScores, MatchRecord match, int sent, CancellationToken token)
	{
		var current = await reports.GetAsync(match, token);
		var all = current.Records.OrderBy(r => r.Timestamp).ToList();
		var fresh = new List<SseEvent>();
		for (var index = sent; index < all.Count; index++)
			fresh.Add(new SseEvent(await EntryOf(all[index], roundScores, token), index.ToString()));
		return (new SsePoll(fresh, current.Match.Value.EndedAt is not null), all.Count);
	}
}
