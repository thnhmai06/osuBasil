using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Application.Models.Notifications;

/// <summary>A round started in a room.</summary>
public sealed record RoundStarted(Room Room) : Notification;