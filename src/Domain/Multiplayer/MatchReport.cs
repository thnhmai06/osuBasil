namespace Basil.Domain.Multiplayer;

public sealed record MatchReport(MatchRecord Match, IReadOnlyList<IMatchEntry> Records);