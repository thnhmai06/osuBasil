using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A single lifecycle event recorded against a match.
/// </summary>
public sealed record MatchEvent(
	Match Match,
	MatchEventType Type,
	DateTimeOffset OccurredAt,
	User? Actor = null,
	User? Target = null,
	string? Detail = null) : IMatchEntry;

/// <summary>
///     The kinds of match lifecycle events that get recorded against a match.
/// </summary>
public enum MatchEventType : byte
{
	Created = 0,
	RefAdded = 1,
	RefRemoved = 2,
	HostGranted = 3,
	PlayerJoined = 4,
	PlayerLeft = 5,
	Kicked = 6,
	Closed = 7
}