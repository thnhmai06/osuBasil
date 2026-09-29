namespace Basil.Application.Common.Notifications;

/// <summary>A server-wide announcement.</summary>
public sealed record Announcement(string Text) : Notification;
