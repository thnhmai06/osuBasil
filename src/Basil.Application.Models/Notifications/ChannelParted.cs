namespace Basil.Application.Models.Notifications;

/// <summary>The recipient parted a channel.</summary>
public sealed record ChannelParted(string Channel) : Notification;