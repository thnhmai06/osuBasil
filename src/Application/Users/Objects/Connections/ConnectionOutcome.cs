namespace Basil.Application.Users.Objects.Connections;

/// <summary>What happened to an operation on a connection that was not refused for lack of permissions.</summary>
public enum ConnectionOutcome : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok = 1,

	/// <summary>The client to act on is no longer online.</summary>
	TargetOffline,

	/// <summary>A user cannot do this to their own client.</summary>
	Self,

	/// <summary>The client's user blocked the caller, who has no right that overrides a block.</summary>
	Blocked,

	/// <summary>The caller's own connection closed while the operation was waiting.</summary>
	Closed,

	/// <summary>Only the client itself may do this; another connection tried.</summary>
	OtherClient,

	/// <summary>The operation ran but its input was rejected; the rejection says why.</summary>
	Rejected
}