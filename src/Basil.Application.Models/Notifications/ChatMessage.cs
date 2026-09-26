namespace Basil.Application.Models.Notifications;

/// <summary>A chat message was delivered to a channel or a user.</summary>
public sealed record ChatMessage(int FromUserId, string Target, string Text) : Notification;