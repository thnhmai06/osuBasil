namespace Basil.Application.Chat.Objects;

/// <summary>What a member may do in a channel beyond reading it.</summary>
[Flags]
public enum ChannelMemberRole : byte
{
	None = 0,

	/// <summary>The member manages the channel.</summary>
	Manager = 1,

	/// <summary>The member may write in the channel.</summary>
	Speaker = 2
}