using Basil.Application.Common.Notifications;
namespace Basil.Application.Sessions;

/// <summary>A notification about a spectator relationship.</summary>
/// <param name="Session">The spectator session the notification is about.</param>
public abstract record SpectatorNotification(GameSession Session) : Notification;

/// <summary>A user started spectating the recipient.</summary>
/// <param name="Spectator">The session that started spectating.</param>
public sealed record SpectatorJoined(GameSession Spectator) : SpectatorNotification(Spectator);

/// <summary>A user stopped spectating the recipient.</summary>
/// <param name="Spectator">The session that stopped spectating.</param>
public sealed record SpectatorLeft(GameSession Spectator) : SpectatorNotification(Spectator);
