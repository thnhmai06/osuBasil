namespace Basil.Application.Chat.Objects;

/// <summary>What happened when a connection acted on a chat channel.</summary>
public enum ChannelOutcome : byte
{
	/// <summary>The caller lacks the privileges the operation needs.</summary>
	Denied = 0,

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

	/// <summary>The message text holds a control character that chat does not carry.</summary>
	InvalidText,

	/// <summary>The recipient does not accept private messages from the author.</summary>
	Blocked,

	/// <summary>The author is silenced and may not post messages.</summary>
	Silenced,

	/// <summary>The recipient is silenced, so the channel refuses private messages.</summary>
	TargetSilenced,

	/// <summary>The kind of channel does not let this connection read or write it.</summary>
	AccessDenied,

	/// <summary>No channel has the name.</summary>
	NotFound
}