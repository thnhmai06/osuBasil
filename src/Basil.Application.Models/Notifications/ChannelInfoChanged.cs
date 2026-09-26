namespace Basil.Application.Models.Notifications;

/// <summary>A channel's metadata (topic or membership) changed.</summary>
public sealed record ChannelInfoChanged(string Channel) : Notification;