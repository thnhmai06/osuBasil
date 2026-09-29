using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>A user started spectating the recipient.</summary>
/// <param name="Spectator">The session that started spectating.</param>
public sealed record SpectatorJoined(GameSession Spectator) : Notification;