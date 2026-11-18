namespace Basil.Application.Users.Objects.Connections;

public enum ConnectionOutcome : byte
{
	Denied = 0,

	Ok = 1,

	TargetOffline,

	Self,

	Blocked,

	Closed,

	OtherClient,

	Rejected
}