namespace Basil.Application.Models.Notifications;

/// <summary>A room closed.</summary>
public sealed record RoomClosed(int RoomId) : Notification;