namespace Basil.Application.Models.Notifications;

/// <summary>A server-wide announcement.</summary>
public sealed record Announcement(string Text) : Notification;