namespace Basil.Domain.Multiplayer;

public interface IMatchEntry
{
	MatchRecord Match { get; }

	DateTimeOffset Timestamp { get; }
}