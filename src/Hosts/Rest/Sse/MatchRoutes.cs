using System.Text.Json;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Sse;

/// <summary>Match reports: the list for managers and the live report of one match.</summary>
internal static class MatchRoutes
{
	/// <summary>Maps the Matches routes.</summary>
	public static void MapMatches(this RouteGroupBuilder v1)
	{
		var matches = v1.MapGroup("/matches").WithTags("Matches");
		matches.MapGet("/", ListAsync).Admitted(WorkClass.Listing).RequireToken().WithSummary("List matches.")
			.WithDescription(
				"For room managers only. `query` filters with `ended:` and `private:`. Paged with `offset` and `limit`.");
		matches.MapGet("/{id:int}", ReportAsync).Sse().WithSummary("Stream the report of a match.")
			.WithDescription("""
			                 The response is streamed using Server-Sent Events and needs no token; a private match is visible only to those who may view it, and everyone else gets 404.

			                 The first event is the full report with its entries, in time order. A match that has ended ends the stream after that event; a match in progress keeps sending bundles with the new entries. Each event carries the position of its last entry as `id`; reconnecting with the `Last-Event-ID` of the last entry of an ended match answers 204, which stops a browser from reconnecting. Data is up to about one second old.
			                 """);
	}

	private static async Task<IResult> ListAsync(HttpContext context, string? query, int? offset, int? limit,
		ISender sender)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var (result, found) = await sender.Send(
			new ListMatches(context.Connection(), MatchQuery.Parse(query ?? string.Empty), page!),
			context.RequestAborted);
		return result == RoomOutcome.Ok
			? Api.Paged(new Page<MatchView>([.. found!.Items.Select(MatchView.Of)], found.HasNext), page!)
			: result.Refusal();
	}

	private static object VerdictOf(RoundResult result)
	{
		var winnerId = (result as HeadToHeadResult)?.WinnerId;
		var team = result as TeamVersusResult;
		return new
		{
			ScoreOrder = result.Scores.Select(score => score.Score.Id), WinnerId = winnerId,
			team?.Winner,
			result.Margin, result.IsDraw, RedScore = team?.TeamScore[GameTeam.Red],
			BlueScore = team?.TeamScore[GameTeam.Blue]
		};
	}

	private static object EntryOf(MatchReportEntry reported)
	{
		switch (reported.Entry)
		{
			case Round round:
				return new
				{
					type = "round", index = round.Index, timestamp = reported.Entry.Timestamp,
					beatmapHash = round.BeatmapHash.HashValue,
					mode = round.Settings.Mode, mods = round.Settings.Mods.ToString(),
					teamType = round.Settings.TeamType,
					aborted = round.Aborted, startedAt = round.StartedAt, endedAt = round.EndedAt,
					verdict = round.Result is { } r ? VerdictOf(r) : null,
					scores = reported.Scores.Select(s => new
					{
						userId = s.Score.Value.UserId, team = s.Team, totalScore = s.Score.Value.LastState.TotalScore,
						maxCombo = s.Score.Value.LastState.MaxCombo, accuracy = s.Score.Value.Accuracy,
						grade = s.Score.Value.Grade,
						passed = s.Score.Value.IsPassed, scoreId = s.Score.Id
					})
				};
			case MatchEvent e:
				return new
				{
					type = "event", eventType = e.Type, timestamp = e.Timestamp,
					actor = e.Actor is null ? null : UserRef.Of(e.Actor),
					target = e.Target is null ? null : UserRef.Of(e.Target), detail = e.Detail
				};
			default:
				throw new InvalidOperationException("Unknown match entry.");
		}
	}

	private static async Task<IResult> ReportAsync(HttpContext context, int id, ISender sender)
	{
		var ct = context.RequestAborted;
		var connection = context.ConnectionOrNull();
		if (await sender.Send(new GetMatchReport(connection, id), ct) is not { } report)
			return Api.NotFound("No such match.");

		var lastId = report.Total == 0 ? null : (report.Total - 1).ToString();
		if (report.Match.Value.EndedAt is not null) return Ended(context, report, lastId);

		var sent = report.Total;
		return await context.StreamAsync($"match:{id}", async token =>
		{
			var snapshot = await sender.Send(new GetMatchReport(connection, id), token);
			if (snapshot is null) return (new ReportView(MatchView.Of(report.Match), []), null);
			sent = snapshot.Total;
			return (SnapshotOf(snapshot), snapshot.Total == 0 ? null : (snapshot.Total - 1).ToString());
		}, null, async token =>
		{
			var current = await sender.Send(new GetMatchReport(connection, id, sent), token);
			if (current is null) return new SsePoll([], true);
			var fresh = current.Entries
				.Select((entry, offset) => new SseEvent(EntryOf(entry), (sent + offset).ToString())).ToList();
			sent = current.Total;
			return new SsePoll(fresh, current.Match.Value.EndedAt is not null);
		});
	}

	private static ReportView SnapshotOf(MatchReportSnapshot report)
	{
		return new ReportView(MatchView.Of(report.Match), [.. report.Entries.Select(EntryOf)]);
	}

	// An ended match has no root: its report never changes, so the snapshot is built and the stream ends.
	private static IResult Ended(HttpContext context, MatchReportSnapshot report, string? lastId)
	{
		if (lastId is not null && context.Request.Headers["Last-Event-ID"].ToString() == lastId)
			return Results.StatusCode(StatusCodes.Status204NoContent);

		return new SnapshotOnlyResult(SseHub.Frame("snapshot", lastId,
			JsonSerializer.SerializeToUtf8Bytes(SnapshotOf(report), Api.Json)));
	}

	/// <summary>The full report of a match: the match and its entries in time order.</summary>
	public sealed record ReportView(MatchView Match, IReadOnlyList<object> Entries);
}