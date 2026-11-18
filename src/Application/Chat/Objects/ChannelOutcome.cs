namespace Basil.Application.Chat.Objects;

public enum ChannelOutcome : byte
{
	Denied = 0,

	Ok = 1,

	AlreadyMember,

	NotMember,

	Closed,

	Empty,

	InvalidText,

	Blocked,

	Silenced,

	TargetSilenced,

	AccessDenied,

	NotFound
}