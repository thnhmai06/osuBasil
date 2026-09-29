using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>The recipient parted a channel.</summary>
/// <param name="Channel">The channel the recipient parted.</param>
public sealed record ChannelParted(ChannelSession Channel) : Notification;