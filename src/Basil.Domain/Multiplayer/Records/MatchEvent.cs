using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A single lifecycle event recorded against a match.
/// </summary>
/// <param name="Match">The match the event is recorded against.</param>
/// <param name="Type">The kind of lifecycle event.</param>
/// <param name="OccurredAt">The date and time when the event occurred.</param>
/// <param name="Actor">The user who performed the action, if any.</param>
/// <param name="Target">The user the action affected, if any.</param>
/// <param name="Detail">Additional detail about the event, if any.</param>
public sealed record MatchEvent(
	Match Match,
	MatchEventType Type,
	DateTimeOffset OccurredAt,
	User? Actor = null,
	User? Target = null,
	string? Detail = null) : IMatchRecord;

/// <summary>
///     The kinds of match lifecycle events that get recorded against a match.
/// </summary>
public enum MatchEventType : byte
{
	/// <summary>The match was created.</summary>
	Created = 0,

	/// <summary>A referee was added to the match.</summary>
	RefAdded = 1,

	/// <summary>A referee was removed from the match.</summary>
	RefRemoved = 2,

	/// <summary>Match host privileges were granted to a player.</summary>
	HostGranted = 3,

	/// <summary>A player joined the match.</summary>
	PlayerJoined = 4,

	/// <summary>A player left the match.</summary>
	PlayerLeft = 5,

	/// <summary>A player was kicked from the match.</summary>
	Kicked = 6,

	/// <summary>The match was closed.</summary>
	Closed = 7
}