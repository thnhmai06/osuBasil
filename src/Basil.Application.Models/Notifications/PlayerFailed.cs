namespace Basil.Application.Models.Notifications;

/// <summary>A player in the recipient's room failed the current round.</summary>
public sealed record PlayerFailed(int Slot) : Notification;