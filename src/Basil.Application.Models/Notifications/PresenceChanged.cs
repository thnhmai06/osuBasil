using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>A user's presence (status, stats, or online state) changed and should be re-announced.</summary>
public sealed record PresenceChanged(GameSession Session) : Notification;