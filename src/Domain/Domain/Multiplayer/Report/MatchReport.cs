namespace Basil.Domain.Multiplayer;

public sealed record MatchReport(MatchRecord Match, IReadOnlyList<IMatchEntry> Records);

public interface IMatchEntry
{
	MatchRecord Match { get; }

	DateTimeOffset Timestamp { get; }
}