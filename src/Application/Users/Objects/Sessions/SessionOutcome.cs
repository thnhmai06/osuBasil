namespace Basil.Application.Users.Objects.Sessions;

public enum SessionOutcome : byte
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