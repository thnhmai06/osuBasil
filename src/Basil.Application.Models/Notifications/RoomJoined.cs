using Basil.Application.Models.Multiplayer;

namespace Basil.Application.Models.Notifications;

/// <summary>The recipient joined a room.</summary>
public sealed record RoomJoined(Room Room) : Notification;