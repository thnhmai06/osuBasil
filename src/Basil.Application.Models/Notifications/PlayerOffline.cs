namespace Basil.Application.Models.Notifications;

/// <summary>A user went offline.</summary>
public sealed record PlayerOffline(int UserId) : Notification;