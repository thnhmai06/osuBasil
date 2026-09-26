namespace Basil.Application.Models.Notifications;

/// <summary>A chat message could not be delivered.</summary>
public sealed record MessageRefused(string Target, RefusalReason Reason) : Notification;