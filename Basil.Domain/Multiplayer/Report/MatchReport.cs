using Basil.Domain.Multiplayer.Report.Matches;

namespace Basil.Domain.Multiplayer.Report;

public sealed record MatchReport(MatchRecord Match, IReadOnlyCollection<IMatchEntry> Records);

public interface IMatchEntry
{
	MatchRecord Match { get; }

	DateTimeOffset Timestamp { get; }
}