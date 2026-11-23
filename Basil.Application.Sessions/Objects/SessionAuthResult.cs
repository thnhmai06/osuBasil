namespace Basil.Application.Sessions.Objects;

public enum SessionAuthResult : byte
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