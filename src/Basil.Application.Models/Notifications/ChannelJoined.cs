namespace Basil.Application.Models.Notifications;

/// <summary>The recipient joined a channel.</summary>
public sealed record ChannelJoined(string Channel) : Notification;