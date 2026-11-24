using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Domain.Multiplayer.Report.Rounds;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class GetMatchReportHandler(
	Lobby lobby,
	IMatchRepository matches,
	IMatchReportRepository reports,
	IRoundScoreRepository roundScores) : IRequestHandler<GetMatchReport, MatchReportSnapshot?>
{
	public async ValueTask<MatchReportSnapshot?> Handle(GetMatchReport request, CancellationToken ct)
	{
		if (await matches.GetAsync(request.MatchId, ct) is not { } match) return null;

		var visible = request.Actor is null
			? !match.Value.IsPrivate
			: await lobby.ScopeTo(request.Actor).CanViewAsync(match, ct);
		if (!visible) return null;

		var report = await reports.GetAsync(match, ct);
		var ordered = report.Records.OrderBy(entry => entry.Timestamp).ToList();
		var shown = ordered.Skip(request.From).ToList();
		var scores = (await roundScores.ListAsync([.. shown.OfType<Round>()], ct)).ToLookup(score => score.Round.Index);
		var entries = shown.Select(entry => new MatchReportEntry(entry,
			entry is Round round ? [.. scores[round.Index]] : [])).ToList();
		return new MatchReportSnapshot(report.Match, ordered.Count, entries);
	}
}