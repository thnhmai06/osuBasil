using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Notifications;

/// <summary>A user stopped spectating the recipient.</summary>
/// <param name="Spectator">The session that stopped spectating.</param>
public sealed record SpectatorLeft(GameSession Spectator) : Notification;