using Basil.Application.Models.Multiplayer;

namespace Basil.Application.Models.Notifications;

/// <summary>A player in the recipient's room failed the current round.</summary>
/// <param name="Slot">The slot of the player who failed.</param>
public sealed record PlayerFailed(RoomSlot Slot) : Notification;