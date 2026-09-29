using Basil.Application.Models.Multiplayer;

namespace Basil.Application.Models.Notifications;

/// <summary>A player in the recipient's room skipped the current beatmap's intro.</summary>
/// <param name="Slot">The slot of the player who skipped.</param>
public sealed record PlayerSkipped(RoomSlot Slot) : Notification;