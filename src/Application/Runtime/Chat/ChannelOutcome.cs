namespace Basil.Application.Runtime.Chat;

/// <summary>What happened when a connection acted on a chat channel.</summary>
public enum ChannelOutcome : byte
{
	/// <summary>The action was done.</summary>
	Ok = 1,

	/// <summary>The connection is already a member of the channel.</summary>
	AlreadyMember,

	/// <summary>The connection is not a member of the channel.</summary>
	NotMember,

	/// <summary>The channel is closed.</summary>
	Closed,

	/// <summary>The message text is empty.</summary>
	Empty,

	/// <summary>The recipient does not accept private messages from the author.</summary>
	Blocked,

	/// <summary>The recipient may not post private messages, so the channel refuses messages.</summary>
	TargetSilenced,

	/// <summary>The kind of channel does not let this connection read or write it.</summary>
	AccessDenied
}
