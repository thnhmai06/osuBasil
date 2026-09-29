using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>The recipient joined a channel.</summary>
/// <param name="Channel">The channel the recipient joined.</param>
public sealed record ChannelJoined(ChannelSession Channel) : Notification;