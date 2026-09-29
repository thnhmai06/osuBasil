using Basil.Domain.Users;

using Basil.Application.Common.Notifications;
namespace Basil.Application.Sessions;

/// <summary>A notification about a user's presence or online state.</summary>
public abstract record PresenceNotification : Notification;

/// <summary>A user's presence (status, stats, or online state) changed and should be re-announced.</summary>
public sealed record PresenceChanged(GameSession Session) : PresenceNotification;

/// <summary>A user went offline.</summary>
/// <param name="User">The user who went offline.</param>
public sealed record PlayerOffline(User User) : PresenceNotification;
