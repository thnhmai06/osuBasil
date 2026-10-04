namespace Basil.Application.Contracts.Chat;

/// <summary>The outcome of joining a chat channel.</summary>
public enum ChannelJoinResult : byte
{
	/// <summary>The connection joined the channel.</summary>
	Joined,

	/// <summary>The connection may not read the channel.</summary>
	NoPermission,

	/// <summary>The connection is already a member of the channel.</summary>
	AlreadyMember,

	/// <summary>The channel is closed.</summary>
	Closed
}

/// <summary>The outcome of leaving a chat channel.</summary>
public enum ChannelPartResult : byte
{
	/// <summary>The connection left the channel.</summary>
	Parted,

	/// <summary>The connection was not a member of the channel.</summary>
	NotMember
}

/// <summary>The outcome of posting to a chat channel.</summary>
public enum ChannelPostResult : byte
{
	/// <summary>The message was posted.</summary>
	Posted,

	/// <summary>The sender is silenced and cannot post.</summary>
	Silenced,

	/// <summary>The message text is empty.</summary>
	Empty,

	/// <summary>The sender is not a member of a channel that requires membership to post.</summary>
	NotMember,

	/// <summary>The sender may not write to the channel.</summary>
	NoWritePermission,

	/// <summary>The recipient is silenced and the channel refuses messages.</summary>
	TargetSilenced,

	/// <summary>The recipient does not accept private messages from the author.</summary>
	Blocked,

	/// <summary>The channel is closed.</summary>
	Closed
}

/// <summary>The outcome of starting to spectate a player.</summary>
public enum SpectateResult : byte
{
	/// <summary>The connection is now spectating the host.</summary>
	Spectating,

	/// <summary>The host's connection is closed.</summary>
	TargetOffline,

	/// <summary>A user cannot spectate themselves.</summary>
	Self,

	/// <summary>The connection is already spectating this host.</summary>
	AlreadySpectating
}