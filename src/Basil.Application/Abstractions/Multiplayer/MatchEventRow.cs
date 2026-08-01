namespace Basil.Application.Abstractions.Multiplayer;

/// <summary>
///     The kinds of match lifecycle events that get recorded against a match.
/// </summary>
public enum MatchEventType
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

/// <summary>
///     A single lifecycle event recorded against a match.
/// </summary>
/// <param name="MatchId">The id of the match the event belongs to.</param>
/// <param name="EventType">The kind of event, one of the <see cref="MatchEventType" /> values.</param>
/// <param name="ActorUserId">The id of the user who triggered the event, or <see langword="null" />.</param>
/// <param name="ActorUserName">
///     The name of the user who triggered the event, denormalized onto the row for a self-contained
///     report.
/// </param>
/// <param name="TargetUserId">The id of the user the event acted on, or <see langword="null" />.</param>
/// <param name="TargetUserName">
///     The name of the user the event acted on, denormalized onto the row for a self-contained
///     report.
/// </param>
/// <param name="Timestamp">The time the event occurred, in UTC.</param>
/// <param name="Detail">Optional event-specific detail, such as the reason for a kick.</param>
public sealed record MatchEventRow(
	int MatchId,
	int EventType,
	int? ActorUserId,
	string? ActorUserName,
	int? TargetUserId,
	string? TargetUserName,
	DateTime Timestamp,
	string? Detail);