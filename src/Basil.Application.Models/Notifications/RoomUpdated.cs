using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Application.Models.Notifications;

/// <summary>A room's settings or slots changed.</summary>
public sealed record RoomUpdated(Room Room) : Notification;