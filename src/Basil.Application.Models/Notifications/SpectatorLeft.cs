namespace Basil.Application.Models.Notifications;

/// <summary>A user stopped spectating the recipient.</summary>
public sealed record SpectatorLeft(int UserId) : Notification;