namespace Basil.Domain.Chat;

public enum ChannelJoinResult : byte
{
	Denied = 0,

	Ok = 1,

	AlreadyMember,

	NotMember,

	Closed,

	Blocked,

	Silenced,

	TargetSilenced,

	AccessDenied,

	NotFound
}