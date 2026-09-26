namespace Basil.Application.Models.Notifications;

/// <summary>The reason a chat message was refused delivery.</summary>
public enum RefusalReason : byte
{
	/// <summary>The sender lacks write permission on the target channel.</summary>
	NoWritePermission,

	/// <summary>The sender is currently silenced.</summary>
	Silenced,

	/// <summary>The recipient only accepts messages from friends.</summary>
	RecipientRestrictsMessages,

	/// <summary>The recipient is away and did not receive the message.</summary>
	RecipientAway
}