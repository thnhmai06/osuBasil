namespace Basil.Application.Models.Notifications;

/// <summary>A user started spectating the recipient.</summary>
public sealed record SpectatorJoined(int UserId) : Notification;