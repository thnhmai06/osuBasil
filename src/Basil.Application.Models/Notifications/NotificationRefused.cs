namespace Basil.Application.Models.Notifications;

/// <summary>A chat message could not be delivered.</summary>
public sealed record NotificationRefused(string Target, RefusalReason Reason) : Notification;