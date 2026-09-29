using Basil.Domain.Users;

using Basil.Application.Common.Notifications;
namespace Basil.Application.Chat;

/// <summary>A notification about a chat message being delivered or refused.</summary>
public abstract record ChatDeliveryNotification : Notification;

/// <summary>A chat message was delivered to a channel or a user.</summary>
/// <param name="From">The user who sent the message.</param>
/// <param name="Target">The channel name or username the message was sent to.</param>
/// <param name="Text">The message body.</param>
public sealed record ChatNotification(User From, string Target, string Text) : ChatDeliveryNotification;

/// <summary>A chat message could not be delivered.</summary>
public sealed record NotificationRefused(string Target, RefusalReason Reason) : ChatDeliveryNotification;

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
