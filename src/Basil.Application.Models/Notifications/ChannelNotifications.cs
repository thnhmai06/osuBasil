using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>A notification about a channel.</summary>
/// <param name="Channel">The channel the notification is about.</param>
public abstract record ChannelNotification(ChannelSession Channel) : Notification;

/// <summary>A channel's metadata (topic or membership) changed.</summary>
/// <param name="Channel">The channel whose metadata changed.</param>
public sealed record ChannelInfoChanged(ChannelSession Channel) : ChannelNotification(Channel);

/// <summary>The recipient joined a channel.</summary>
/// <param name="Channel">The channel the recipient joined.</param>
public sealed record ChannelJoined(ChannelSession Channel) : ChannelNotification(Channel);

/// <summary>The recipient parted a channel.</summary>
/// <param name="Channel">The channel the recipient parted.</param>
public sealed record ChannelParted(ChannelSession Channel) : ChannelNotification(Channel);
