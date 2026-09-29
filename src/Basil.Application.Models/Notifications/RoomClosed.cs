using Basil.Application.Models.Multiplayer;

namespace Basil.Application.Models.Notifications;

/// <summary>A room closed.</summary>
/// <param name="Room">The room that closed.</param>
public sealed record RoomClosed(Room Room) : Notification;