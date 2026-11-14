using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Reads the report of a match for a caller who may view it.</summary>
internal sealed class GetMatchReportHandler(
	Lobby lobby,
	IMatchRepository matches,
	IMatchReportRepository reports,
	IRoundScoreRepository roundScores) : IRequestHandler<GetMatchReport, MatchReportSnapshot?>
{
	/// <inheritdoc />
	public async ValueTask<MatchReportSnapshot?> Handle(GetMatchReport request, CancellationToken ct)
	{
		if (await matches.GetAsync(request.MatchId, ct) is not { } match) return null;

		var visible = request.Actor is null
			? !match.Value.IsPrivate
			: await lobby.ScopeTo(request.Actor).CanViewAsync(match, ct);
		if (!visible) return null;

		var report = await reports.GetAsync(match, ct);
		var ordered = report.Records.OrderBy(entry => entry.Timestamp).ToList();
		var entries = new List<MatchReportEntry>();
		foreach (var entry in ordered.Skip(request.From))
			entries.Add(new MatchReportEntry(entry,
				entry is Round round ? await roundScores.ListAsync(round, ct) : []));
		return new MatchReportSnapshot(report.Match, ordered.Count, entries);
	}
}