using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer;

public sealed record MatchEvent(
	MatchRecord Match,
	MatchEventType Type,
	DateTimeOffset Timestamp,
	UserRecord? Actor = null,
	UserRecord? Target = null,
	string? Detail = null) : IMatchEntry
{
	public const string ServerStoppedDetail = "ServerStopped";

	public string? Detail
	{
		get;
		init => field = value?.ThrowIfHasNul();
	} = Detail?.ThrowIfHasNul();
}

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