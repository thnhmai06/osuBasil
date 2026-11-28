namespace Basil.Application.Sessions.Objects;

public enum SessionAuthResult : byte
{
	Denied = 0,

	Ok = 1,

	TargetOffline,

	Blocked,

	Closed,

	OtherClient,

	Rejected
}