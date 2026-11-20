using Basil.Domain.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Matches;

/// <summary>A notable event in a match, such as a player joining or the room closing.</summary>
/// <param name="Match">The match.</param>
/// <param name="Type">What happened.</param>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Actor">The user who caused it, or <see langword="null"/> when no user did.</param>
/// <param name="Target">The user it happened to, or <see langword="null"/> when it concerns no one in particular.</param>
/// <param name="Detail">Further text about the event, or <see langword="null"/>; it cannot contain a NUL character.</param>
public sealed record MatchEvent(
	MatchRecord Match,
	MatchEventType Type,
	DateTimeOffset Timestamp,
	UserRecord? Actor = null,
	UserRecord? Target = null,
	string? Detail = null) : IMatchEntry
{
	/// <summary>The detail recorded when a room closes because the server stopped.</summary>
	public const string ServerStoppedDetail = "Server stopped";

	/// <summary>Gets further text about the event, or <see langword="null"/>; a value with a NUL character is refused.</summary>
	public string? Detail
	{
		get;
		init => field = value?.ThrowIfHasNul();
	} = Detail?.ThrowIfHasNul();
}

/// <summary>The kind of a match event.</summary>
public enum MatchEventType : byte
{
	/// <summary>The room was created.</summary>
	Created = 0,

	/// <summary>A user was made a referee.</summary>
	RefAdded = 1,

	/// <summary>A user stopped being a referee.</summary>
	RefRemoved = 2,

	/// <summary>A user became the host.</summary>
	HostGranted = 3,

	/// <summary>A player took a slot.</summary>
	PlayerJoined = 4,

	/// <summary>A player left their slot.</summary>
	PlayerLeft = 5,

	/// <summary>A player was removed from their slot.</summary>
	Kicked = 6,

	/// <summary>The room closed.</summary>
	Closed = 7
}