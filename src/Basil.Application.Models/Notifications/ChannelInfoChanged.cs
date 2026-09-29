using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>A channel's metadata (topic or membership) changed.</summary>
/// <param name="Channel">The channel whose metadata changed.</param>
public sealed record ChannelInfoChanged(ChannelSession Channel) : Notification;