using Basil.Domain.Users;

namespace Basil.Application.Models.Notifications;

/// <summary>A chat message was delivered to a channel or a user.</summary>
/// <param name="From">The user who sent the message.</param>
/// <param name="Target">The channel name or username the message was sent to.</param>
/// <param name="Text">The message body.</param>
public sealed record ChatNotification(User From, string Target, string Text) : Notification;